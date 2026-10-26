using System.Text;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Geography;
using BusinessIntelligence.Modules.Registry.Onrc;
using Xunit;

namespace BusinessIntelligence.Tests.Registry;

public sealed class RegistryParsingTests
{
    [Theory]
    [InlineData("10960940", CuiState.Valid, 10960940L)]
    [InlineData(" 0478197 ", CuiState.Valid, 478197L)]
    [InlineData("10960941", CuiState.InvalidChecksum, null)]
    [InlineData("0", CuiState.Zero, null)]
    [InlineData("", CuiState.Missing, null)]
    [InlineData("RO123", CuiState.NonNumeric, null)]
    [InlineData("7", CuiState.TooShort, null)]
    [InlineData("1850101123456", CuiState.PersonalCode, null)]
    public void CuiIsClassifiedAndOnlyValidValuesAreReturned(string text, CuiState state, long? value)
    {
        Assert.Equal((state, value), RomanianCui.Parse(text));
    }

    [Fact]
    public void CaenLevelsFollowTheMostSpecificCodeAndDefectsAreReported()
    {
        var content = Encoding.UTF8.GetBytes(
            "﻿SECTIUNEA^SUBSECTIUNEA^DIVIZIUNEA^GRUPA^CLASA^DENUMIRE^VERSIUNE_CAEN\r\n" +
            "A^  ^  ^   ^    ^Agricultură^2\r\n" +
            "A^  ^01^   ^01  ^Agricultură, vânătoare^2\r\n" +
            "A^  ^01^011^011 ^Culturi nepermanente^2\r\n" +
            "A^  ^01^011^0111^Cereale^2\r\n" +
            "C^CA^  ^   ^    ^Extractivă energetică^1\r\n" +
            "D^  ^  ^   ^    ^Agricultură (etichetat greșit)^1\r\n" +
            "D^  ^  ^   ^    ^Industria prelucrătoare^1\r\n" +
            "A^  ^01^011^0112^^0\r\n");
        var (entries, defects) = OnrcNomenclatures.ParseCaen(content);

        Assert.Equal(["section", "division", "group", "class"],
            entries.Where(entry => entry.Version == 2).OrderBy(entry => entry.Code.Length).Select(entry => entry.Level));
        Assert.Equal("subsection", entries.Single(entry => entry.Code == "CA").Level);
        Assert.Null(entries.Single(entry => entry.Code == "0112").Name);
        Assert.Contains(defects, defect => defect.Contains("version 1 section D", StringComparison.Ordinal));
        Assert.Equal("Agricultură (etichetat greșit)", entries.Single(entry => entry.Version == 1 && entry.Code == "D").Name);
    }

    [Fact]
    public void DuplicateStatusCodesAreRejected()
    {
        var content = Encoding.UTF8.GetBytes("COD^DENUMIRE\n1048^funcțiune\n1048^altceva\n");
        Assert.Throws<InvalidDataException>(() => OnrcNomenclatures.ParseStatusCodes(content));
    }

    [Fact]
    public async Task CompanyRowsAreTrimmedDatesParsedAndPersonalCodesDropped()
    {
        var csv = "﻿DENUMIRE^CUI^COD_INMATRICULARE^DATA_INMATRICULARE^EUID^FORMA_JURIDICA^ADR_TARA^ADR_JUDET^ADR_LOCALITATE^ADR_DEN_STRADA^ADR_NR_STRADA^ADR_BLOC^ADR_SCARA^ADR_ETAJ^ADR_APARTAMENT^ADR_COD_POSTAL^ADR_SECTOR^ADR_COMPLETARE^WEB^TARA_FIRMA_MAMA\r\n" +
            "EXEMPLU SRL^10960940^J40/1/2000^13/02/2000^ROONRC.J40/1/2000^SRL^România^Cluj^Cluj-Napoca^Str. A^1^^^^^  400001^^^www.exemplu.ro^\r\n" +
            "POPESCU ION PF^1850101123456^F12/2/1995^14/08/2026 09:23:04^ROONRC.F12/2/1995^PF^România^Cluj^Cluj-Napoca^Str. B^2^^^^^^^^^\r\n" +
            "FARA NUMAR SRL^0^ ^99/99/2000^^SRL^România^^^^^^^^^^^^^\r\n";
        var counts = new CompanyReadCounts();
        var rows = new List<CompanyRow>();
        await foreach (var row in OnrcCompanyReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(csv)), counts, CancellationToken.None))
        {
            rows.Add(row);
        }

        Assert.Equal(3, counts.Rows);
        Assert.Equal(1, counts.WithoutRegistrationNumber);
        Assert.Equal(2, rows.Count);
        Assert.Equal("400001", rows[0].PostalCode);
        Assert.Equal(new DateOnly(2000, 2, 13), rows[0].RegistrationDate);
        Assert.Null(rows[0].Block);
        Assert.Equal(new DateOnly(2026, 8, 14), rows[1].RegistrationDate);
        Assert.Null(rows[1].Cui);
        Assert.Equal(CuiState.PersonalCode, rows[1].CuiState);
        Assert.DoesNotContain(rows, row => row.ToString().Contains("1850101123456", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnexpectedHeaderIsRejected()
    {
        var counts = new CompanyReadCounts();
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in OnrcCompanyReader.ReadAsync(new MemoryStream("A^B\n"u8.ToArray()), counts, CancellationToken.None))
            {
            }
        });
    }

    [Theory]
    [InlineData("Cluj", "Sat Luna de Sus, Comuna Floreşti", "Locality", 3001)]
    [InlineData("Cluj", "Loc. Luna De Sus, Comuna Florești", "Locality", 3001)]
    [InlineData("Cluj", "Comuna Floreşti", "Uat", null)]
    [InlineData("Cluj", "Cluj-Napoca", "Uat", null)]
    [InlineData("Bucureşti", "Bucureşti Sectorul 3", "Sector", 4003)]
    [InlineData("Cluj", "Sat Necunoscut, Comuna Floreşti", "UatOnly", null)]
    [InlineData("Cluj", "***localitatea negasita***", "LocalityNotGiven", null)]
    [InlineData("Atlantida", "Oraş X", "CountyUnmatched", null)]
    public void RegistryLocalitiesResolveToSiruta(string county, string locality, string state, int? localityCode)
    {
        var match = RegistryFixtures.Matcher().Match(county, locality);
        Assert.Equal(state, match.State);
        Assert.Equal(localityCode, match.LocalityCode);
    }

    [Fact]
    public void SirutaWorkbookIsReadIncludingSharedStrings()
    {
        var rows = SimpleXlsx.ReadFirstSheet(RegistryFixtures.SirutaWorkbook());
        Assert.Equal(["SIRUTA", "DENLOC", "CODP", "JUD", "SIRSUP", "TIP", "NIV", "MED", "REGIUNE", "FSJ", "FSL"], rows[0]);
        Assert.Equal("JUDEŢUL CLUJ", rows[1][1]);
        Assert.Equal(RegistryFixtures.Places.Count + 1, rows.Count);
    }
}
