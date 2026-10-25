using System.Globalization;
using System.Text;
using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BusinessIntelligence.Modules.Financials;

/// <summary>
/// Imports one year and family of MF financial statements (data file plus its dictionary) in one transaction. A newer
/// file for the same year and family replaces the previous rows; readers see either the old or the new set.
/// </summary>
public sealed class ImportMfFinancials(AppDbContext database, TimeProvider time)
{
    public const string DataKind = "mf-financials-data";
    public const string DictionaryKind = "mf-financials-dictionary";
    public const string ParserVersion = "mf-financials-v2";

    /// <summary>
    /// Employee counts outside 0–1,000,000 are filler values (2011 has 999999999999999); they are stored as missing.
    /// No Romanian employer comes near the limit.
    /// </summary>
    public const int MaximumEmployees = 1_000_000;

    /// <summary>
    /// Amounts at or above 1 trillion lei are filler values (2020 has 300,000,000,000,000); the largest real turnover is
    /// about 56 billion. They are stored as missing and flagged.
    /// </summary>
    public const decimal MaximumAmount = 1_000_000_000_000m;

    /// <summary>Statements up to 2024 use CAEN Rev.2; 2025 mixes Rev.2 and Rev.3, so its version stays unknown.</summary>
    public const short LastRev2Year = 2024;

    public async Task<ImportResult> ExecuteAsync(CatalogFile data, CatalogFile dictionary, string rawRoot, CancellationToken cancellationToken)
    {
        if (data.Kind != DataKind || dictionary.Kind != DictionaryKind || data.Year is not { } year || data.Family is not { } family
            || dictionary.Year != year || dictionary.Family != family)
        {
            throw new ArgumentException($"{data.Id} and {dictionary.Id} are not the data and dictionary of one year and family.");
        }

        var existing = await database.SourceFiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Sha256 == data.Sha256 && item.ParserVersion == ParserVersion, cancellationToken);
        if (existing is not null)
        {
            return new ImportResult(ImportOutcome.AlreadyImported, existing.Id, new Dictionary<string, object>());
        }

        var (columns, corrections) = MfIndicatorMap.Parse(
            await VerifiedFileStream.ReadAllAsync(Path.Combine(rawRoot, dictionary.LocalPath!), dictionary, cancellationToken));

        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var source = RegistryImports.NewSourceFile(data, ParserVersion, time, "{}");
        database.SourceFiles.Add(source);
        await database.SaveChangesAsync(cancellationToken);

        await Execute(connection, Sql.CreateStaging, cancellationToken);
        var counts = new Counts();
        await using (var stream = VerifiedFileStream.Open(Path.Combine(rawRoot, data.LocalPath!), data))
        {
            await CopyAsync(connection, stream, columns, counts, cancellationToken);
            stream.EnsureComplete();
        }

        var implausibleEmployees = await Scalar(connection, Sql.ImplausibleEmployees, cancellationToken);
        var implausibleAmounts = await Scalar(connection, Sql.ImplausibleAmounts, cancellationToken);
        var previous = await RegistryImports.PublishAsync(database, $"mf-financials-{year}-{family}", source.Id, time, cancellationToken);
        var replaced = await Execute(connection, "DELETE FROM financials.statements WHERE year = @year AND family = @family", cancellationToken,
            ("year", year), ("family", family));
        var inserted = await Execute(connection, Sql.Insert, cancellationToken, ("year", year), ("family", family), ("source", source.Id),
            ("version", year <= LastRev2Year ? (object)(short)2 : DBNull.Value));

