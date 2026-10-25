using BusinessIntelligence.Modules.Financials;
using BusinessIntelligence.Modules.Fiscal;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Profile;

/// <summary>Where a section's values come from: publisher, dataset, the dates they describe and the license.</summary>
public sealed record SourceNote(string Publisher, string Dataset, string Period, string License, Uri? Url = null);

public sealed record StatusEntry(string RegistrationNumber, int Code, string Name);

public sealed record ActivityEntry(short Version, string Code, string? Name);

/// <summary>
/// A representative as pages may show it (ADR 0001): name, role, birth year for natural persons, and the place only for
/// firms acting as representatives. Full birth dates, places of birth and the rest stay in the database.
/// </summary>
public sealed record PersonEntry(string RegistrationNumber, string? Name, string? Role, string Kind, int? BirthYear, string? Place);

/// <summary>One financial year in the company's range; <see cref="Statement"/> is null when nothing was filed.</summary>
public sealed record FinancialYear(short Year, FinancialStatement? Statement, SourceFile? Source, string? CaenName);

/// <summary>Everything public about one company, read from the registry, fiscal and financial modules. Nothing is derived beyond labels.</summary>
public sealed record CompanyProfile
{
    public required Company Company { get; init; }
    public required IReadOnlyList<Registration> Registrations { get; init; }
    public required IReadOnlyList<StatusEntry> Statuses { get; init; }
    public required IReadOnlyList<ActivityEntry> Activities { get; init; }
    public required IReadOnlyList<PersonEntry> People { get; init; }
    public required IReadOnlyList<RegistrationPeriod> RegistryHistory { get; init; }
    public required IReadOnlyList<TaxpayerPeriod> FiscalHistory { get; init; }
    public required IReadOnlyList<FinancialYear> Financials { get; init; }
    /// <summary>SIRUTA names of the UATs and localities shown, by code.</summary>
    public required IReadOnlyDictionary<int, string> Places { get; init; }

    /// <summary>County names by SIRUTA county number.</summary>
    public required IReadOnlyDictionary<short, string> Counties { get; init; }
    public required SourceNote RegistrySource { get; init; }
    public SourceNote? HistorySource { get; init; }
    public SourceNote? FiscalSource { get; init; }
    public IReadOnlyList<DateOnly> FiscalReleases { get; init; } = [];

    public Registration? CurrentRegistration => Registrations.FirstOrDefault(item => item.RegistrationNumber == Company.RegistrationNumber);

    public TaxpayerPeriod? LatestFiscal => FiscalHistory.Count == 0 ? null : FiscalHistory[^1];
}

/// <summary>
/// Reads one company of the published registry snapshot, by CUI or (for companies without a valid CUI) by registry
/// number, with its registrations, statuses, activities, registry and fiscal history and every financial year.
/// </summary>
public sealed class CompanyProfileQuery(AppDbContext database)
{
    /// <summary>First year of the MF financial statements; earlier years are not published as open data.</summary>
    public const short FirstFinancialYear = 2008;

