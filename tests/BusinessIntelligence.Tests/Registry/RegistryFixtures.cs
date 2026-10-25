using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Geography;
using BusinessIntelligence.Modules.Registry.Sources;

namespace BusinessIntelligence.Tests.Registry;

/// <summary>Synthetic registry and SIRUTA data shaped like the real files; no real company or person.</summary>
internal static class RegistryFixtures
{
    public static readonly IReadOnlyList<SirutaPlace> Places =
    [
        Place(1000, "JUDEŢUL CLUJ", 12, 1, 1),
        Place(2000, "MUNICIPIUL CLUJ-NAPOCA", 12, 1000, 2),
        Place(2001, "CLUJ-NAPOCA", 12, 2000, 3),
        Place(3000, "COMUNA FLOREŞTI", 12, 1000, 2),
        Place(3002, "FLOREŞTI", 12, 3000, 3),
        Place(3001, "LUNA DE SUS", 12, 3000, 3),
        Place(4000, "MUNICIPIUL BUCUREŞTI", 40, 1, 1),
        Place(4100, "MUNICIPIUL BUCUREŞTI", 40, 4000, 2),
        Place(4003, "BUCUREŞTI SECTORUL 3", 40, 4100, 3),
    ];

    public static SirutaSeatMatcher Matcher() => new(Places);

    public const string Header =
        "DENUMIRE^CUI^COD_INMATRICULARE^DATA_INMATRICULARE^EUID^FORMA_JURIDICA^ADR_TARA^ADR_JUDET^ADR_LOCALITATE^ADR_DEN_STRADA^ADR_NR_STRADA^ADR_BLOC^ADR_SCARA^ADR_ETAJ^ADR_APARTAMENT^ADR_COD_POSTAL^ADR_SECTOR^ADR_COMPLETARE^WEB^TARA_FIRMA_MAMA";

    /// <summary>
    /// Exemplu Soft moved from Bucharest (J40) to Cluj (J12) under the same CUI; its J40 number appears twice, once with a
    /// zero CUI and a different postal code. J12/9/2005 carries two different valid CUIs. Popescu is a sole trader whose CUI column holds a personal code.
    /// </summary>
    public static readonly string[] FirstSnapshot =
    [
        "EXEMPLU SOFT SRL^10960940^J40/100/2001^10/05/2001^ROONRC.J40/100/2001^SRL^România^Bucureşti^Bucureşti Sectorul 3^Str. Veche^1^^^^^030001^3^^^",
        "EXEMPLU SOFT SRL^0^J40/100/2001^10/05/2001^ROONRC.J40/100/2001^SRL^România^Bucureşti^Bucureşti Sectorul 3^Str. Veche^1^^^^^030002^3^^^",
        "EXEMPLU SOFT S.R.L.^10960940^J2024000100123^14/08/2024 09:23:04^ROONRC.J2024000100123^SRL^România^Cluj^Cluj-Napoca^Str. Nouă^7^A^^2^^400001^^^www.exemplu-soft.ro^Germania",
        "POPESCU ION PFA^1850101123456^F12/5/2010^01/03/2010^ROONRC.F12/5/2010^PFA^România^Cluj^Sat Luna de Sus, Comuna Floreşti^Str. Privată^9^^^^^407281^^^^",
        "COOPERATIVA DE TEST^8629511^C12/1/1995^20/06/1995^ROONRC.C12/1/1995^SC^România^Cluj^Sat Inexistent, Comuna Floreşti^^^^^^^^^^^",
        "CUI DISPUTAT SRL^478197^J12/9/2005^01/02/2005^ROONRC.J12/9/2005^SRL^România^Cluj^Cluj-Napoca^^^^^^^^^^^",
        "CUI DISPUTAT SRL^7937734^J12/9/2005^01/02/2005^ROONRC.J12/9/2005^SRL^România^Cluj^Cluj-Napoca^^^^^^^^^^^",
    ];

