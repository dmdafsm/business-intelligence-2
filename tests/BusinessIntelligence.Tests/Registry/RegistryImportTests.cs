using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Registry;

public sealed class RegistryImportTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("bi-registry-");

    public void Dispose() => _files.Delete(recursive: true);

    [Fact]
    public async Task SnapshotIsMergedGroupedByCuiAndPublished()
    {
        await using var database = await SeededAsync();
        var (path, file) = RegistryFixtures.WriteCompanies(_files.FullName, "first", RegistryFixtures.FirstSnapshot);

        var result = await new ImportOnrcCompanies(database, TimeProvider.System).ExecuteAsync(path, file, CancellationToken.None);

        Assert.Equal(ImportOutcome.Published, result.Outcome);
        var registrations = await database.Registrations.AsNoTracking().ToDictionaryAsync(item => item.RegistrationNumber);
        Assert.Equal(5, registrations.Count);

        // Two source rows for J40/100/2001: the valid CUI wins over zero (not a conflict), the differing postal code is flagged.
        var merged = registrations["J40/100/2001"];
        Assert.Equal(10960940, merged.Cui);
        Assert.Equal(2, merged.SourceRows);
        Assert.Equal(["postal_code"], merged.ConflictingFields);

        // Two different valid CUIs for one registry number: neither is chosen, the registration stays its own company.
        var disputed = registrations["J12/9/2005"];
        Assert.Equal((null, "Conflict"), (disputed.Cui, disputed.CuiState));
        Assert.Equal(["cui"], disputed.ConflictingFields);
        Assert.True(await database.Companies.AnyAsync(item => item.CompanyKey == "RJ12/9/2005"));
        Assert.Equal(("Sector", 4003), (merged.GeographyState, merged.LocalityCode));

        // Both registrations share the CUI: one company, current registration is the 2024 one in Cluj.
        var company = await database.Companies.AsNoTracking().SingleAsync(item => item.Cui == 10960940);
        Assert.Equal("C10960940", company.CompanyKey);
        Assert.Equal("J2024000100123", company.RegistrationNumber);
        Assert.Equal(2, company.RegistrationCount);
        Assert.Equal(new DateOnly(2001, 5, 10), company.FirstRegistrationDate);
        Assert.Equal(new DateOnly(2024, 8, 14), company.RegistrationDate);
        Assert.Equal((short)12, company.CountyNumber);
        Assert.Equal("www.exemplu-soft.ro", company.Website);
        Assert.Equal(4, await database.Companies.CountAsync());
    }

    [Fact]
    public async Task IndividualsKeepNoPersonalCodeAndOnlyACoarseAddress()
    {
        await using var database = await SeededAsync();
        var (path, file) = RegistryFixtures.WriteCompanies(_files.FullName, "individuals", RegistryFixtures.FirstSnapshot);
        await new ImportOnrcCompanies(database, TimeProvider.System).ExecuteAsync(path, file, CancellationToken.None);

        var individual = await database.Registrations.AsNoTracking().SingleAsync(item => item.RegistrationNumber == "F12/5/2010");
        Assert.Equal(("individual", "PersonalCode"), (individual.FormCategory, individual.CuiState));
        Assert.Null(individual.Cui);
        Assert.Null(individual.Street);
        Assert.Null(individual.StreetNumber);
        Assert.Equal(("Locality", 3001), (individual.GeographyState, individual.LocalityCode));
        Assert.Equal("RF12/5/2010", (await database.Companies.AsNoTracking().SingleAsync(item => item.RegistrationNumber == "F12/5/2010")).CompanyKey);

        var stored = await database.Database.SqlQueryRaw<long>(
            "SELECT count(*) AS \"Value\" FROM registry.registrations r WHERE r::text LIKE '%1850101123456%'").SingleAsync();
        Assert.Equal(0, stored);

        var cooperative = await database.Registrations.AsNoTracking().SingleAsync(item => item.RegistrationNumber == "C12/1/1995");
        Assert.Equal(("cooperative", "UatOnly", 3000), (cooperative.FormCategory, cooperative.GeographyState, cooperative.UatCode!.Value));
    }

    [Fact]
    public async Task ReimportIsANoOpAndANewSnapshotReplacesTheOld()
    {
        await using var database = await SeededAsync();
        var import = new ImportOnrcCompanies(database, TimeProvider.System);
        var (firstPath, first) = RegistryFixtures.WriteCompanies(_files.FullName, "a", RegistryFixtures.FirstSnapshot);
        var published = await import.ExecuteAsync(firstPath, first, CancellationToken.None);

        var again = await import.ExecuteAsync(firstPath, first, CancellationToken.None);
        Assert.Equal((ImportOutcome.AlreadyImported, published.SourceFileId), (again.Outcome, again.SourceFileId));
        Assert.Equal(5, await database.Registrations.CountAsync());

        var (secondPath, second) = RegistryFixtures.WriteCompanies(_files.FullName, "b", RegistryFixtures.SecondSnapshot);
        var replaced = await import.ExecuteAsync(secondPath, second, CancellationToken.None);
        Assert.Equal(replaced.SourceFileId, (await database.PublishedDatasets.AsNoTracking().SingleAsync(item => item.Kind == ImportOnrcCompanies.Kind)).SourceFileId);
        Assert.Equal([replaced.SourceFileId], await database.Registrations.Select(item => item.SnapshotId).Distinct().ToListAsync());
        Assert.Equal(2, await database.Companies.CountAsync());
    }

    [Fact]
    public async Task TamperedFileChangesNothing()
    {
        await using var database = await SeededAsync();
        var import = new ImportOnrcCompanies(database, TimeProvider.System);
        var (path, file) = RegistryFixtures.WriteCompanies(_files.FullName, "good", RegistryFixtures.FirstSnapshot);
        var published = await import.ExecuteAsync(path, file, CancellationToken.None);

        var (otherPath, other) = RegistryFixtures.WriteCompanies(_files.FullName, "tampered", RegistryFixtures.SecondSnapshot);
        var bytes = await File.ReadAllBytesAsync(otherPath);
        bytes[^3] = (byte)'X';
        await File.WriteAllBytesAsync(otherPath, bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() => import.ExecuteAsync(otherPath, other, CancellationToken.None));
        database.ChangeTracker.Clear();
        Assert.Equal(published.SourceFileId, (await database.PublishedDatasets.AsNoTracking().SingleAsync()).SourceFileId);
        Assert.Equal(1, await database.SourceFiles.CountAsync(item => item.Kind == ImportOnrcCompanies.Kind));
        Assert.Equal(5, await database.Registrations.CountAsync());
    }

    [Fact]
    public async Task RegistryNeedsSirutaFirst()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var (path, file) = RegistryFixtures.WriteCompanies(_files.FullName, "no-siruta", RegistryFixtures.FirstSnapshot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ImportOnrcCompanies(database, TimeProvider.System).ExecuteAsync(path, file, CancellationToken.None));
    }

    [Fact]
    public async Task SirutaWorkbookImportsOnceAndReplacesTheTable()
    {
        await using var database = await postgres.CreateIsolatedContextAsync();
        var path = Path.Combine(_files.FullName, "siruta.xlsx");
        var bytes = RegistryFixtures.SirutaWorkbook();
        await File.WriteAllBytesAsync(path, bytes);
        var file = new Modules.Registry.Sources.CatalogFile("siruta-test", "siruta", "SIRUTA test", "siruta.xlsx", new Uri("https://example.test/siruta"),
            "Test", "CC BY 4.0", new DateOnly(2024, 1, 1), bytes.Length, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        var import = new ImportReferenceData(database, TimeProvider.System);

        Assert.Equal(ImportOutcome.Published, (await import.SirutaAsync(path, file, CancellationToken.None)).Outcome);
        Assert.Equal(ImportOutcome.AlreadyImported, (await import.SirutaAsync(path, file, CancellationToken.None)).Outcome);
        Assert.Equal(RegistryFixtures.Places.Count, await database.Siruta.CountAsync());
        Assert.Equal("LUNA DE SUS", (await database.Siruta.SingleAsync(item => item.Code == 3001)).Name);
    }

    private async Task<Modules.Platform.Persistence.AppDbContext> SeededAsync()
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
        return database;
    }
}