    public async Task<CompanyProfile?> ExecuteAsync(long? cui, string? registrationNumber, CancellationToken cancellationToken)
    {
        var firme = await database.PublishedDatasets.AsNoTracking().Where(item => item.Kind == ImportOnrcFirme.Kind)
            .Join(database.SourceFiles, item => item.SourceFileId, file => file.Id, (_, file) => file).FirstOrDefaultAsync(cancellationToken);
        if (firme is null || (cui is null && string.IsNullOrWhiteSpace(registrationNumber)))
        {
            return null;
        }

        var snapshot = firme.Id;
        var companies = database.Companies.AsNoTracking().Where(item => item.SnapshotId == snapshot);
        var company = cui is not null
            ? await companies.FirstOrDefaultAsync(item => item.Cui == cui, cancellationToken)
            : await companies.FirstOrDefaultAsync(item => item.Cui == null && item.RegistrationNumber == registrationNumber!.Trim(), cancellationToken);
        if (company is null)
        {
            return null;
        }

        var registrations = await database.Registrations.AsNoTracking()
            .Where(item => item.SnapshotId == snapshot && (company.Cui != null ? item.Cui == company.Cui : item.RegistrationNumber == company.RegistrationNumber))
            .OrderByDescending(item => item.RegistrationNumber == company.RegistrationNumber).ThenByDescending(item => item.RegistrationDate)
            .ToListAsync(cancellationToken);
        var numbers = registrations.Select(item => item.RegistrationNumber).ToList();
        var statuses = await database.RegistrationStatuses.AsNoTracking()
            .Where(item => item.SnapshotId == snapshot && numbers.Contains(item.RegistrationNumber))
            .Join(database.StatusCodes, item => item.StatusCode, code => code.Code, (item, code) => new StatusEntry(item.RegistrationNumber, item.StatusCode, code.Name))
            .ToListAsync(cancellationToken);
        var activities = await database.CompanyActivities.AsNoTracking()
            .Where(item => item.SnapshotId == snapshot && item.CompanyKey == company.CompanyKey)
            .GroupJoin(database.Caen, item => new { item.CaenVersion, Code = item.CaenCode }, entry => new { CaenVersion = entry.Version, entry.Code },
                (item, entries) => new { item, entries })
            .SelectMany(pair => pair.entries.DefaultIfEmpty(), (pair, entry) => new ActivityEntry(pair.item.CaenVersion, pair.item.CaenCode, entry != null ? entry.Name : null))
            .ToListAsync(cancellationToken);

        var people = await database.Representatives.AsNoTracking()
            .Where(item => item.SnapshotId == snapshot && numbers.Contains(item.RegistrationNumber))
            .Select(item => new PersonEntry(item.RegistrationNumber, item.Name, item.Role, item.PersonKind,
                // Birth years outside 1900 and the snapshot year are source errors and are not shown.
                item.PersonKind == "natural" && item.BirthDate != null && item.BirthDate.Value.Year >= 1900 && item.BirthDate.Value.Year <= firme.ReferenceDate.Year
                    ? item.BirthDate.Value.Year : null,
                item.PersonKind == "legal" ? string.Join(", ", new[] { item.Locality, item.County, item.Country }.Where(part => part != null)) : null))
            .ToListAsync(cancellationToken);

        // Earlier registry numbers of the same CUI (a company moved or renumbered) appear in the history by CUI.
        var history = await database.RegistrationPeriods.AsNoTracking()
            .Where(item => (company.Cui != null && item.Cui == company.Cui) || numbers.Contains(item.RegistrationNumber))
            .OrderBy(item => item.ValidFrom).ThenBy(item => item.RegistrationNumber)
            .ToListAsync(cancellationToken);
        var fiscal = company.Cui is null ? [] : await database.TaxpayerPeriods.AsNoTracking()
            .Where(item => item.Cui == company.Cui).OrderBy(item => item.ValidFrom).ToListAsync(cancellationToken);
        var releases = await database.FiscalReleases.AsNoTracking().OrderBy(item => item.ReleaseDate).Select(item => item.ReleaseDate).ToListAsync(cancellationToken);
        var snapshots = await database.HistorySnapshots.AsNoTracking().OrderBy(item => item.SnapshotDate).Select(item => item.SnapshotDate).ToListAsync(cancellationToken);

        return new CompanyProfile
        {
            Company = company,
            Registrations = registrations,
            Statuses = [.. statuses.OrderBy(item => item.RegistrationNumber != company.RegistrationNumber).ThenBy(item => item.Code)],
            Activities = [.. activities.OrderByDescending(item => item.Version).ThenBy(item => item.Code, StringComparer.Ordinal)],
            People = [.. people.OrderBy(item => RoleOrder(item.Role)).ThenBy(item => item.Name, StringComparer.Create(Culture, true))],
            RegistryHistory = history,
            FiscalHistory = fiscal,
            Financials = await FinancialsAsync(company, registrations, cancellationToken),
            Places = await PlacesAsync([company.UatCode, company.LocalityCode, .. history.SelectMany(item => new int?[] { item.UatCode, item.LocalityCode })], cancellationToken),
            Counties = await database.Siruta.AsNoTracking().Where(item => item.Level == 1)
                .ToDictionaryAsync(item => item.CountyNumber, item => Search.PlaceNames.Display(item.Name, true), cancellationToken),
            RegistrySource = Note(firme, firme.ReferenceDate.ToString("dd.MM.yyyy", Culture)),
            HistorySource = snapshots.Count == 0 ? null : HistoryNote(snapshots),
            FiscalSource = releases.Count == 0 ? null : FiscalNote(releases),
            FiscalReleases = releases,
        };
    }

    /// <summary>Management first, then representatives, then insolvency and liquidation roles, then the rest.</summary>
    private static int RoleOrder(string? role) => role switch
    {
        null => 9,
        _ when role.StartsWith("administrator", StringComparison.Ordinal) && !role.Contains("judiciar", StringComparison.Ordinal) => 0,
        _ when role.StartsWith("director", StringComparison.Ordinal) || role.StartsWith("membru", StringComparison.Ordinal) => 1,
        _ when role.StartsWith("reprezentant", StringComparison.Ordinal) || role.StartsWith("titular", StringComparison.Ordinal) => 2,
        _ when role.Contains("lichidator", StringComparison.Ordinal) || role.Contains("judiciar", StringComparison.Ordinal) => 3,
        _ => 4,
    };

