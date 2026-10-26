using System.Globalization;
using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BusinessIntelligence.Modules.Procurement;

/// <summary>
/// Imports a quarterly SEAP export (contracts or direct purchases) into <c>procurement.awards</c>, one transaction per
/// file. A newer file for the same kind and quarter replaces the older one; the same file again is a no-op. Columns are
/// addressed by position because the published header repeats "Data publicare"; the header is checked first.
/// </summary>
public sealed class ImportSeap(AppDbContext database, TimeProvider time)
{
    public const string ContractsKind = "seap-contracts";
    public const string DirectKind = "seap-direct";
    public const string ParserVersion = "seap-v2";

    public static readonly string[] ContractHeader =
    [
        "Autoritate contractanta", "CUI autoritate contractanta", "Tip anunt", "Tip procedura", "Numar anunt initiere",
        "Data publicare", "Tip legislatie", "Tip incheiere contract", "Tip contract", "Tip criterii de atribuire",
        "Cod CPV", "Denumire CPV", "Numar lot", "Numar anunt atribuire", "Data publicare", "Data contract",
        "Numar contract", "Incheiat prin", "Valoare contract (RON)", "Ofertant castigator", "CUI ofertant castigator",
    ];

    public static readonly string[] DirectHeader =
    [
        "Autoritate contractanta", "CUI autoritate contractanta", "Numar achizitie directa", "Data publicare", "Denumire achizitie",
        "Cod CPV", "Denumire CPV", "Tip contract", "Finantare prin fonduri comunitare?", "Denumire program", "Data finalizare",
        "Valoare achizitie (RON)", "Ofertant castigator", "CUI ofertant castigator",
    ];

    public static IReadOnlyList<CatalogFile> Files() =>
        [.. SourceCatalog.All.Where(file => file.Kind is ContractsKind or DirectKind).OrderBy(file => file.Id, StringComparer.Ordinal)];

    public async Task<ImportResult> ExecuteAsync(string path, CatalogFile file, CancellationToken cancellationToken)
    {
        if (file.Kind is not (ContractsKind or DirectKind) || file.Year is null || file.Family is null)
        {
            throw new ArgumentException($"Catalog entry {file.Id} is not a SEAP export with a year and period.", nameof(file));
        }

        var existing = await database.SourceFiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Sha256 == file.Sha256 && item.ParserVersion == ParserVersion, cancellationToken);
        if (existing is not null)
        {
            return new ImportResult(ImportOutcome.AlreadyImported, existing.Id, new Dictionary<string, object>());
        }

        // A workbook is a zip that has to be read with seeking: hash it fully before opening it.
        await using (var verified = VerifiedFileStream.Open(path, file))
        {
            verified.EnsureComplete();
        }

        var contracts = file.Kind == ContractsKind;
        var quarter = Quarter(file.Family);
        var counts = new Counts();

        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var source = RegistryImports.NewSourceFile(file, ParserVersion, time, "{}");
        database.SourceFiles.Add(source);
        await database.SaveChangesAsync(cancellationToken);

