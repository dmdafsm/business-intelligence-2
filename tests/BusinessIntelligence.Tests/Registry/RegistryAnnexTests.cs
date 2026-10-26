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
    public async Task RepresentativesKeepEveryColumnAndAreClassifiedByKind()
    {
        await using var database = await PublishedSnapshotAsync();
        var (path, file) = RegistryFixtures.WriteFile(_files.FullName, "representatives", ImportOnrcAnnexes.RepresentativesKind,
            "COD_INMATRICULARE^PERSOANA_IMPUTERNICITA^CALITATE^DATA_NASTERE^LOCALITATE_NASTERE^JUDET_NASTERE^TARA_NASTERE^LOCALITATE^JUDET^TARA",
            [
                "J12/9/2005^POPESCU ANA^administrator^05/03/1980 00:00:00^Cluj-Napoca^Cluj^România^^^",
                "J12/9/2005^POPESCU ANA^administrator^05/03/1980 00:00:00^Cluj-Napoca^Cluj^România^^^",
                "J12/9/2005^EXEMPLU INSOLV IPURL^lichidator judiciar^^^^^Cluj-Napoca^Cluj^România",
                "J12/9/2005^IONESCU DAN^administrator^31/02/1975^Turda^Cluj^România^^^",
                "C12/1/1995^VASILE ION^reprezentant al persoanei juridice^^^^^^^",
                "J99/1/2000^NIMENI^administrator^01/01/1990^Arad^Arad^România^^^",
            ], Snapshot);

        var result = await new ImportOnrcAnnexes(database, TimeProvider.System).RepresentativesAsync(path, file, CancellationToken.None);

        Assert.Equal(4, result.Report["representatives"]);
        Assert.Equal(1L, result.Report["rowsForUnknownRegistrations"]);
        Assert.Equal(1, result.Report["unreadableBirthDates"]);
        var rows = await database.Representatives.AsNoTracking().OrderBy(item => item.RowNumber).ToListAsync();
        var natural = rows[0];
        Assert.Equal(("natural", new DateOnly(1980, 3, 5), "Cluj-Napoca", "Cluj", "România"),
            (natural.PersonKind, natural.BirthDate!.Value, natural.BirthLocality!, natural.BirthCounty!, natural.BirthCountry!));
        Assert.Equal(("legal", "Cluj-Napoca"), (rows[1].PersonKind, rows[1].Locality!));
        Assert.Equal(("natural", (DateOnly?)null, "31/02/1975"), (rows[2].PersonKind, rows[2].BirthDate, rows[2].BirthDateText!));
        Assert.Equal("unknown", rows[3].PersonKind);
        // Linking keys (ADR 0004): folded name, birth date and place for people, the name for firms; none without a readable date.
        Assert.Equal(["N:POPESCUANA:1980-03-05:CLUJNAPOCA", "L:EXEMPLUINSOLVIPURL", null, null], rows.Select(item => item.PersonKey));

        var (familyPath, familyFile) = RegistryFixtures.WriteFile(_files.FullName, "family", ImportOnrcAnnexes.FamilyMembersKind,
            "COD_INMATRICULARE^NUME^DATA_NASTERE^LOCALITATE_NASTERE^JUDET_NASTERE^TARA_NASTERE^CALITATE",
            ["F12/5/2010^POPESCU ION^01/01/1970^Florești^Cluj^România^membru întreprindere familiala"], Snapshot);
        await new ImportOnrcAnnexes(database, TimeProvider.System).FamilyMembersAsync(familyPath, familyFile, CancellationToken.None);
        var member = await database.Representatives.AsNoTracking().SingleAsync(item => item.Source == "family");
        Assert.Equal(("membru întreprindere familiala", new DateOnly(1970, 1, 1), "natural"), (member.Role!, member.BirthDate!.Value, member.PersonKind));
        Assert.Equal("N:POPESCUION:1970-01-01:FLORESTI", member.PersonKey);

        // A newer OD_FIRME snapshot removes the previous snapshot's representatives with its other annexes.
        var (firme, companiesFile) = RegistryFixtures.WriteFile(_files.FullName, "firme-next", "onrc-firme", RegistryFixtures.Header, RegistryFixtures.SecondSnapshot, Snapshot.AddDays(1));
        await new ImportOnrcCompanies(database, TimeProvider.System).ExecuteAsync(firme, companiesFile, CancellationToken.None);
        Assert.Equal(0, await database.Representatives.CountAsync());
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

        var (path, file) = RegistryFixtures.WriteCompanies(_files.FullName, "next", RegistryFixtures.SecondSnapshot);
        await new ImportOnrcCompanies(database, TimeProvider.System).ExecuteAsync(path, file, CancellationToken.None);

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
        var (path, file) = RegistryFixtures.WriteCompanies(_files.FullName, "snapshot-" + Guid.NewGuid().ToString("N"), RegistryFixtures.FirstSnapshot);
        await new ImportOnrcCompanies(database, TimeProvider.System).ExecuteAsync(path, file, CancellationToken.None);
        return database;
    }
}
