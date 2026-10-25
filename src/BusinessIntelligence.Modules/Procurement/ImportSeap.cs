using System.Globalization;
using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Files;
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
    public const string ParserVersion = "seap-xlsx-v1";

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
        if (file.Kind is not (ContractsKind or DirectKind) || file.Year is null || file.Family is not ['Q', >= '1' and <= '4'])
        {
            throw new ArgumentException($"Catalog entry {file.Id} is not a quarterly SEAP export.", nameof(file));
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
        var header = contracts ? ContractHeader : DirectHeader;
        var quarter = (short)(file.Family![1] - '0');
        var counts = new Counts();

        database.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var source = RegistryImports.NewSourceFile(file, ParserVersion, time, "{}");
        database.SourceFiles.Add(source);
        await database.SaveChangesAsync(cancellationToken);

        await ImportOnrcFirme.ExecuteAsync(connection,
            "CREATE TEMP TABLE award_rows (LIKE procurement.awards INCLUDING DEFAULTS) ON COMMIT DROP", cancellationToken);
        await using (var writer = await connection.BeginBinaryImportAsync(Copy, cancellationToken))
        {
            var headerSeen = false;
            await foreach (var (row, cells) in StreamingXlsx.ReadAsync(path, header.Length, cancellationToken))
            {
                if (!headerSeen)
                {
                    var found = cells.Select(cell => cell?.Trim() ?? string.Empty).ToArray();
                    if (!found.SequenceEqual(header, StringComparer.Ordinal))
                    {
                        throw new InvalidDataException($"{file.FileName}: unexpected header {string.Join(" | ", found)}");
                    }

                    headerSeen = true;
                    continue;
                }

                // Contract exports repeat the header as machine labels on the second row.
                if (contracts && cells[0] == "DENUMIRE_AC")
                {
                    continue;
                }

                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                await WriteAsync(writer, source.Id, row, contracts, (short)file.Year, quarter, cells, counts, cancellationToken);
            }

            if (!headerSeen)
            {
                throw new InvalidDataException($"{file.FileName}: the sheet is empty.");
            }

            await writer.CompleteAsync(cancellationToken);
        }

        // Rows of a contract export that share award notice, lot, contract number and authority are the winners of one contract.
        const string Contract = "PARTITION BY authority_cui, authority_name, reference, lot, contract_number";
        var winners = contracts ? $"count(*) OVER ({Contract})::smallint, row_number() OVER ({Contract} ORDER BY row_number) = 1" : "winners, true";
        await ImportOnrcFirme.ExecuteAsync(connection, $"""
            INSERT INTO procurement.awards ({Columns}, first_winner)
            SELECT {Columns.Replace("winners", winners, StringComparison.Ordinal)}
            FROM award_rows
            """, cancellationToken);

        var kind = $"{file.Kind}-{file.Year}-{file.Family}";
        var previous = await RegistryImports.PublishAsync(database, kind, source.Id, time, cancellationToken);
        if (previous is { } old && old != source.Id)
        {
            await ImportOnrcFirme.ExecuteAsync(connection, "DELETE FROM procurement.awards WHERE source_file_id = @old", cancellationToken, ("old", old));
        }

        var report = new Dictionary<string, object>
        {
            ["rows"] = counts.Rows,
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

    private const string Columns = """
        source_file_id, row_number, kind, year, quarter, authority_name, authority_cui, reference, procedure,
            contract_type, cpv_code, cpv_name, title, lot, contract_number, published_on, contract_date, value_ron, eu_funded, programme,
            winner_name, winner_cui, winner_cui_state, winners
        """;

    private const string Copy = $"COPY award_rows ({Columns}) FROM STDIN (FORMAT BINARY)";

    private static async Task WriteAsync(NpgsqlBinaryImporter writer, int source, int row, bool contracts, short year, short quarter,
        string?[] cells, Counts counts, CancellationToken cancellationToken)
    {
        counts.Rows++;
        string? Cell(int index) => string.IsNullOrWhiteSpace(cells[index]) ? null : cells[index]!.Trim();

        var (winnerState, winnerCui) = Identifier(Cell(contracts ? 20 : 13));
        counts.WinnerStates[winnerState.ToString()] = counts.WinnerStates.GetValueOrDefault(winnerState.ToString()) + 1;
        // A personal numeric code identifies a person: neither it nor the winner's name is stored.
        var withheld = winnerState == CuiState.PersonalCode;
        counts.Withheld += withheld ? 1 : 0;
        var value = Amount(Cell(contracts ? 18 : 11));
        counts.MissingValues += value is null ? 1 : 0;

        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(source, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync(row, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync(contracts ? "contract" : "direct", NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(year, NpgsqlDbType.Smallint, cancellationToken);
        await writer.WriteAsync(quarter, NpgsqlDbType.Smallint, cancellationToken);
        await Text(Cell(0));
        await Number(Identifier(Cell(1)).Cui);
        await Text(Cell(contracts ? 13 : 2));
        await Text(contracts ? Cell(3) : null);
        await Text(Cell(contracts ? 8 : 7));
        await Text(Cell(contracts ? 10 : 5));
        await Text(Cell(contracts ? 11 : 6));
        await Text(contracts ? null : Cell(4));
        await Text(contracts ? Cell(12) : null);
        await Text(contracts ? Cell(16) : null);
        await Date(Cell(contracts ? 14 : 3));
        await Date(Cell(contracts ? 15 : 10));
        if (value is { } amount)
        {
            await writer.WriteAsync(amount, NpgsqlDbType.Numeric, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }

        bool? eu = contracts ? null : Cell(8) switch { "Da" or "DA" => true, "Nu" or "NU" => false, _ => null };
        if (eu is { } flag)
        {
            await writer.WriteAsync(flag, NpgsqlDbType.Boolean, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }

        await Text(contracts ? null : Cell(9));
        await Text(withheld ? null : Cell(contracts ? 19 : 12));
        await Number(winnerCui);
        await writer.WriteAsync(winnerState.ToString(), NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync((short)1, NpgsqlDbType.Smallint, cancellationToken);

        Task Text(string? text) => ImportOnrcFirme.WriteAsync(writer, text, cancellationToken);

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
            var date = StreamingXlsx.SerialDate(text)
                ?? (DateOnly.TryParseExact(text?.Split(' ')[0], ["dd.MM.yyyy", "yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null);
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
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? Math.Round(value, 2) : null;

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql, int source, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("source", source);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private sealed class Counts
    {
        public int Rows { get; set; }
        public int Withheld { get; set; }
        public int MissingValues { get; set; }
        public int UnreadableDates { get; set; }
        public Dictionary<string, int> WinnerStates { get; } = [];
    }
}
