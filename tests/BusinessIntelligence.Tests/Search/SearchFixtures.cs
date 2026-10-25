using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Search;

namespace BusinessIntelligence.Tests.Search;

/// <summary>
/// A small published registry snapshot written straight into the registry tables, then indexed. Synthetic names and
/// numbers; CUIs carry valid check digits.
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
            Kind = ImportOnrcFirme.Kind,
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
        database.PublishedDatasets.Add(new PublishedDataset { Kind = ImportOnrcFirme.Kind, SourceFileId = Snapshot, PublishedAt = DateTimeOffset.UnixEpoch });
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
        database.Registrations.Add(Registration(null, "F12/5/2010", "POPESCU ION PFA", "PersonalCode"));
        database.Companies.Add(Company(null, "RF12/5/2010", "F12/5/2010", "POPESCU ION PFA", Lifecycle.Active, Cluj, Floresti, 3001, null));

        for (var index = 1; index <= Filler; index++)
        {
            AddCompany(database, Cui(60000 + index), $"J40/{100 + index}/2020", $"FIRMA TEST {index:00} SRL", Lifecycle.Active, Bucuresti, 4100, 4003, 2, ["6201"]);
        }

        await database.SaveChangesAsync();
        if (build)
        {
            await new BuildSearchIndex(database, TimeProvider.System).ExecuteAsync(CancellationToken.None);
        }
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
        FormCategory = "Company",
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
        FormCategory = "Company",
        CountyNumber = county,
        UatCode = uat,
        LocalityCode = locality,
        GeographyState = "Locality",
        Lifecycle = lifecycle,
        CaenVersion = caenVersion,
        RegistrationDate = new DateOnly(2010, 1, 1),
    };

    private static SirutaPlace Place(int code, string name, short county, int parent, short level) =>
        new() { Code = code, Name = name, CountyNumber = county, ParentCode = parent, Level = level, Type = 1 };

    private static CaenEntry Caen(short version, string code, string level, string section, string? division, string? group, string name) =>
        new() { Version = version, Code = code, Level = level, Section = section, Division = division, Group = group, Name = name };
}
