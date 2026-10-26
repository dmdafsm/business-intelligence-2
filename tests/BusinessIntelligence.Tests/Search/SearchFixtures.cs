using BusinessIntelligence.Modules.Financials;
using BusinessIntelligence.Modules.Fiscal;
using BusinessIntelligence.Modules.Market;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Risk;
using BusinessIntelligence.Modules.Search;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Tests.Search;

/// <summary>
/// A small published registry snapshot written straight into the registry tables, with fiscal periods and financial
/// statements for EXEMPLU SOFT, then indexed. Synthetic names and numbers; CUIs carry valid check digits.
/// </summary>
public static class SearchFixtures
{
    public const int Snapshot = 1;
    public const short Cluj = 12;
    public const short Bucuresti = 40;
    public const int ClujNapoca = 2000;
    public const int Floresti = 3000;
    public const int Filler = 30;

    public static readonly long ExempluSoft = Cui(1096094);
    public static readonly long Exemplu = Cui(4781);
    public static readonly long ExemplulNou = Cui(7937);
    public static readonly long AlfaExemplu = Cui(8629);
    public static readonly long Zarzavat = Cui(55501);

    /// <summary>Appends the check digit to a CUI body.</summary>
    public static long Cui(long body)
    {
        const string key = "753217532";
        var digits = body.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var offset = key.Length - digits.Length;
        var sum = digits.Select((digit, index) => (digit - '0') * (key[offset + index] - '0')).Sum();
        return body * 10 + sum * 10 % 11 % 10;
    }

