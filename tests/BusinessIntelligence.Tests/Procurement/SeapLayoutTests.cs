using System.IO.Compression;
using System.Text;
using BusinessIntelligence.Modules.Procurement;
using Xunit;

namespace BusinessIntelligence.Tests.Procurement;

public sealed class SeapLayoutTests : IDisposable
{
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("bi-seap-layouts-");

    public void Dispose() => _files.Delete(recursive: true);

    private async Task<List<SeapRow>> ReadAsync(string name, byte[] content, bool contracts, SeapRows.FileFormat? format = null)
    {
        var path = Path.Combine(_files.FullName, name);
        await File.WriteAllBytesAsync(path, content);
        var rows = new List<SeapRow>();
        await foreach (var row in SeapRows.ReadAsync(path, contracts, format ?? new SeapRows.FileFormat(), CancellationToken.None))
        {
            rows.Add(row);
        }

        return rows;
    }

    [Fact]
    public async Task OldCaretFilesWithWindows1250LinesAreRead()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var header = "Castigator^CastigatorCUI^CastigatorTara^CastigatorLocalitate^CastigatorAdresa^TipProcedura^AutoritateContractanta^AutoritateContractantaCU^NumarAnunt^DataAnunt^TipIncheiereContract^NumarContract^DataContract^TitluContract^Valoare^Moneda^ValoareRON^ValoareEUR^CPVCodeID^CPVCode\n";
        var utf8 = "CADOR S.R.L.^7171490^Romania^Călărași^Str. 1^Cumparare directa^SPITALUL TEST^4301057^DA0003626^2007-03-13 13:01:08.493^Cumparare directa^1^2007-03-14 00:00:00^Hârtie^100^EUR^350.5^100^1^30197630-1\n";
        var cp1250 = Encoding.GetEncoding(1250).GetBytes("ŢESĂTORIA SRL^1590120^Romania^Brăila^Str. 2^Cumparare directa^PRIMĂRIA TEST^4301057^DA0003627^2007-03-15 10:00:00^Cumparare directa^2^2007-03-16 00:00:00^Țesături^10^RON^10^2.8^1^19000000-6\n");
        var format = new SeapRows.FileFormat();
        var rows = await ReadAsync("cumparari-directe-2007-t1.csv", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(header + utf8), .. cp1250], contracts: false, format);

        Assert.Equal(2, rows.Count);
        Assert.Equal(("CADOR S.R.L.", "7171490", "SPITALUL TEST", "4301057", "DA0003626", "350.5", "Hârtie"),
            (rows[0].WinnerName, rows[0].WinnerCui, rows[0].AuthorityName, rows[0].AuthorityCui, rows[0].Reference, rows[0].Value, rows[0].Title));
        Assert.Equal("ŢESĂTORIA SRL", rows[1].WinnerName);
        Assert.Equal(1, format.Windows1250Lines);
        Assert.Equal(new DateOnly(2007, 3, 13), SeapRows.Date(rows[0].Published, format));
    }

    [Fact]
    public async Task PipeQuotedFilesAndSlashDatesAreRead()
    {
        var content = string.Join("\n",
            "|NUMAR_ACHIZITIE_DIRECTA|,|DATA_PUBLICARE_ACHIZITIE|,|STARE_ACHIZITIE|,|DENUMIRE_AC|,|CUI_AC|,|DENUMIRE_ACHIZITIE|,|COD_CPV|,|DENUMIRE_CPV|,|DATA_ATRIBUIRE_ACHIZITIE|,|VALOARE_ESTIMATA_RON|,|VALOARE_ATRIBUITA_RON|,|OFERTANT|,|CUI_OFERTANT|",
            "|DA28897629|,|09/30/2021|,|Oferta acceptata|,|SCOALA \"AXINTE\", SCANTEIA|,|17179424|,|PACHET|,|44423000-1|,|Diverse|,|10/01/2021|,|900|,|850.25|,|TEST SRL|,|RO 10960940|");
        var format = new SeapRows.FileFormat();
        var row = Assert.Single(await ReadAsync("raport-achizitii-directe-t4.csv", Encoding.UTF8.GetBytes(content), contracts: false, format));

        Assert.Equal(("SCOALA \"AXINTE\", SCANTEIA", "850.25", "RO 10960940", "PACHET"), (row.AuthorityName, row.Value, row.WinnerCui, row.Title));
        Assert.Equal(new DateOnly(2021, 9, 30), SeapRows.Date(row.Published, format));
        Assert.Equal(new DateOnly(2021, 10, 1), SeapRows.Date(row.ContractDate, format));
        // A day over 12 in the first position switches the file to day-first.
        Assert.Equal(new DateOnly(2021, 2, 13), SeapRows.Date("13/02/2021", format));
        Assert.Equal(new DateOnly(2021, 2, 3), SeapRows.Date("03/02/2021", format));
        Assert.Equal(new DateOnly(2023, 7, 1), SeapRows.Date("7/1/2023 6:41:01 AM", new SeapRows.FileFormat()));
        Assert.Equal(new DateOnly(2018, 4, 2), SeapRows.Date("02-04-2018 09:32:26", new SeapRows.FileFormat()));
        Assert.Equal(new DateOnly(2025, 10, 1), SeapRows.Date("45931.25", new SeapRows.FileFormat()));
    }

    [Fact]
    public async Task CommaRowsWithUnquotedCommasAreRealignedOrSkipped()
    {
        var header = "Castigator,CastigatorCUI,CastigatorTara,CastigatorLocalitate,CastigatorAdresa,Tip,TipContract,TipProcedura,AutoritateContractanta,"
            + "AutoritateContractantaCUI,TipAC,TipActivitateAC,NumarAnuntAtribuire,DataAnuntAtribuire,TipIncheiereContract,TipCriteriiAtribuire,"
            + "CuLicitatieElectronica,NumarOfertePrimite,Subcontractat,NumarContract,DataContract,TitluContract,Valoare,Moneda,ValoareRON,ValoareEUR,"
            + "CPVCodeID,CPVCode,NumarAnuntParticipare,DataAnuntParticipare,ValoareEstimataParticipare,MonedaValoareEstimataParticipare,FonduriComunitare,"
            + "TipFinantare,TipLegislatieID,FondEuropean,ContractPeriodic,DepoziteGarantii,ModalitatiFinantare";
        // Commas inside the address, the authority's name and the title, none of them quoted (as in contracte-2016-s1.csv).
        var row = "RCS & RDS S.A.,5888716,Romania,Bucuresti,STR.DR.STAICOVICI,NR.75,ET.2,Anunt de atribuire la anunt de participare,Furnizare,"
            + "Negociere,Spitalul X, Iasi,4701339,,,167970,2016-04-05 01:30:01,Un contract,Pretul,,7,,3010456,2016-01-01 00:00:00,"
            + "FURNIZARE ENERGIE, LOT 1,645975.0000,RON,645975,142344.8,10309,09310000-5,,,,,,,1,,,,";
        var broken = "X SRL,1,Romania,Iasi,adresa, fara ancore";
        var format = new SeapRows.FileFormat();
        var rows = await ReadAsync("contracte-2016-s1.csv", Encoding.UTF8.GetBytes(string.Join("\n", header, row, broken + new string(',', 40))), contracts: true, format);

        var aligned = Assert.Single(rows);
        Assert.Equal(("Spitalul X, Iasi", "4701339", "167970", "FURNIZARE ENERGIE, LOT 1", "645975", "09310000-5", "3010456"),
            (aligned.AuthorityName, aligned.AuthorityCui, aligned.Reference, aligned.Title, aligned.Value, aligned.CpvCode, aligned.ContractNumber));
        Assert.Equal((1, 1), (format.RealignedRows, format.SkippedRows));
        Assert.Equal("ab", SeapRows.Clean("a\uD800b"));
    }

    [Fact]
    public async Task OpenDocumentSheetsAreRead()
    {
        var content = """
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
              <office:body><office:spreadsheet><table:table table:name="Raport">
                <table:table-row>
                  <table:table-cell><text:p>Autoritate contractanta</text:p></table:table-cell><table:table-cell><text:p>CUI autoritate contractanta</text:p></table:table-cell>
                  <table:table-cell><text:p>Numar achizitie directa</text:p></table:table-cell><table:table-cell><text:p>Data publicare</text:p></table:table-cell>
                  <table:table-cell><text:p>Valoare achizitie (RON)</text:p></table:table-cell><table:table-cell><text:p>Ofertant castigator</text:p></table:table-cell>
                  <table:table-cell><text:p>CUI ofertant castigator</text:p></table:table-cell>
                </table:table-row>
                <table:table-row>
                  <table:table-cell><text:p>COMUNA TEST</text:p></table:table-cell><table:table-cell office:value-type="float" office:value="4305857"><text:p>4305857</text:p></table:table-cell>
                  <table:table-cell><text:p>DA1</text:p></table:table-cell><table:table-cell office:value-type="date" office:date-value="2026-04-02T10:00:00"><text:p>02.04.2026</text:p></table:table-cell>
                  <table:table-cell office:value-type="float" office:value="1234.5"><text:p>1.234,50</text:p></table:table-cell><table:table-cell table:number-columns-repeated="2"><text:p>X</text:p></table:table-cell>
                </table:table-row>
                <table:table-row table:number-rows-repeated="1048000"><table:table-cell table:number-columns-repeated="16384"/></table:table-row>
              </table:table></office:spreadsheet></office:body>
            </office:document-content>
            """;
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = archive.CreateEntry("content.xml").Open())
        {
            entry.Write(Encoding.UTF8.GetBytes(content));
        }

        var row = Assert.Single(await ReadAsync("achizitii-directe-tii.ods", zip.ToArray(), contracts: false));
        Assert.Equal(("COMUNA TEST", "4305857", "2026-04-02", "1234.5", "X", "X"), (row.AuthorityName, row.AuthorityCui, row.Published, row.Value, row.WinnerName, row.WinnerCui));
    }
}
