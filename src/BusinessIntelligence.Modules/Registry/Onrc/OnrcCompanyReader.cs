using System.Globalization;
using System.Text;

namespace BusinessIntelligence.Modules.Registry.Onrc;

public sealed record CompanyRow(
    int RowNumber, string RegistrationNumber, string? Euid, string? Name, long? Cui, CuiState CuiState, string? LegalForm,
    DateOnly? RegistrationDate, string? Country, string? County, string? Locality, string? Street, string? StreetNumber,
    string? Block, string? Staircase, string? Floor, string? Apartment, string? PostalCode, string? Sector,
    string? AddressDetails, string? Website, string? ParentCountry);

public sealed class CompanyReadCounts
{
    public int Rows { get; set; }
    public int WithoutRegistrationNumber { get; set; }
    public int InvalidRegistrationDate { get; set; }
    public Dictionary<string, int> CuiStates { get; } = [];
}

/// <summary>
/// Streams ONRC OD_FIRME.CSV: UTF-8 (BOM), '^'-delimited, unquoted, 20 columns. Values are trimmed and blank becomes null.
/// Personal numeric codes found in the CUI column are dropped here, before anything is stored.
/// </summary>
public static class OnrcCompanyReader
{
    public const string ParserVersion = "onrc-firme-v1";

    private static readonly string[] Header =
    [
        "DENUMIRE", "CUI", "COD_INMATRICULARE", "DATA_INMATRICULARE", "EUID", "FORMA_JURIDICA", "ADR_TARA", "ADR_JUDET",
        "ADR_LOCALITATE", "ADR_DEN_STRADA", "ADR_NR_STRADA", "ADR_BLOC", "ADR_SCARA", "ADR_ETAJ", "ADR_APARTAMENT",
        "ADR_COD_POSTAL", "ADR_SECTOR", "ADR_COMPLETARE", "WEB", "TARA_FIRMA_MAMA",
    ];

    private static readonly string[] DateFormats = ["dd/MM/yyyy", "dd/MM/yyyy HH:mm:ss"];

    public static async IAsyncEnumerable<CompanyRow> ReadAsync(
        Stream stream, CompanyReadCounts counts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16, leaveOpen: true);
        var header = await reader.ReadLineAsync(cancellationToken);
        if (header is null || !header.Split('^').SequenceEqual(Header))
        {
            throw new InvalidDataException("OD_FIRME: unexpected header.");
        }

        var rowNumber = 1;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            rowNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('^');
            if (fields.Length != Header.Length)
            {
                throw new InvalidDataException($"OD_FIRME row {rowNumber}: expected {Header.Length} fields, found {fields.Length}.");
            }

            counts.Rows++;
            var registrationNumber = Clean(fields[2]);
            if (registrationNumber is null)
            {
                counts.WithoutRegistrationNumber++;
                continue;
            }

            var (cuiState, cui) = RomanianCui.Parse(fields[1]);
            var state = cuiState.ToString();
            counts.CuiStates[state] = counts.CuiStates.GetValueOrDefault(state) + 1;
            DateOnly? registered = null;
            if (Clean(fields[3]) is { } dateText)
            {
                if (DateTime.TryParseExact(dateText, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    registered = DateOnly.FromDateTime(parsed);
                }
                else
                {
                    counts.InvalidRegistrationDate++;
                }
            }

            yield return new CompanyRow(
                rowNumber, registrationNumber, Clean(fields[4]), Clean(fields[0]), cui, cuiState, Clean(fields[5]), registered,
                Clean(fields[6]), Clean(fields[7]), Clean(fields[8]), Clean(fields[9]), Clean(fields[10]), Clean(fields[11]),
                Clean(fields[12]), Clean(fields[13]), Clean(fields[14]), Clean(fields[15]), Clean(fields[16]), Clean(fields[17]),
                Clean(fields[18]), Clean(fields[19]));
        }
    }

    private static string? Clean(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
