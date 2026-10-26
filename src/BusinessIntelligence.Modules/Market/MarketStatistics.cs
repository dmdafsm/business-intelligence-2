using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Market;

/// <summary>Narrows the statistics to one county (SIRUTA county number, 0 for unknown) and one kind of entity.</summary>
public sealed record MarketFilter(short? County, string? FormCategory);

public sealed record YearRegistrations(int Year, int Registered, int ActiveNow);

/// <summary>Registrations in one month; the snapshot's month is <see cref="Partial"/>.</summary>
public sealed record MonthRegistrations(DateOnly Month, int Registered, bool Partial);

public sealed record StateCount(string Lifecycle, int Companies);

/// <summary>State changes seen between two consecutive snapshots, by the state reached.</summary>
public sealed record SnapshotChanges(DateOnly PreviousSnapshot, DateOnly ObservedOn, IReadOnlyDictionary<string, int> ByLifecycle);

public sealed record CountyCount(short County, int Active, int RegisteredLastYear);

public sealed record MarketReport(
    MarketBuild Build, IReadOnlyList<YearRegistrations> Years, IReadOnlyList<MonthRegistrations> Months, IReadOnlyList<StateCount> States,
    IReadOnlyList<SnapshotChanges> Changes, IReadOnlyList<CountyCount> Counties)
{
    public int Total => States.Sum(item => item.Companies);
}

/// <summary>Reads the newest market build for the statistics page; the tables are small (one row per month, county, kind and state).</summary>
public sealed class MarketStatistics(AppDbContext database)
{
    public const int Months = 24;
    private const string Active = Registry.Onrc.Lifecycle.Active;

    public async Task<MarketReport?> ExecuteAsync(MarketFilter filter, CancellationToken cancellationToken)
    {
        var build = await database.MarketBuilds.AsNoTracking().OrderByDescending(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        if (build is null)
        {
            return null;
        }

        var registrations = database.MarketRegistrations.AsNoTracking();
        var changes = database.LifecycleChanges.AsNoTracking();
        if (filter.County is { } county)
        {
            registrations = registrations.Where(item => item.CountyNumber == county);
            changes = changes.Where(item => item.CountyNumber == county);
        }

        if (filter.FormCategory is { } form)
        {
            registrations = registrations.Where(item => item.FormCategory == form);
            changes = changes.Where(item => item.FormCategory == form);
        }

        var current = new DateOnly(build.ReferenceDate.Year, build.ReferenceDate.Month, 1);
        var firstMonth = current.AddMonths(-Months);
        var yearStart = current.AddMonths(-12);

        var years = (await registrations.GroupBy(item => item.Month.Year)
            .Select(group => new { Year = group.Key, Registered = group.Sum(item => item.Companies), Active = group.Sum(item => item.Lifecycle == Active ? item.Companies : 0) })
            .ToListAsync(cancellationToken))
            .OrderBy(item => item.Year).Select(item => new YearRegistrations(item.Year, item.Registered, item.Active)).ToList();
        var monthly = await registrations.Where(item => item.Month >= firstMonth)
            .GroupBy(item => item.Month).Select(group => new { Month = group.Key, Registered = group.Sum(item => item.Companies) })
            .ToDictionaryAsync(item => item.Month, item => item.Registered, cancellationToken);
        var months = Enumerable.Range(0, Months + 1).Select(index => firstMonth.AddMonths(index))
            .Select(month => new MonthRegistrations(month, monthly.GetValueOrDefault(month), month == current)).ToList();
        var states = (await registrations.GroupBy(item => item.Lifecycle)
            .Select(group => new { group.Key, Companies = group.Sum(item => item.Companies) }).ToListAsync(cancellationToken))
            .Select(item => new StateCount(item.Key, item.Companies)).ToList();
        var changeRows = await changes.GroupBy(item => new { item.PreviousSnapshot, item.ObservedOn, item.ToLifecycle })
            .Select(group => new { group.Key.PreviousSnapshot, group.Key.ObservedOn, group.Key.ToLifecycle, Registrations = group.Sum(item => item.Registrations) })
            .ToListAsync(cancellationToken);
        var snapshots = changeRows.GroupBy(item => (item.PreviousSnapshot, item.ObservedOn)).OrderByDescending(group => group.Key.ObservedOn)
            .Select(group => new SnapshotChanges(group.Key.PreviousSnapshot, group.Key.ObservedOn, group.ToDictionary(item => item.ToLifecycle, item => item.Registrations)))
            .ToList();
        var counties = filter.County is not null ? [] : (await registrations.GroupBy(item => item.CountyNumber)
            .Select(group => new
            {
                County = group.Key,
                Active = group.Sum(item => item.Lifecycle == Active ? item.Companies : 0),
                Recent = group.Sum(item => item.Month >= yearStart && item.Month < current ? item.Companies : 0),
            })
            .ToListAsync(cancellationToken)).Select(item => new CountyCount(item.County, item.Active, item.Recent)).ToList();
        return new MarketReport(build, years, months, states, snapshots, counties);
    }
}
