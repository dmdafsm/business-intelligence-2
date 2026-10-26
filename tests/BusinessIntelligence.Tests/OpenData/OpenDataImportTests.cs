using System.Net;
using System.Text;
using System.Text.Json;
using BusinessIntelligence.Modules.Anaf;
using BusinessIntelligence.Modules.Funds;
using BusinessIntelligence.Modules.Licences;
using BusinessIntelligence.Modules.Profile;
using BusinessIntelligence.Modules.Registry.Geography;
using BusinessIntelligence.Tests.Infrastructure;
using BusinessIntelligence.Tests.Search;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.OpenData;

public sealed class OpenDataImportTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("bi-open-data-");
    private static readonly DateOnly Reference = new(2025, 8, 31);

    public void Dispose() => _files.Delete(recursive: true);

    private string File(string name) => Path.Combine(_files.FullName, name);

    [Fact]
    public async Task EuProjectsLinkByCuiOrByAUniqueNameInTheCounty()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database);
        // A Ministry list: title block, the header row, a sub-header and projects.
        var ministry = OpenDataWorkbooks.Xlsx(File("por-lista-proiectelor-contractate-la-31-august-2025.xlsx"),
        [
            [null, "LISTA PROIECTELOR CONTRACTATE"],
            ["Nr. crt.", "Cod SMIS", "Titlu proiect", "Denumire beneficiar", "Data de începere a proiectului", "Data de finalizare a proiectului", "Județ", "Localitate",
                "Stadiu proiect", "Valoarea ELIGIBILĂ a proiectului (LEI)", null, "Total valoare proiect", "Plăţi către beneficiari (lei)"],
            [null, null, null, null, null, null, null, null, null, "Fonduri UE", "Buget național"],
            ["1", "123", "Digitalizare EXEMPLU", "EXEMPLU SOFT S.R.L.", "45000", "45500", "Cluj", "Cluj-Napoca", "în implementare", "800000", "100000", "1000000", "250000"],
            ["2", "124", "Proiect al altei firme", "FIRMA TEST 01 SRL", "45000", "45500", "Cluj", "Cluj-Napoca", "finalizat", "10", null, "10", "10"],
        ]);
        var afir = File("proiectepndr2013.csv");
        await System.IO.File.WriteAllTextAsync(afir, string.Join("\n",
            "entityid,nume,tip_organizare,cod_fiscal,roapia,siruta,localitate,adresa,codproiect,valoare_contractata,valoare_platita,oj,cr,longitude,latitude,amplasarelocalitate",
            $"a,\"EXEMPLU SOFT SRL\",SRL,{SearchFixtures.ExempluSoft},RO1,2001,CLUJ-NAPOCA,\"Str. Nouă, 7\",'1410,1500.00,1200.00,OJ,CR,23.6,46.7,CLUJ",
            "b,POPESCU ION,PF,1850101123456,RO2,3001,LUNA DE SUS,Str. X,'1411,100,100,OJ,CR,23.6,46.7,LUNA"), Encoding.UTF8);

        var import = new ImportFunds(database, TimeProvider.System);
        var first = await import.ExecuteAsync(ministry, OpenDataWorkbooks.Catalog(ministry, "eu-projects-mipe-por", ImportFunds.MinistryKind, Reference), CancellationToken.None);
        var second = await import.ExecuteAsync(afir, OpenDataWorkbooks.Catalog(afir, "eu-projects-afir-2013", ImportFunds.AfirKind, Reference), CancellationToken.None);
        Assert.Equal(2, first.Report["rows"]);
        Assert.Equal(1, second.Report["withheld"]);

        var rows = await database.FundProjects.AsNoTracking().OrderBy(item => item.SourceFileId).ThenBy(item => item.RowNumber).ToListAsync();
        Assert.Equal(("POR", "Digitalizare EXEMPLU", 1000000m, 250000m, (short)SearchFixtures.Cluj), (rows[0].Programme, rows[0].Title!, rows[0].TotalValue!.Value, rows[0].PaidValue!.Value, rows[0].CountyNumber!.Value));
        Assert.Equal(new DateOnly(2023, 3, 15), rows[0].StartDate);
        Assert.Equal(("PNDR 2007–2013", SearchFixtures.ExempluSoft, (short)SearchFixtures.Cluj), (rows[2].Programme, rows[2].BeneficiaryCui!.Value, rows[2].CountyNumber!.Value));
        Assert.Null(rows[3].BeneficiaryName);
        Assert.Null(rows[3].BeneficiaryCui);

        var profile = await new CompanyProfileQuery(database).ExecuteAsync(SearchFixtures.ExempluSoft, null, CancellationToken.None);
        Assert.Equal([("PNDR 2007–2013", false), ("POR", true)], profile!.Funds.Select(item => (item.Project.Programme, item.ByName)).OrderBy(item => item.Programme));
        // FIRMA TEST 01 is in Bucharest, not Cluj: its project is not linked to anyone.
        var other = await new CompanyProfileQuery(database).ExecuteAsync(SearchFixtures.Cui(60001), null, CancellationToken.None);
        Assert.Empty(other!.Funds);
    }

    [Fact]
    public async Task LicencesComeFromCsvShapefileAndWorkbookLists()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database);
        var anrsc = File("evidenta-licente-valabile-06.07.2026.csv");
        await System.IO.File.WriteAllTextAsync(anrsc, string.Join("\n",
            "﻿,,,,", "Autoritatea Naţională,,,,", "Nr.crt.,Cod operator,Solicitant,Serviciul public,Clasa/Valabilitatea licenței",
            "\"EUROREGIUNEA NORD-VEST\",,,,",
            $"1,CJ{SearchFixtures.ExempluSoft},EXEMPLU SOFT SRL Cluj-Napoca,alimentare cu apă,\"Licenţă nr. 1/01.01.2025 clasa 3", "Valabilă până la data de 01.01.2030\""), Encoding.UTF8);
        var anrm = OpenDataWorkbooks.Shapefile(File("licente-exploatare-active.zip"), [[(1, 1), (2, 1), (2, 2), (1, 1)]],
            [new() { ["NUME_PERIM"] = "Perimetru", ["TIP_CONTR1"] = "Lic.Conces.Exploatare", ["AGENT_ECON"] = "ZARZAVAT FRESH SRL", ["SUBSTANTE"] = "Nisip", ["JUDETE"] = "Cluj", ["LOCALIZARE"] = "Floresti" }]);
        var cnas = OpenDataWorkbooks.Xlsx(File("contracte-farm-31.03.2026.xlsx"),
        [
            ["Numar contract", "Cod fiscal furnizor", "Tip furnizor", "Nume furnizor", "Cod CAS", "Nume CAS"],
            ["478", SearchFixtures.ExempluSoft.ToString(System.Globalization.CultureInfo.InvariantCulture), "Farmacie", "EXEMPLU SOFT SRL", "CAS-CJ", "CAS CLUJ"],
        ]);
        var import = new ImportLicences(database, TimeProvider.System);
        await import.ExecuteAsync(anrsc, OpenDataWorkbooks.Catalog(anrsc, "licences-anrsc-valabile", ImportLicences.AnrscKind, Reference), CancellationToken.None);
        await import.ExecuteAsync(anrm, OpenDataWorkbooks.Catalog(anrm, "licences-anrm-exploatare", ImportLicences.AnrmKind, Reference), CancellationToken.None);
        await import.ExecuteAsync(cnas, OpenDataWorkbooks.Catalog(cnas, "licences-cnas-farm", ImportLicences.CnasKind, Reference), CancellationToken.None);

        var rows = await database.RegulatorLicences.AsNoTracking().OrderBy(item => item.Authority).ToListAsync();
        Assert.Equal([("ANRM", null), ("ANRSC", SearchFixtures.ExempluSoft), ("CNAS", SearchFixtures.ExempluSoft)], rows.Select(item => (item.Authority, item.HolderCui)));
        Assert.Equal(("valabilă", "EUROREGIUNEA NORD-VEST"), (rows[1].Status, rows[1].Place!));
        Assert.Contains("Valabilă până la data de 01.01.2030", rows[1].Details!, StringComparison.Ordinal);

        var exemplu = await new CompanyProfileQuery(database).ExecuteAsync(SearchFixtures.ExempluSoft, null, CancellationToken.None);
        Assert.Equal(["ANRSC", "CNAS"], exemplu!.Licences.Select(item => item.Licence.Authority));
        var zarzavat = await new CompanyProfileQuery(database).ExecuteAsync(SearchFixtures.Zarzavat, null, CancellationToken.None);
        Assert.True(Assert.Single(zarzavat!.Licences).ByName);
    }

    [Fact]
    public async Task BoundariesBecomeSvgOutlinesInTheNationalFrame()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database);
        (double, double)[] square = [(400_000, 600_000), (410_000, 600_000), (410_000, 610_000), (400_000, 610_000), (400_000, 600_000)];
        var counties = OpenDataWorkbooks.Shapefile(File("judet.zip"), [square], [new() { ["natLevel"] = "2ndOrder", ["natCode"] = "1000", ["name"] = "CLUJ" }]);
        var uats = OpenDataWorkbooks.Shapefile(File("uat.zip"), [square], [new() { ["natLevel"] = "3rdOrder", ["natCode"] = SearchFixtures.ClujNapoca.ToString(System.Globalization.CultureInfo.InvariantCulture), ["name"] = "CLUJ-NAPOCA" }]);
        var import = new ImportBoundaries(database, TimeProvider.System);
        await import.ExecuteAsync(counties, OpenDataWorkbooks.Catalog(counties, "boundaries-ancpi-judet", ImportBoundaries.Kind, new DateOnly(2024, 3, 7)), CancellationToken.None);
        await import.ExecuteAsync(uats, OpenDataWorkbooks.Catalog(uats, "boundaries-ancpi-uat", ImportBoundaries.Kind, new DateOnly(2024, 3, 7)), CancellationToken.None);

        var uat = await database.AreaOutlines.AsNoTracking().SingleAsync(item => item.Level == 2);
        // x = (400000 - 120000) / 765000 * 1000 = 366.0; y = (770000 - 610000) / 765000 * 1000 = 209.2
        Assert.StartsWith("M366,222.2L379.1,222.2L379.1,209.2L366,209.2Z", uat.Path, StringComparison.Ordinal);
        var profile = await new CompanyProfileQuery(database).ExecuteAsync(SearchFixtures.ExempluSoft, null, CancellationToken.None);
        Assert.Equal((1000, SearchFixtures.ClujNapoca), (profile!.Map!.CountyCode!.Value, profile.Map.Area!.Code));
    }

    [Fact]
    public async Task AnafAnswersAreStoredWithoutContactsAndTheHarvestResumes()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        await SearchFixtures.SeedAsync(database);
        var handler = new AnafHandler();
        using var http = new HttpClient(handler);
        var harvest = new HarvestAnafStatus(database, http, TimeProvider.System, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));

        var result = await harvest.ExecuteAsync(new DateOnly(2026, 1, 1), null, _ => { }, CancellationToken.None);
        // The fixture's latest MF release lists one taxpayer; the first answer was a 503 and was retried.
        Assert.Equal((2, 1, 0, 0), (result.Requests, result.Found, result.NotFound, result.Remaining));
        var stored = await database.AnafStatuses.AsNoTracking().SingleAsync(item => item.CheckedOn > new DateOnly(2026, 1, 1));
        Assert.Equal((true, true, false, true, new DateOnly(2022, 7, 1), "Direcţia Generală de Administrare a Marilor Contribuabili"),
            (stored.VatPayer!.Value, stored.EInvoice!.Value, stored.Inactive!.Value, stored.SplitVat!.Value, stored.EInvoiceSince!.Value, stored.TaxOffice!));
        Assert.DoesNotContain("0264", JsonSerializer.Serialize(stored), StringComparison.Ordinal);

        var again = await harvest.ExecuteAsync(new DateOnly(2026, 1, 1), null, _ => { }, CancellationToken.None);
        Assert.Equal((0, 0), (again.Requests, again.Remaining));
    }

    private sealed class AnafHandler : HttpMessageHandler
    {
        private int _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (++_calls == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            var asked = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.EnumerateArray().Select(item => item.GetProperty("cui").GetInt64()).ToList();
            var found = string.Join(",", asked.Select(cui => $$$"""
                {"date_generale":{"cui":{{{cui}}},"denumire":"EXEMPLU","telefon":"0264000002","stare_inregistrare":"INREGISTRAT din data 01.01.2010",
                  "organFiscalCompetent":"Direcţia Generală de Administrare a Marilor Contribuabili","cod_CAEN":"6210","statusRO_e_Factura":true,
                  "data_inreg_Reg_RO_e_Factura":"2022-07-01","iban":"RO49AAAA1B31007593840000"},
                 "inregistrare_scop_Tva":{"scpTVA":true,"perioade_TVA":[{"data_inceput_ScpTVA":"2010-01-01","data_sfarsit_ScpTVA":""}]},
                 "inregistrare_RTVAI":{"statusTvaIncasare":false,"dataInceputTvaInc":""},
                 "stare_inactiv":{"statusInactivi":false,"dataInactivare":"","dataReactivare":"","dataRadiere":""},
                 "inregistrare_SplitTVA":{"statusSplitTVA":true}}
                """));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"cod":200,"message":"SUCCESS","found":[{{found}}],"notFound":[]}""", Encoding.UTF8, "application/json") };
        }
    }
}