        await ImportOnrcCompanies.ExecuteAsync(connection,
            "CREATE TEMP TABLE award_rows (LIKE procurement.awards INCLUDING DEFAULTS) ON COMMIT DROP", cancellationToken);
        await using (var writer = await connection.BeginBinaryImportAsync(Copy, cancellationToken))
        {
            await foreach (var row in SeapRows.ReadAsync(path, contracts, counts.Format, cancellationToken))
            {
                await WriteAsync(writer, source.Id, contracts, (short)file.Year, quarter, row, counts, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        if (counts.Rows == 0)
        {
            throw new InvalidDataException($"{file.FileName}: no award rows were found.");
        }

        // Rows of a contract export that share award notice, lot, contract number and authority are the winners of one contract.
        const string Contract = "PARTITION BY authority_cui, authority_name, reference, lot, contract_number";
        var winners = contracts ? $"count(*) OVER ({Contract})::smallint, row_number() OVER ({Contract} ORDER BY row_number) = 1" : "winners, true";
        winners += ", coalesce(contract_date, published_on)";
        await ImportOnrcCompanies.ExecuteAsync(connection, $"""
            INSERT INTO procurement.awards ({Columns}, first_winner, awarded_on)
            SELECT {Columns.Replace("winners", winners, StringComparison.Ordinal)}
            FROM award_rows
            """, cancellationToken);

        var kind = $"{file.Kind}-{file.Year}-{file.Family}";
        var previous = await RegistryImports.PublishAsync(database, kind, source.Id, time, cancellationToken);
        if (previous is { } old && old != source.Id)
        {
            await ImportOnrcCompanies.ExecuteAsync(connection, "DELETE FROM procurement.awards WHERE source_file_id = @old", cancellationToken, ("old", old));
        }

        var report = new Dictionary<string, object>
        {
            ["rows"] = counts.Rows,
            ["layout"] = counts.Format.Layout,
            ["windows1250Lines"] = counts.Format.Windows1250Lines,
            ["realignedRows"] = counts.Format.RealignedRows,
            ["skippedRows"] = counts.Format.SkippedRows,
            ["winnerCuiStates"] = counts.WinnerStates,
            ["withheldWinners"] = counts.Withheld,
            ["missingValues"] = counts.MissingValues,
            ["unreadableDates"] = counts.UnreadableDates,
            ["contractsWithSeveralWinners"] = contracts ? await ScalarAsync(connection,
                "SELECT count(*) FROM procurement.awards WHERE source_file_id = @source AND winners > 1", source.Id, cancellationToken) : 0L,
        };
        source.Report = JsonSerializer.Serialize(report);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await RegistryImports.VacuumAsync(database, "procurement.awards", cancellationToken);
        return new ImportResult(ImportOutcome.Published, source.Id, report);
    }

    /// <summary>The quarter of a period code: Q1–Q4 (with an optional part suffix); 0 for a half-year (S1, S2) or a whole year (Y).</summary>
    public static short Quarter(string family) => family is ['Q', >= '1' and <= '4', ..] ? (short)(family[1] - '0') : (short)0;

    private const string Columns = """
        source_file_id, row_number, kind, year, quarter, authority_name, authority_cui, reference, procedure,
            contract_type, cpv_code, cpv_name, title, lot, contract_number, published_on, contract_date, value_ron, eu_funded, programme,
            winner_name, winner_cui, winner_cui_state, winners
        """;

    private const string Copy = $"COPY award_rows ({Columns}) FROM STDIN (FORMAT BINARY)";

    private static async Task WriteAsync(NpgsqlBinaryImporter writer, int source, bool contracts, short year, short quarter,
        SeapRow row, Counts counts, CancellationToken cancellationToken)
    {
        counts.Rows++;
        var (winnerState, winnerCui) = Identifier(row.WinnerCui);
        counts.WinnerStates[winnerState.ToString()] = counts.WinnerStates.GetValueOrDefault(winnerState.ToString()) + 1;
        // A personal numeric code identifies a person: neither it nor the winner's name is stored.
        var withheld = winnerState == CuiState.PersonalCode;
        counts.Withheld += withheld ? 1 : 0;
        var value = Amount(row.Value);
        counts.MissingValues += value is null ? 1 : 0;

        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(source, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync(row.Row, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync(contracts ? "contract" : "direct", NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(year, NpgsqlDbType.Smallint, cancellationToken);
        await writer.WriteAsync(quarter, NpgsqlDbType.Smallint, cancellationToken);
        await Text(row.AuthorityName);
        await Number(Identifier(row.AuthorityCui).Cui);
        await Text(row.Reference);
        await Text(row.Procedure);
        await Text(row.ContractType);
        await Text(row.CpvCode);
        await Text(row.CpvName);
        await Text(row.Title);
        await Text(row.Lot);
        await Text(row.ContractNumber);
        await Date(row.Published);
        await Date(row.ContractDate);
        if (value is { } amount)
        {
            await writer.WriteAsync(amount, NpgsqlDbType.Numeric, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }

        bool? eu = row.EuFunded?.ToUpperInvariant() switch { "DA" => true, "NU" => false, _ => null };
        if (eu is { } flag)
        {
            await writer.WriteAsync(flag, NpgsqlDbType.Boolean, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }

        await Text(row.Programme);
        await Text(withheld ? null : row.WinnerName);
        await Number(winnerCui);
        await writer.WriteAsync(winnerState.ToString(), NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync((short)1, NpgsqlDbType.Smallint, cancellationToken);

        Task Text(string? text) => ImportOnrcCompanies.WriteAsync(writer, text, cancellationToken);

        async Task Number(long? number)
        {
            if (number is { } present)
            {
                await writer.WriteAsync(present, NpgsqlDbType.Bigint, cancellationToken);
            }
            else
            {
                await writer.WriteNullAsync(cancellationToken);
            }
        }

        async Task Date(string? text)
        {
            var date = SeapRows.Date(text, counts.Format);
            counts.UnreadableDates += text is not null && date is null ? 1 : 0;
            if (date is { } present)
            {
                await writer.WriteAsync(present, NpgsqlDbType.Date, cancellationToken);
            }
            else
            {
                await writer.WriteNullAsync(cancellationToken);
            }
        }
    }

    /// <summary>A CUI as SEAP writes it: digits, sometimes with an RO prefix and spaces.</summary>
    public static (CuiState State, long? Cui) Identifier(string? text)
    {
        if (text is null)
        {
            return (CuiState.Missing, null);
        }

        var compact = new string([.. text.Where(character => !char.IsWhiteSpace(character))]);
        if (compact.StartsWith("RO", StringComparison.OrdinalIgnoreCase))
        {
            compact = compact[2..];
        }

        // Numeric cells can come back as "12345678.0" or in exponent form from the workbook.
        if (decimal.TryParse(compact, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == decimal.Truncate(number) && number > 0)
        {
            compact = number.ToString("0", CultureInfo.InvariantCulture);
        }

        return RomanianCui.Parse(compact);
    }

    public static decimal? Amount(string? text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? Math.Round(value, 2)
        // Older exports write "1.234,56".
        : decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("ro-RO"), out var local) ? Math.Round(local, 2)
        : null;

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql, int source, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("source", source);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private sealed class Counts
    {
        public SeapRows.FileFormat Format { get; } = new();
        public int Rows { get; set; }
        public int Withheld { get; set; }
        public int MissingValues { get; set; }
        public int UnreadableDates { get; set; }
        public Dictionary<string, int> WinnerStates { get; } = [];
    }
}
