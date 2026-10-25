using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Registry;

public sealed class RegistryAnnexTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private static readonly DateOnly Snapshot = new(2026, 10, 19);
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("bi-annex-");

    public void Dispose() => _files.Delete(recursive: true);

    [Theory]
    [InlineData(new int[0], Lifecycle.Unknown)]
    [InlineData(new[] { 1048 }, Lifecycle.Active)]
    [InlineData(new[] { 1048, 2069 }, Lifecycle.Active)]
    [InlineData(new[] { 1074, 2120 }, Lifecycle.Suspended)]
    [InlineData(new[] { 1048, 1049 }, Lifecycle.Dissolution)]
    [InlineData(new[] { 1070, 1139 }, Lifecycle.Insolvency)]
    [InlineData(new[] { 1084, 2069 }, Lifecycle.StruckOff)]
    [InlineData(new[] { 2069 }, Lifecycle.Other)]
    public void StatusCodesReduceToTheMostSevereLifecycle(int[] codes, string expected)
    {
        Assert.Equal(expected, Lifecycle.FromCodes(codes));
    }

    [Fact]
    public async Task StatusesSetLifecycleFromTheCurrentRegistration()
    {
        await using var database = await PublishedSnapshotAsync();
        var (path, file) = RegistryFixtures.WriteFile(_files.FullName, "status", ImportOnrcAnnexes.StatusKind, "COD_INMATRICULARE^COD", RegistryFixtures.Statuses, Snapshot);

        var result = await new ImportOnrcAnnexes(database, TimeProvider.System).StatusesAsync(path, file, CancellationToken.None);

        Assert.Equal(ImportOutcome.Published, result.Outcome);
        Assert.Equal(1L, result.Report["rowsForUnknownRegistrations"]);
        var lifecycles = await database.Registrations.AsNoTracking().ToDictionaryAsync(item => item.RegistrationNumber, item => item.Lifecycle);
        Assert.Equal(Lifecycle.StruckOff, lifecycles["J40/100/2001"]);
        Assert.Equal(Lifecycle.Unknown, lifecycles["J12/9/2005"]);

        // The company moved: its old Bucharest registration is struck off, the current one is active.
        Assert.Equal(Lifecycle.Active, (await database.Companies.AsNoTracking().SingleAsync(item => item.Cui == 10960940)).Lifecycle);
        Assert.Equal(Lifecycle.Suspended, (await database.Companies.AsNoTracking().SingleAsync(item => item.RegistrationNumber == "F12/5/2010")).Lifecycle);
        Assert.Equal(Lifecycle.Insolvency, (await database.Companies.AsNoTracking().SingleAsync(item => item.RegistrationNumber == "C12/1/1995")).Lifecycle);
        Assert.Equal(7, await database.RegistrationStatuses.CountAsync());

        var again = await new ImportOnrcAnnexes(database, TimeProvider.System).StatusesAsync(path, file, CancellationToken.None);
        Assert.Equal(ImportOutcome.AlreadyImported, again.Outcome);
    }

    [Fact]
    public async Task ActivitiesKeepTheCurrentRegistrationsNewestCaenVersion()
    {
        await using var database = await PublishedSnapshotAsync();
        var (path, file) = RegistryFixtures.WriteFile(_files.FullName, "caen", ImportOnrcAnnexes.ActivitiesKind,
            "COD_INMATRICULARE^COD_CAEN_AUTORIZAT^VER_CAEN_AUTORIZAT", RegistryFixtures.Activities, Snapshot);

        var result = await new ImportOnrcAnnexes(database, TimeProvider.System).ActivitiesAsync(path, file, CancellationToken.None);

        Assert.Equal(1, result.Report["rowsWithInvalidValues"]);
        var moved = await database.Companies.AsNoTracking().SingleAsync(item => item.Cui == 10960940);
        Assert.Equal((short)3, moved.CaenVersion);
        Assert.Equal(["6210", "7022"], await database.CompanyActivities.Where(item => item.CompanyKey == moved.CompanyKey)
            .OrderBy(item => item.CaenCode).Select(item => item.CaenCode).ToListAsync());
        Assert.Equal(["4711"], await database.CompanyActivities.Where(item => item.CompanyKey == "RF12/5/2010").Select(item => item.CaenCode).ToListAsync());
        Assert.Null((await database.Companies.AsNoTracking().SingleAsync(item => item.RegistrationNumber == "C12/1/1995")).CaenVersion);
    }

    [Fact]
    public async Task AnnexOfAnotherSnapshotIsRefused()
    {
        await using var database = await PublishedSnapshotAsync();
        var (path, file) = RegistryFixtures.WriteFile(_files.FullName, "old-status", ImportOnrcAnnexes.StatusKind, "COD_INMATRICULARE^COD",
            RegistryFixtures.Statuses, new DateOnly(2026, 7, 8));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ImportOnrcAnnexes(database, TimeProvider.System).StatusesAsync(path, file, CancellationToken.None));
        Assert.Equal(0, await database.RegistrationStatuses.CountAsync());
    }

    [Fact]
    public async Task ANewSnapshotRemovesThePreviousAnnexes()
    {
        await using var database = await PublishedSnapshotAsync();
        var annexes = new ImportOnrcAnnexes(database, TimeProvider.System);
        var (statusPath, statusFile) = RegistryFixtures.WriteFile(_files.FullName, "s", ImportOnrcAnnexes.StatusKind, "COD_INMATRICULARE^COD", RegistryFixtures.Statuses, Snapshot);
        var (caenPath, caenFile) = RegistryFixtures.WriteFile(_files.FullName, "c", ImportOnrcAnnexes.ActivitiesKind,
            "COD_INMATRICULARE^COD_CAEN_AUTORIZAT^VER_CAEN_AUTORIZAT", RegistryFixtures.Activities, Snapshot);
        await annexes.StatusesAsync(statusPath, statusFile, CancellationToken.None);
        await annexes.ActivitiesAsync(caenPath, caenFile, CancellationToken.None);

        var (path, file) = RegistryFixtures.WriteFirme(_files.FullName, "next", RegistryFixtures.SecondSnapshot);
        await new ImportOnrcFirme(database, TimeProvider.System).ExecuteAsync(path, file, CancellationToken.None);

        Assert.Equal(0, await database.RegistrationStatuses.CountAsync());
        Assert.Equal(0, await database.CompanyActivities.CountAsync());
        Assert.All(await database.Companies.AsNoTracking().ToListAsync(), company => Assert.Equal(Lifecycle.Unknown, company.Lifecycle));
    }

    private async Task<AppDbContext> PublishedSnapshotAsync()
    {
        var database = await postgres.CreateIsolatedContextAsync();
        database.Siruta.AddRange(RegistryFixtures.Places.Select(place => new SirutaPlace
        {
            Code = place.Code,
            Name = place.Name,
            CountyNumber = place.CountyNumber,
            ParentCode = place.ParentCode,
            Level = place.Level,
            Type = place.Type,
            Environment = place.Environment,
        }));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var (path, file) = RegistryFixtures.WriteFirme(_files.FullName, "snapshot-" + Guid.NewGuid().ToString("N"), RegistryFixtures.FirstSnapshot);
        await new ImportOnrcFirme(database, TimeProvider.System).ExecuteAsync(path, file, CancellationToken.None);
        return database;
    }
}