        var report = new Dictionary<string, object>
        {
            ["year"] = year,
            ["family"] = family,
            ["rows"] = counts.Rows,
            ["statements"] = inserted,
            ["replacedStatements"] = replaced,
            ["previousSourceFileId"] = previous ?? 0,
            ["duplicateRows"] = counts.Rows - inserted,
            ["implausibleEmployeeCounts"] = implausibleEmployees,
            ["implausibleAmountRows"] = implausibleAmounts,
            ["dictionaryCorrections"] = corrections,
            ["indicators"] = columns,
        };
        source.Report = JsonSerializer.Serialize(report);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ImportResult(ImportOutcome.Published, source.Id, report);
    }

    private static async Task CopyAsync(NpgsqlConnection connection, Stream stream, IReadOnlyList<string> columns, Counts counts, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 1 << 16, leaveOpen: true);
        var header = (await reader.ReadLineAsync(cancellationToken))?.Split(',').Select(cell => cell.Trim().ToUpperInvariant()).ToArray();
        var expected = Sql.KeyColumns.Concat(columns.Select((_, index) => "I" + (index + 1).ToString(CultureInfo.InvariantCulture))).ToArray();
        if (header is null || !header.SequenceEqual(expected))
        {
            throw new InvalidDataException("MF data header does not match its dictionary: " + string.Join(',', header ?? []));
        }

        var targets = columns.Select(column => Array.IndexOf(Sql.Indicators, column)).ToArray();
        await using var writer = await connection.BeginBinaryImportAsync(Sql.Copy, cancellationToken);
        var values = new string?[Sql.Indicators.Length];
        var lineNumber = 1;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            var cells = line.Split(',');
            if (cells.Length != expected.Length)
            {
                throw new InvalidDataException($"MF data line {lineNumber}: expected {expected.Length} cells, found {cells.Length}.");
            }

            var (state, cui) = Registry.RomanianCui.Parse(cells[0]);
            if (state != Registry.CuiState.Valid)
            {
                throw new InvalidDataException($"MF data line {lineNumber}: CUI '{cells[0]}' is {state}.");
            }

            counts.Rows++;
            Array.Clear(values);
            for (var index = 0; index < targets.Length; index++)
            {
                var cell = cells[index + 2].Trim();
                values[targets[index]] = cell.Length == 0 ? null : cell;
            }

            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(lineNumber, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(cui!.Value, NpgsqlDbType.Bigint, cancellationToken);
            var caen = cells[1].Trim();
            if (caen.Length == 0)
            {
                await writer.WriteNullAsync(cancellationToken);
            }
            else
            {
                // The files drop the leading zero of CAEN codes (111 for 0111).
                await writer.WriteAsync(caen.PadLeft(4, '0'), NpgsqlDbType.Text, cancellationToken);
            }

            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] is not { } text)
                {
                    await writer.WriteNullAsync(cancellationToken);
                }
                else if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                {
                    throw new InvalidDataException($"MF data line {lineNumber}: '{text}' is not a whole number.");
                }
                else
                {
                    await writer.WriteAsync(number, NpgsqlDbType.Numeric, cancellationToken);
                }
            }
        }

        await writer.CompleteAsync(cancellationToken);
    }

    private static async Task<int> Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> Scalar(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private sealed class Counts
    {
        public int Rows { get; set; }
    }

    private static class Sql
    {
        public static readonly string[] KeyColumns = ["CUI", "CAEN"];

        public static readonly string ImplausibleEmployees = $"SELECT count(*) FROM statement_rows WHERE employees < 0 OR employees > {MaximumEmployees}";

        public static readonly string[] Indicators =
        [
            nameof(FinancialStatement.FixedAssets), nameof(FinancialStatement.CurrentAssets), nameof(FinancialStatement.Inventories),
            nameof(FinancialStatement.Receivables), nameof(FinancialStatement.Cash), nameof(FinancialStatement.PrepaidExpenses),
            nameof(FinancialStatement.Liabilities), nameof(FinancialStatement.DeferredIncome), nameof(FinancialStatement.Provisions),
            nameof(FinancialStatement.Equity), nameof(FinancialStatement.ShareCapital), nameof(FinancialStatement.RegiePatrimony),
            nameof(FinancialStatement.PublicPatrimony), nameof(FinancialStatement.Turnover), nameof(FinancialStatement.TotalIncome),
            nameof(FinancialStatement.TotalExpenses), nameof(FinancialStatement.GrossProfit), nameof(FinancialStatement.GrossLoss),
            nameof(FinancialStatement.NetProfit), nameof(FinancialStatement.NetLoss), nameof(FinancialStatement.Employees),
        ];

        private static readonly string ColumnList = string.Join(", ", Indicators.Select(Platform.Persistence.SnakeCaseNames.Convert));

        public static readonly string CreateStaging =
            $"CREATE TEMP TABLE statement_rows (line integer NOT NULL, cui bigint NOT NULL, caen_code text, {string.Join(", ", Indicators.Select(name => Platform.Persistence.SnakeCaseNames.Convert(name) + " numeric"))}) ON COMMIT DROP";

        public static readonly string Copy = $"COPY statement_rows (line, cui, caen_code, {ColumnList}) FROM STDIN (FORMAT BINARY)";

        private static readonly string[] Amounts = [.. Indicators.Where(name => name != nameof(FinancialStatement.Employees)).Select(Platform.Persistence.SnakeCaseNames.Convert)];

        private static readonly string ImplausibleAmount = string.Join(" OR ", Amounts.Select(column => $"abs({column}) >= {MaximumAmount}"));

        public static readonly string ImplausibleAmounts = $"SELECT count(*) FROM statement_rows WHERE {ImplausibleAmount}";

        // Repeated CUIs in one file are identical in every published year; if they ever differ, the row with the most
        // filled cells wins, then the later line.
        public static readonly string Insert = $"""
            INSERT INTO financials.statements (year, cui, family, caen_code, caen_version, source_file_id, {ColumnList}, quality_flags)
            SELECT DISTINCT ON (cui) @year, cui, @family, caen_code, @version, @source,
                {string.Join(", ", Amounts.Select(column => $"CASE WHEN abs({column}) < {MaximumAmount} THEN {column} END"))},
                CASE WHEN employees BETWEEN 0 AND {MaximumEmployees} THEN employees::integer END,
                array_remove(ARRAY[
                    CASE WHEN {ImplausibleAmount} THEN 'amount_out_of_range' END,
                    CASE WHEN employees < 0 OR employees > {MaximumEmployees} THEN 'employees_out_of_range' END,
                    CASE WHEN employees BETWEEN 1000 AND {MaximumEmployees} AND turnover < employees * 1000 THEN 'employees_unlikely' END
                ], NULL)
            FROM statement_rows
            ORDER BY cui, num_nonnulls({ColumnList}) DESC, line DESC
            """;
    }
}
