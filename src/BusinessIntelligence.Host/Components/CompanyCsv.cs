using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using BusinessIntelligence.Modules.Risk;
using BusinessIntelligence.Modules.Search;

namespace BusinessIntelligence.Host.Components;

/// <summary>
/// Search results for spreadsheets, as XLSX (opens in Excel whatever its regional list separator) or as standard CSV
/// (comma-separated UTF-8 with a byte order mark). Numbers stay numbers, a missing value is an empty cell, never 0, and
/// the sources with their licenses come with the data: a second sheet in XLSX, the last line in CSV.
/// </summary>
public static class CompanyCsv
{
    public static readonly string[] Header =
    [
        "CUI", "Nr. înregistrare", "Denumire", "Formă juridică", "Stare", "Data înregistrării", "Localitate", "Județ",
        "An financiar", "Cifra de afaceri (lei)", "Salariați", "Rezultat net (lei)", "Semnale de atenție",
    ];

    /// <summary>One row per hit: text cells as strings, numeric cells as numbers, missing values as null.</summary>
    public static IReadOnlyList<object?[]> Rows(SearchResult result, IReadOnlyDictionary<string, string[]> signals, Func<string, string> lifecycle) =>
        [.. result.Hits.Select(hit => new object?[]
        {
            hit.Cui, hit.RegistrationNumber, hit.Name, hit.LegalForm, lifecycle(hit.Lifecycle),
            hit.FirstRegistrationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), hit.Place, hit.County,
            hit.FinancialYear, hit.Turnover, hit.Employees, hit.NetResult,
            string.Join(", ", signals.GetValueOrDefault(hit.CompanyKey, []).Select(key => RiskSignals.Get(key).Name)) is { Length: > 0 } names ? names : null,
        })];

    /// <summary>What the file holds and where it comes from.</summary>
    public static string Sources(SearchResult result, DateOnly exportedOn)
    {
        var shown = result.Hits.Count < result.Total
            ? $"primele {result.Hits.Count.ToString(CultureInfo.InvariantCulture)} din {result.Total.ToString(CultureInfo.InvariantCulture)} rezultate"
            : $"{result.Hits.Count.ToString(CultureInfo.InvariantCulture)} rezultate";
        return $"Export din {exportedOn.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}, {shown}. Surse: Oficiul Național al Registrului Comerțului (CC BY 4.0); "
            + "Ministerul Finanțelor, situațiile financiare anuale de pe data.gov.ro (CC BY 4.0 sau Open Government Licence, după an); ANAF, serviciul web de stare fiscală. "
            + "Semnalele sunt fapte din datele publice, nu un rating.";
    }

    public static byte[] Write(SearchResult result, IReadOnlyDictionary<string, string[]> signals, Func<string, string> lifecycle, DateOnly exportedOn)
    {
        var text = new StringBuilder();
        Line(text, Header);
        foreach (var row in Rows(result, signals, lifecycle))
        {
            Line(text, row.Select(value => value switch
            {
                null => null,
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            }));
        }

        Line(text, [Sources(result, exportedOn)]);
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    /// <summary>A workbook with the results on "Firme" (header row frozen, filters on) and the sources on "Surse".</summary>
    public static byte[] WriteXlsx(SearchResult result, IReadOnlyDictionary<string, string[]> signals, Func<string, string> lifecycle, DateOnly exportedOn)
    {
        var rows = Rows(result, signals, lifecycle);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            Entry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Entry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Entry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets><sheet name="Firme" sheetId="1" r:id="rId1"/><sheet name="Surse" sheetId="2" r:id="rId2"/></sheets>
                <definedNames><definedName name="_xlnm._FilterDatabase" localSheetId="0" hidden="1">Firme!$A$1:$M$1</definedName></definedNames>
                </workbook>
                """);
            Entry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
                <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            // Style 1: bold header; style 2: whole numbers with thousands separators.
            Entry(archive, "xl/styles.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
                <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                <cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="3" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs>
                </styleSheet>
                """);

            var sheet = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
            sheet.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
            sheet.Append("""<cols><col min="1" max="2" width="16" customWidth="1"/><col min="3" max="3" width="40" customWidth="1"/><col min="4" max="8" width="14" customWidth="1"/><col min="9" max="12" width="16" customWidth="1"/><col min="13" max="13" width="40" customWidth="1"/></cols><sheetData>""");
            Row(sheet, 1, Header, bold: true);
            for (var index = 0; index < rows.Count; index++)
            {
                Row(sheet, index + 2, rows[index], bold: false);
            }

            sheet.Append("</sheetData>");
            sheet.Append(CultureInfo.InvariantCulture, $"""<autoFilter ref="A1:M{rows.Count + 1}"/></worksheet>""");
            Entry(archive, "xl/worksheets/sheet1.xml", sheet.ToString());

            var sources = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><cols><col min="1" max="1" width="140" customWidth="1"/></cols><sheetData>""");
            Row(sources, 1, [Sources(result, exportedOn)], bold: false);
            sources.Append("</sheetData></worksheet>");
            Entry(archive, "xl/worksheets/sheet2.xml", sources.ToString());
        }

        return buffer.ToArray();
    }

    private static void Row(StringBuilder sheet, int number, IReadOnlyList<object?> values, bool bold)
    {
        sheet.Append(CultureInfo.InvariantCulture, $"<row r=\"{number}\">");
        for (var column = 0; column < values.Count; column++)
        {
            var reference = $"{(char)('A' + column)}{number.ToString(CultureInfo.InvariantCulture)}";
            switch (values[column])
            {
                case null:
                    break;
                case string text:
                    sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"inlineStr\"{(bold ? " s=\"1\"" : "")}><is><t xml:space=\"preserve\">{SecurityElement.Escape(Clean(text))}</t></is></c>");
                    break;
                case IFormattable amount:
                    // CUI and the financial year are identifiers: no thousands separators.
                    var style = column is 0 or 8 ? "" : " s=\"2\"";
                    sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\"{style}><v>{amount.ToString(null, CultureInfo.InvariantCulture)}</v></c>");
                    break;
            }
        }

        sheet.Append("</row>");
    }

    /// <summary>Drops control characters XML cannot hold (source text sometimes has them).</summary>
    private static string Clean(string text) => new([.. text.Where(character => character is '\t' or '\n' or '\r' || !char.IsControl(character))]);

    private static void Entry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private static void Line(StringBuilder text, IEnumerable<string?> cells)
    {
        text.AppendJoin(',', cells.Select(Cell));
        text.Append("\r\n");
    }

    /// <summary>Quotes a cell that holds a separator, quote or line break, doubling its quotes; a leading formula sign is neutralized.</summary>
    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '@' or '-' && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', ';', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