    public static async Task SeedAsync(AppDbContext database, bool build = true)
    {
        database.SourceFiles.Add(new SourceFile
        {
            Id = Snapshot,
            CatalogId = "test-firme",
            Kind = ImportOnrcCompanies.Kind,
            Dataset = "test",
            FileName = "OD_FIRME.CSV",
            Url = "https://example.invalid",
            Publisher = "test",
            License = "test",
            ReferenceDate = new DateOnly(2026, 10, 19),
            Sha256 = new string('0', 64),
            Bytes = 1,
            ParserVersion = "test",
            ImportedAt = DateTimeOffset.UnixEpoch,
            Report = "{}",
        });
        await database.SaveChangesAsync();
        database.PublishedDatasets.Add(new PublishedDataset { Kind = ImportOnrcCompanies.Kind, SourceFileId = Snapshot, PublishedAt = DateTimeOffset.UnixEpoch });
        database.Siruta.AddRange(
            Place(1000, "JUDEŢUL CLUJ", Cluj, 1, 1),
            Place(ClujNapoca, "MUNICIPIUL CLUJ-NAPOCA", Cluj, 1000, 2),
            Place(2001, "CLUJ-NAPOCA", Cluj, ClujNapoca, 3),
            Place(Floresti, "COMUNA FLOREŞTI", Cluj, 1000, 2),
            Place(3001, "LUNA DE SUS", Cluj, Floresti, 3),
            Place(4000, "MUNICIPIUL BUCUREŞTI", Bucuresti, 1, 1),
            Place(4100, "MUNICIPIUL BUCUREŞTI", Bucuresti, 4000, 2),
            Place(4003, "BUCUREŞTI SECTORUL 3", Bucuresti, 4100, 3));
        database.Caen.AddRange(
            Caen(3, "K", "section", "K", null, null, "Telecomunicații și tehnologia informației"),
            Caen(3, "62", "division", "K", "62", null, "Activități de programare"),
            Caen(3, "621", "group", "K", "62", "621", "Activități de programare"),
            Caen(3, "6210", "class", "K", "62", "621", "Activități de programare la comandă"),
            Caen(3, "622", "group", "K", "62", "622", "Consultanță"),
            Caen(3, "6220", "class", "K", "62", "622", "Consultanță în tehnologia informației"),
            Caen(3, "C", "section", "C", null, null, "Industria prelucrătoare"),
            Caen(3, "10", "division", "C", "10", null, "Industria alimentară"),
            Caen(3, "101", "group", "C", "10", "101", "Prelucrarea cărnii"),
            Caen(3, "1011", "class", "C", "10", "101", "Prelucrarea și conservarea cărnii"),
            Caen(2, "J", "section", "J", null, null, "Informații și comunicații"),
            Caen(2, "62", "division", "J", "62", null, "Activități de servicii în tehnologia informației"),
            Caen(2, "620", "group", "J", "62", "620", "Activități de servicii în tehnologia informației"),
            Caen(2, "6201", "class", "J", "62", "620", "Realizarea soft-ului la comandă"));

        // EXEMPLU SOFT moved from J08/5/2010 to a new-format number; both registrations share the CUI.
        AddCompany(database, ExempluSoft, "J2024000100123", "EXEMPLU SOFT SRL", Lifecycle.Active, Cluj, ClujNapoca, 2001, 3, ["6210", "6220"]);
        AddRegistration(database, ExempluSoft, "J08/5/2010", "EXEMPLU SOFT SRL");
        database.RegistrationPeriods.Add(new RegistrationPeriod
        {
            RegistrationNumber = "J08/5/2010",
            ValidFrom = new DateOnly(2013, 10, 21),
            ValidTo = new DateOnly(2018, 11, 1),
            Name = "VECHI NUME S.R.L.",
            NameKey = "VECHI NUME SRL",
            Cui = ExempluSoft,
            CuiState = "Valid",
            Lifecycle = Lifecycle.Active,
            StatusCodes = [1048],
        });
        AddCompany(database, Exemplu, "J12/1/2001", "EXEMPLU", Lifecycle.StruckOff, Cluj, Floresti, 3001, 3, ["1011"]);
        AddCompany(database, ExemplulNou, "J12/2/2001", "EXEMPLUL NOU SRL", Lifecycle.StruckOff, Cluj, ClujNapoca, 2001, 3, ["6210"]);
        AddCompany(database, AlfaExemplu, "J40/3/2005", "S.C. ALFA EXEMPLU S.R.L.", Lifecycle.Active, Bucuresti, 4100, 4003, 2, ["6201"]);
        AddCompany(database, Zarzavat, "J12/4/2011", "ZARZAVAT FRESH SRL", Lifecycle.Insolvency, Cluj, ClujNapoca, 2001, null, []);

        // A sole trader whose CUI column held a personal code: no CUI, keyed by its registry number.
        var individualRegistration = Registration(null, "F12/5/2010", "POPESCU ION PFA", "PersonalCode");
        individualRegistration.FormCategory = "individual";
        individualRegistration.LegalForm = "PFA";
        database.Registrations.Add(individualRegistration);
        var individual = Company(null, "RF12/5/2010", "F12/5/2010", "POPESCU ION PFA", Lifecycle.Active, Cluj, Floresti, 3001, null);
        individual.FormCategory = "individual";
        individual.LegalForm = "PFA";
        database.Companies.Add(individual);

        // A number only the history knows, tied to the company by its CUI.
        database.RegistrationPeriods.Add(new RegistrationPeriod
        {
            RegistrationNumber = "J12/99/2000",
            ValidFrom = new DateOnly(2013, 10, 21),
            ValidTo = new DateOnly(2014, 10, 31),
            Name = "EXEMPLU SOFT SRL",
            NameKey = "EXEMPLU SOFT SRL",
            Cui = ExempluSoft,
            CuiState = "Valid",
            Lifecycle = Lifecycle.Active,
            StatusCodes = [1048],
        });
        // Two yearly snapshots: ZARZAVAT FRESH was active in the first and in insolvency in the second.
        database.HistorySnapshots.AddRange(
            new HistorySnapshot { SnapshotDate = new DateOnly(2024, 12, 18), ImportedAt = DateTimeOffset.UnixEpoch, ParserVersion = "test", Report = "{}" },
            new HistorySnapshot { SnapshotDate = new DateOnly(2025, 12, 8), ImportedAt = DateTimeOffset.UnixEpoch, ParserVersion = "test", Report = "{}" });
        foreach (var (from, to, lifecycle) in new[] { (new DateOnly(2024, 12, 18), new DateOnly(2024, 12, 18), Lifecycle.Active), (new DateOnly(2025, 12, 8), new DateOnly(2025, 12, 8), Lifecycle.Insolvency) })
        {
            database.RegistrationPeriods.Add(new RegistrationPeriod
            {
                RegistrationNumber = "J12/4/2011",
                ValidFrom = from,
                ValidTo = to,
                Name = "ZARZAVAT FRESH SRL",
                NameKey = "ZARZAVAT FRESH SRL",
                Cui = Zarzavat,
                CuiState = "Valid",
                CountyNumber = Cluj,
                Lifecycle = lifecycle,
                StatusCodes = [],
            });
        }

        AddFiscal(database);
        database.Representatives.AddRange(
            new Representative
            {
                SnapshotId = Snapshot,
                Source = "legal",
                RowNumber = 1,
                RegistrationNumber = "J2024000100123",
                Name = "POPESCU ANA",
                Role = "administrator",
                BirthDate = new DateOnly(1980, 3, 5),
                BirthLocality = "Turda",
                BirthCounty = "Cluj",
                BirthCountry = "România",
                PersonKind = "natural",
                PersonKey = "N:POPESCUANA:1980-03-05:TURDA",
            },
            new Representative
            {
                SnapshotId = Snapshot,
                Source = "legal",
                RowNumber = 2,
                RegistrationNumber = "J2024000100123",
                Name = "EXEMPLU INSOLV IPURL",
                Role = "lichidator judiciar",
                Locality = "Cluj-Napoca",
                County = "Cluj",
                Country = "România",
                PersonKind = "legal",
                PersonKey = "L:EXEMPLUINSOLVIPURL",
            },
            // The same POPESCU ANA (name spelled differently, same birth date and place) at ALFA EXEMPLU, and a namesake born
            // elsewhere at ZARZAVAT FRESH, who must not be linked.
            new Representative
            {
                SnapshotId = Snapshot,
                Source = "legal",
                RowNumber = 3,
                RegistrationNumber = "J40/3/2005",
                Name = "Popescu-Ana",
                Role = "asociat unic",
                BirthDate = new DateOnly(1980, 3, 5),
                BirthLocality = "TURDA",
                PersonKind = "natural",
                PersonKey = "N:POPESCUANA:1980-03-05:TURDA",
            },
            new Representative
            {
                SnapshotId = Snapshot,
                Source = "legal",
                RowNumber = 4,
                RegistrationNumber = "J12/4/2011",
                Name = "POPESCU ANA",
                Role = "administrator",
                BirthDate = new DateOnly(1980, 3, 5),
                BirthLocality = "Arad",
                PersonKind = "natural",
                PersonKey = "N:POPESCUANA:1980-03-05:ARAD",
            });
        await database.SaveChangesAsync();
        AddFinancials(database);
        database.SourceFiles.Add(new SourceFile
        {
            Id = 200,
            CatalogId = "seap-direct-2025-q1",
            Kind = "seap-direct",
            Dataset = "Achiziții publice 2025",
            FileName = "achizitii-directe.xlsx",
            Url = "https://example.invalid/seap.xlsx",
            Publisher = "Autoritatea pentru Digitalizarea României (ADR)",
            License = "CC BY 4.0",
            ReferenceDate = new DateOnly(2025, 3, 31),
            Sha256 = new string('f', 64),
            Bytes = 1,
            ParserVersion = "test",
            ImportedAt = DateTimeOffset.UnixEpoch,
            Report = "{}",
        });
        database.PublishedDatasets.Add(new PublishedDataset { Kind = "seap-direct-2025-Q1", SourceFileId = 200, PublishedAt = DateTimeOffset.UnixEpoch });
        AddOpenData(database);
        database.ProcurementAwards.Add(new BusinessIntelligence.Modules.Procurement.ProcurementAward
        {
            SourceFileId = 200,
            RowNumber = 2,
            Kind = "direct",
            Year = 2025,
            Quarter = 1,
            AuthorityName = "SCOALA GIMNAZIALA TEST",
            AuthorityCui = Cui(430585),
            Reference = "DA1",
            Title = "Licente software educationale",
            CpvCode = "48000000-8",
            PublishedOn = new DateOnly(2025, 2, 3),
            ContractDate = new DateOnly(2025, 2, 10),
            AwardedOn = new DateOnly(2025, 2, 10),
            ValueRon = 4999.5m,
            WinnerName = "EXEMPLU SOFT SRL",
            WinnerCui = ExempluSoft,
            WinnerCuiState = "Valid",
        });

        for (var index = 1; index <= Filler; index++)
        {
            AddCompany(database, Cui(60000 + index), $"J40/{100 + index}/2020", $"FIRMA TEST {index:00} SRL", Lifecycle.Active, Bucuresti, 4100, 4003, 2, ["6201"]);
        }

        await database.SaveChangesAsync();
        // Source files were added with explicit ids; later imports take the next free one.
        await database.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('registry.source_files', 'id'), (SELECT max(id) FROM registry.source_files))");
        database.ChangeTracker.Clear();
        await new BusinessIntelligence.Modules.Procurement.SummarizeProcurement(database).ExecuteAsync(CancellationToken.None);
        if (build)
        {
            await new BuildSearchIndex(database, TimeProvider.System).ExecuteAsync(CancellationToken.None);
            await new BuildMarketStatistics(database, TimeProvider.System).ExecuteAsync(CancellationToken.None);
            await new BuildRiskSignals(database, TimeProvider.System).ExecuteAsync(CancellationToken.None);
        }
    }

    private static void AddFiscal(AppDbContext database)
    {
        database.FiscalReleases.AddRange(
            new FiscalRelease { ReleaseDate = new DateOnly(2022, 6, 21), ImportedAt = DateTimeOffset.UnixEpoch, ParserVersion = "test", Report = "{}" },
            new FiscalRelease { ReleaseDate = new DateOnly(2026, 7, 23), ImportedAt = DateTimeOffset.UnixEpoch, ParserVersion = "test", Report = "{}" });
        database.TaxpayerPeriods.AddRange(
            Taxpayer(new DateOnly(2022, 6, 21), new DateOnly(2022, 6, 21), false, "0264000001"),
            Taxpayer(new DateOnly(2026, 7, 23), new DateOnly(2026, 7, 23), true, "0264000002"));

        static TaxpayerPeriod Taxpayer(DateOnly from, DateOnly to, bool latest, string phone) => new()
        {
            Cui = ExempluSoft,
            ValidFrom = from,
            ValidTo = to,
            Latest = latest,
            Releases = 1,
            Name = "EXEMPLU SOFT SRL",
            NameKey = "EXEMPLU SOFT SRL",
            Kind = "PJ",
            County = "CLUJ",
            Locality = "Cluj-Napoca",
            Street = "Str. Nouă",
            StreetNumber = "7",
            Sector = "0",
            Phone = phone,
            VatPayer = latest,
            FiscalStatus = "INREGISTRAT",
            Obligations = latest ? [100, 602] : [120],
        };
    }

    /// <summary>
    /// Published years 2010–2014; EXEMPLU SOFT filed 2010 (profit), 2012 (loss) and 2013 (blank turnover), nothing in 2011
    /// and 2014. Registered in 2010, so its range is 2010–2014.
    /// </summary>
    private static void AddFinancials(AppDbContext database)
    {
        for (short year = 2010; year <= 2014; year++)
        {
            var id = 100 + year - 2010;
            database.SourceFiles.Add(new SourceFile
            {
                Id = id,
                CatalogId = $"test-mf-{year}",
                Kind = "mf-financials-data",
                Dataset = $"Situații financiare {year}",
                FileName = $"web{year}.txt",
                Url = $"https://example.invalid/web{year}.txt",
                Publisher = "Ministerul Finanțelor",
                License = "CC BY 4.0",
                ReferenceDate = new DateOnly(year, 12, 31),
                Sha256 = new string((char)('a' + year - 2010), 64),
                Bytes = 1,
                ParserVersion = "test",
                ImportedAt = DateTimeOffset.UnixEpoch,
                Report = "{}",
            });
            database.PublishedDatasets.Add(new PublishedDataset { Kind = $"mf-financials-{year}-BL", SourceFileId = id, PublishedAt = DateTimeOffset.UnixEpoch });
        }

        database.FinancialStatements.AddRange(
            Statement(2010, turnover: 1_000_000, netProfit: 120_000, netLoss: null, employees: 5),
            Statement(2012, turnover: 800_000, netProfit: 0, netLoss: 45_000, employees: 0),
            Statement(2013, turnover: null, netProfit: 10_000, netLoss: 0, employees: 3));

        // 2013 benchmarks for 6201: return on equity has percentiles for all sizes; net margin has too few 1–9 companies.
        database.FinancialBenchmarks.AddRange(
            Benchmark("return_on_equity", FinancialIndicators.AllSizes, 40, [0.01m, 0.05m, 0.1m, 0.15m, 0.3m]),
            Benchmark("return_on_equity", "1-9", 4, null),
            Benchmark("net_margin", "1-9", 4, null),
            Benchmark("net_margin", FinancialIndicators.AllSizes, 6, null));

        static FinancialBenchmark Benchmark(string indicator, string band, int population, decimal[]? percentiles) => new()
        {
            Year = 2013,
            CaenVersion = 2,
            CaenCode = "6201",
            SizeBand = band,
            Indicator = indicator,
            Statements = population + 2,
            Population = population,
            P10 = percentiles?[0],
            P25 = percentiles?[1],
            P50 = percentiles?[2],
            P75 = percentiles?[3],
            P90 = percentiles?[4],
            FormulaVersion = FinancialIndicators.Version,
            BuiltAt = DateTimeOffset.UnixEpoch,
        };

        static FinancialStatement Statement(short year, decimal? turnover, decimal? netProfit, decimal? netLoss, int? employees) => new()
        {
            Year = year,
            Cui = ExempluSoft,
            Family = "BL",
            CaenCode = "6201",
            CaenVersion = 2,
            SourceFileId = 100 + year - 2010,
            Turnover = turnover,
            NetProfit = netProfit,
            NetLoss = netLoss,
            Employees = employees,
            Equity = 50_000,
        };
    }

    /// <summary>One row for each S08 source: an ANAF answer from before any test harvest, county and UAT outlines, an EU project and a licence.</summary>
    private static void AddOpenData(AppDbContext database)
    {
        SourceFile Source(int id, string kind, string publisher) => new()
        {
            Id = id,
            CatalogId = "test-" + kind,
            Kind = kind,
            Dataset = "Test " + kind,
            FileName = kind + ".dat",
            Url = "https://example.invalid/" + kind,
            Publisher = publisher,
            License = "CC BY 4.0",
            ReferenceDate = new DateOnly(2025, 8, 31),
            Sha256 = new string((char)('0' + id - 300), 64),
            Bytes = 1,
            ParserVersion = "test",
            ImportedAt = DateTimeOffset.UnixEpoch,
            Report = "{}",
        };

        database.SourceFiles.AddRange(Source(301, "boundaries-ancpi", "Agenția Națională de Cadastru"), Source(302, "eu-projects-mipe", "Ministerul Investițiilor"),
            Source(303, "licences-cnas", "Casa Națională de Asigurări de Sănătate"));
        database.PublishedDatasets.AddRange(
            new PublishedDataset { Kind = "boundaries-ancpi-1", SourceFileId = 301, PublishedAt = DateTimeOffset.UnixEpoch },
            new PublishedDataset { Kind = "eu-projects-POR", SourceFileId = 302, PublishedAt = DateTimeOffset.UnixEpoch },
            new PublishedDataset { Kind = "licences-cnas-contracte-farm", SourceFileId = 303, PublishedAt = DateTimeOffset.UnixEpoch });
        database.AreaOutlines.AddRange(
            new BusinessIntelligence.Modules.Registry.Geography.AreaOutline { Code = 1000, Level = 1, Name = "CLUJ", Path = "M300,200L400,200L400,300L300,300Z", SourceFileId = 301 },
            new BusinessIntelligence.Modules.Registry.Geography.AreaOutline { Code = 4000, Level = 1, Name = "MUNICIPIUL BUCUREŞTI", Path = "M600,500L620,500L620,520L600,520Z", SourceFileId = 301 },
            new BusinessIntelligence.Modules.Registry.Geography.AreaOutline { Code = ClujNapoca, Level = 2, Name = "CLUJ-NAPOCA", Path = "M340,240L360,240L360,260L340,260Z", SourceFileId = 301 });
        database.FundProjects.Add(new BusinessIntelligence.Modules.Funds.FundProject
        {
            SourceFileId = 302,
            RowNumber = 1,
            Programme = "POR",
            ProjectCode = "123",
            Title = "Digitalizarea EXEMPLU",
            BeneficiaryName = "EXEMPLU SOFT SRL",
            BeneficiaryNameKey = "EXEMPLU SOFT SRL",
            BeneficiaryCui = ExempluSoft,
            County = "Cluj",
            CountyNumber = Cluj,
            Status = "în implementare",
            StartDate = new DateOnly(2023, 3, 15),
            EndDate = new DateOnly(2024, 7, 28),
            TotalValue = 1_000_000m,
            PaidValue = 250_000m,
        });
        database.RegulatorLicences.Add(new BusinessIntelligence.Modules.Licences.Licence
        {
            SourceFileId = 303,
            RowNumber = 2,
            Authority = "CNAS",
            Status = "contract",
            HolderName = "EXEMPLU SOFT SRL",
            HolderNameKey = "EXEMPLU SOFT SRL",
            HolderCui = ExempluSoft,
            Activity = "Farmacie",
            Details = "contract 478",
            Place = "CAS CLUJ",
        });
        database.AnafStatuses.Add(new BusinessIntelligence.Modules.Anaf.AnafStatus
        {
            Cui = ExempluSoft,
            CheckedOn = new DateOnly(2025, 1, 1),
            Found = true,
            VatPayer = true,
            EInvoice = true,
            EInvoiceSince = new DateOnly(2022, 7, 1),
            Inactive = false,
            SplitVat = false,
            CashAccountingVat = false,
            TaxOffice = "Direcţia Generală de Administrare a Marilor Contribuabili",
            RegistrationStatus = "INREGISTRAT din data 01.01.2010",
        });
    }

    private static void AddCompany(AppDbContext database, long cui, string number, string name, string lifecycle, short county, int uat, int locality,
        short? caenVersion, string[] activities)
    {
        database.Registrations.Add(Registration(cui, number, name, "Valid"));
        database.Companies.Add(Company(cui, "C" + cui, number, name, lifecycle, county, uat, locality, caenVersion));
        foreach (var code in activities)
        {
            database.CompanyActivities.Add(new CompanyActivity { SnapshotId = Snapshot, CompanyKey = "C" + cui, CaenVersion = caenVersion!.Value, CaenCode = code });
        }
    }

    private static void AddRegistration(AppDbContext database, long cui, string number, string name) =>
        database.Registrations.Add(Registration(cui, number, name, "Valid"));

    private static Registration Registration(long? cui, string number, string name, string state) => new()
    {
        SnapshotId = Snapshot,
        RegistrationNumber = number,
        Name = name,
        Cui = cui,
        CuiState = state,
        FormCategory = "company",
        GeographyState = "Locality",
        ConflictingFields = [],
    };

    private static Company Company(long? cui, string key, string number, string name, string lifecycle, short county, int uat, int locality, short? caenVersion) => new()
    {
        SnapshotId = Snapshot,
        CompanyKey = key,
        Cui = cui,
        RegistrationNumber = number,
        Name = name,
        FormCategory = "company",
        CountyNumber = county,
        UatCode = uat,
        LocalityCode = locality,
        GeographyState = "Locality",
        Lifecycle = lifecycle,
        CaenVersion = caenVersion,
        RegistrationDate = new DateOnly(2010, 1, 1),
        FirstRegistrationDate = new DateOnly(2010, 1, 1),
    };

    private static SirutaPlace Place(int code, string name, short county, int parent, short level) =>
        new() { Code = code, Name = name, CountyNumber = county, ParentCode = parent, Level = level, Type = 1 };

    private static CaenEntry Caen(short version, string code, string level, string section, string? division, string? group, string name) =>
        new() { Version = version, Code = code, Level = level, Section = section, Division = division, Group = group, Name = name };
}