    public static readonly string[] SecondSnapshot =
    [
        "EXEMPLU SOFT S.R.L.^10960940^J2024000100123^14/08/2024 09:23:04^ROONRC.J2024000100123^SRL^România^Cluj^Cluj-Napoca^Str. Nouă^7^A^^2^^400001^^^www.exemplu-soft.ro^Germania",
        "FIRMA NOUA SRL^478197^J2026000200456^01/10/2026 10:00:00^ROONRC.J2026000200456^SRL^România^Cluj^Comuna Floreşti^^^^^^^^^^^",
    ];

    public static readonly string[] Statuses =
    [
        "J40/100/2001^1084",
        "J2024000100123^1048",
        "J2024000100123^2069",
        "F12/5/2010^1074",
        "F12/5/2010^2120",
        "C12/1/1995^1070",
        "C12/1/1995^1139",
        "J99/1/1999^1048",
    ];

    public static readonly string[] Activities =
    [
        "J40/100/2001^6201^2",
        "J2024000100123^6201^2",
        "J2024000100123^6210^3",
        "J2024000100123^7022^3",
        "F12/5/2010^4711^2",
        "F12/5/2010^47A1^2",
        "J99/1/1999^0111^2",
    ];

    public static (string Path, CatalogFile File) WriteFirme(string directory, string id, IEnumerable<string> rows) =>
        WriteFile(directory, id, "onrc-firme", Header, rows, new DateOnly(2026, 10, 19));

    public static (string Path, CatalogFile File) WriteFile(
        string directory, string id, string kind, string header, IEnumerable<string> rows, DateOnly referenceDate)
    {
        var path = Path.Combine(directory, id + ".csv");
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(header + "\r\n" + string.Join("\r\n", rows) + "\r\n")).ToArray();
        File.WriteAllBytes(path, bytes);
        return (path, new CatalogFile(id, kind, "Test snapshot", id + ".csv", new Uri("https://example.test/" + id),
            "Test publisher", "CC BY 4.0", referenceDate, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))));
    }

    /// <summary>A minimal XLSX with shared strings, in the SIRUTA column layout.</summary>
    public static byte[] SirutaWorkbook()
    {
        string[] header = ["SIRUTA", "DENLOC", "CODP", "JUD", "SIRSUP", "TIP", "NIV", "MED", "REGIUNE", "FSJ", "FSL"];
        var strings = new List<string>(header);
        var sheet = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        AppendRow(sheet, 1, header.Select((_, index) => (true, index.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray());
        var rowNumber = 2;
        foreach (var place in Places)
        {
            strings.Add(place.Name);
            AppendRow(sheet, rowNumber++,
            [
                (false, place.Code.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (true, (strings.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (false, "0"), (false, place.CountyNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (false, place.ParentCode.ToString(System.Globalization.CultureInfo.InvariantCulture)), (false, "3"),
                (false, place.Level.ToString(System.Globalization.CultureInfo.InvariantCulture)), (false, "1"), (false, "1"), (false, "1"), (false, "0"),
            ]);
        }

        sheet.Append("</sheetData></worksheet>");
        var shared = new StringBuilder("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        foreach (var text in strings)
        {
            shared.Append("<si><t>").Append(SecurityElement.Escape(text)).Append("</t></si>");
        }

        shared.Append("</sst>");
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            Write(archive, "xl/sharedStrings.xml", shared.ToString());
            Write(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }

        return buffer.ToArray();
    }

    private static void AppendRow(StringBuilder sheet, int row, (bool Shared, string Value)[] cells)
    {
        sheet.Append("<row r=\"").Append(row).Append("\">");
        for (var index = 0; index < cells.Length; index++)
        {
            sheet.Append("<c r=\"").Append((char)('A' + index)).Append(row).Append('"');
            sheet.Append(cells[index].Shared ? " t=\"s\"" : string.Empty).Append("><v>").Append(cells[index].Value).Append("</v></c>");
        }

        sheet.Append("</row>");
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static SirutaPlace Place(int code, string name, short county, int parent, short level) => new()
    {
        Code = code,
        Name = name,
        CountyNumber = county,
        ParentCode = parent,
        Level = level,
        Type = 3,
        Environment = 1,
    };
}