    private static readonly System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>
    /// Every year from the first published financial year (or the company's registration year, if later) to the latest
    /// published one. A year without a statement is kept, with no statement: not filed is not zero.
    /// </summary>
    private async Task<List<FinancialYear>> FinancialsAsync(Company company, List<Registration> registrations, CancellationToken cancellationToken)
    {
        var statements = company.Cui is null ? [] : await database.FinancialStatements.AsNoTracking()
            .Where(item => item.Cui == company.Cui).ToListAsync(cancellationToken);
        // Published years come from the dataset pointers (mf-financials-<year>-<family>), not from scanning statements.
        var publishedYears = (await database.PublishedDatasets.AsNoTracking().Where(item => item.Kind.StartsWith("mf-financials-"))
                .Select(item => item.Kind).ToListAsync(cancellationToken))
            .Select(kind => short.TryParse(kind.Split('-')[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year) ? year : (short)0)
            .Where(year => year > 0).Distinct().ToList();
        if (publishedYears.Count == 0)
        {
            return [];
        }

        var sourceIds = statements.Select(item => item.SourceFileId).Distinct().ToList();
        var sources = await database.SourceFiles.AsNoTracking().Where(item => sourceIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var codes = statements.Where(item => item.CaenCode is not null).Select(item => item.CaenCode!).Distinct().ToList();
        var caen = await database.Caen.AsNoTracking().Where(item => item.Version == 2 && item.Level == "class" && codes.Contains(item.Code))
            .ToDictionaryAsync(item => item.Code, item => item.Name, cancellationToken);

        var registered = registrations.Select(item => item.RegistrationDate).Append(company.FirstRegistrationDate).OfType<DateOnly>().DefaultIfEmpty().Min();
        var first = (short)Math.Max(publishedYears.Min(), registered == default ? publishedYears.Min() : registered.Year);
        first = (short)Math.Min(first, statements.Count > 0 ? statements.Min(item => item.Year) : first);
        var last = publishedYears.Max();
        var byYear = statements.ToDictionary(item => item.Year);
        return [.. Enumerable.Range(first, last - first + 1).Select(year =>
        {
            var statement = byYear.GetValueOrDefault((short)year);
            return new FinancialYear((short)year, statement, statement is null ? null : sources.GetValueOrDefault(statement.SourceFileId),
                statement is { CaenCode: { } code, CaenVersion: 2 } ? caen.GetValueOrDefault(code) : null);
        })];
    }

    private async Task<Dictionary<int, string>> PlacesAsync(IEnumerable<int?> codes, CancellationToken cancellationToken)
    {
        var wanted = codes.OfType<int>().Distinct().ToList();
        return await database.Siruta.AsNoTracking().Where(item => wanted.Contains(item.Code))
            .ToDictionaryAsync(item => item.Code, item => Search.PlaceNames.Display(item.Name, false), cancellationToken);
    }

    private static SourceNote Note(SourceFile file, string period) =>
        new(file.Publisher, file.Dataset, period, file.License, Uri.TryCreate(file.Url, UriKind.Absolute, out var url) ? url : null);

    private static SourceNote HistoryNote(List<DateOnly> snapshots)
    {
        var files = SourceCatalog.All.Where(file => file.Kind.StartsWith("onrc-history", StringComparison.Ordinal)).ToList();
        return new SourceNote(files.Select(file => file.Publisher).FirstOrDefault() ?? "ONRC", "Firme înregistrate la Registrul Comerțului, instantanee anuale",
            $"{snapshots[0].ToString("dd.MM.yyyy", Culture)} – {snapshots[^1].ToString("dd.MM.yyyy", Culture)} ({snapshots.Count} instantanee)",
            string.Join(", ", files.Select(file => file.License).Distinct().Order(StringComparer.Ordinal)));
    }

    private static SourceNote FiscalNote(List<DateOnly> releases)
    {
        var files = SourceCatalog.OfKind(ImportFiscalHistory.Kind).ToList();
        return new SourceNote(files.Select(file => file.Publisher).FirstOrDefault() ?? "Ministerul Finanțelor", "Date de identificare plătitori",
            $"{releases[0].ToString("dd.MM.yyyy", Culture)} – {releases[^1].ToString("dd.MM.yyyy", Culture)} ({releases.Count} ediții)",
            string.Join(", ", files.Select(file => file.License).Distinct().Order(StringComparer.Ordinal)));
    }
}
