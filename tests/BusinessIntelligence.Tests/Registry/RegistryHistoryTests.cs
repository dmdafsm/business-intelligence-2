using System.Security.Cryptography;
using System.Text;
using BusinessIntelligence.Modules.Registry;
using BusinessIntelligence.Modules.Registry.History;
using BusinessIntelligence.Modules.Registry.Onrc;
using BusinessIntelligence.Modules.Registry.Sources;
using BusinessIntelligence.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessIntelligence.Tests.Registry;

public sealed class RegistryHistoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private static readonly DateOnly First = new(2013, 10, 21);
    private static readonly DateOnly Second = new(2018, 11, 1);
    private static readonly DateOnly Third = new(2021, 12, 9);
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("bi-history-");

    public void Dispose() => _root.Delete(recursive: true);

    [Theory]
    [InlineData("Sat Luna de Sus, Comuna Floreşti, Str. A, Nr. 2, Județ Cluj", "Cluj", "Sat Luna de Sus, Comuna Floreşti")]
    [InlineData("Municipiul Cluj-Napoca, Str. B, Nr. 25A, Județ Cluj", "Cluj", "Municipiul Cluj-Napoca")]
    [InlineData("Bucureşti Sectorul 3, Str. C, Nr. 1", "Bucureşti", "Bucureşti Sectorul 3")]
    [InlineData("Municipiul Craiova, Str. D, Nr. 4, CAMERA 10", null, "Municipiul Craiova")]
    public void FreeTextAddressesYieldCountyAndLocality(string address, string? county, string locality)
    {
        Assert.Equal((county, locality), AddressText.Split(address));
    }

    [Theory]
    [InlineData("POPESCU ION PERSOANĂ FIZICĂ AUTORIZATĂ", "POPESCU ION PFA")]
    [InlineData("EXEMPLU SOFT S.R.L.", "EXEMPLU SOFT SRL")]
    [InlineData("Exemplu  Soft SRL", "EXEMPLU SOFT SRL")]
    public void NamesAreComparedWithoutFormatNoise(string name, string key)
    {
        Assert.Equal(key, ImportRegistryHistory.NameKey(name));
    }

    [Fact]
    public async Task LegacyLayoutsAreJoinedTrimmedAndUnquoted()
    {
        var counts = new HistoryReadCounts();
        var part1 = Stream("﻿DENUMIRE^CUI^COD_INMATRICULARE^EUID^STARE_FIRMA^ADRESA\r\nA SRL^10960940^J12/1/2001^ROONRC.J12/1/2001^1048,2069^Municipiul Cluj-Napoca, Str. A\r\n, Județ Cluj\r\n");
        var part2 = Stream("B SRL^478197^J12/2/2002^ROONRC.J12/2/2002^1074^Municipiul Cluj-Napoca, Str. B, Județ Cluj^\r\n");
        var rows = await Read([part1, part2], struckOff: false, withOffice: true, counts);
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, counts.JoinedLines);
        Assert.Equal(("Cluj", "Municipiul Cluj-Napoca"), (rows[0].County, rows[0].Locality));
        Assert.Equal([1048, 2069], rows[0].StatusCodes);

        var quoted = await Read([Stream("\"C SRL\"|\"8629511\"|\"J29/3/1994\"|\"1084\"|\"Prahova\"|\"Blejoi\"\r\n")], true, true, new HistoryReadCounts());
        Assert.Equal(("C SRL", 8629511L, "Prahova", "Blejoi"), (quoted[0].Name, quoted[0].Cui!.Value, quoted[0].County, quoted[0].Locality));
    }

    [Fact]
    public async Task PeriodsChangeOnlyWhenTheRegistrationChanges()
    {
        await using var database = await SeededAsync();
        var import = new ImportRegistryHistory(database, TimeProvider.System);

        // 2013: headerless '|' layout. A in Cluj-Napoca, B active.
        await import.ExecuteAsync(First, Legacy(First, "active-office",
            "EXEMPLU SOFT SRL|10960940|J12/1/2001|1048|Cluj|Cluj-Napoca",
            "POPESCU ION PERSOANĂ FIZICĂ AUTORIZATĂ|1850101123456|F12/5/2010|1048|Cluj|Floreşti"), _root.FullName, CancellationToken.None);

        // 2018: '^' with free-text address. A only respelled (same period); B suspended (new period); C appears.
        await import.ExecuteAsync(Second, Legacy(Second, "active-office", "DENUMIRE^CUI^COD_INMATRICULARE^EUID^STARE_FIRMA^ADRESA",
            "EXEMPLU SOFT S.R.L.^10960940^J12/1/2001^ROONRC.J12/1/2001^1048,2069^Municipiul Cluj-Napoca, Str. A, Județ Cluj",
            "POPESCU ION PFA^1850101123456^F12/5/2010^ROONRC.F12/5/2010^1074^Comuna Floreşti, Nr. 9, Județ Cluj",
            "FIRMA NOUA SRL^478197^J12/9/2018^ROONRC.J12/9/2018^1048^Municipiul Cluj-Napoca, Str. C, Județ Cluj"), _root.FullName, CancellationToken.None);

        // 2021: A moved to Bucharest; C missing from the snapshot.
        await import.ExecuteAsync(Third, Legacy(Third, "active-office", "DENUMIRE^CUI^COD_INMATRICULARE^EUID^STARE_FIRMA^ADRESA",
            "EXEMPLU SOFT SRL^10960940^J12/1/2001^ROONRC.J12/1/2001^1048^Bucureşti Sectorul 3, Str. D",
            "POPESCU ION PFA^1850101123456^F12/5/2010^ROONRC.F12/5/2010^1074^Comuna Floreşti, Nr. 9, Județ Cluj"), _root.FullName, CancellationToken.None);

        var periods = await database.RegistrationPeriods.AsNoTracking().OrderBy(item => item.RegistrationNumber).ThenBy(item => item.ValidFrom).ToListAsync();
        var a = periods.Where(item => item.RegistrationNumber == "J12/1/2001").ToList();
        Assert.Equal([(First, Second), (Third, Third)], a.Select(item => (item.ValidFrom, item.ValidTo)));
        Assert.Equal(((short?)40, 4003), (a[1].CountyNumber, a[1].LocalityCode!.Value));

        var b = periods.Where(item => item.RegistrationNumber == "F12/5/2010").ToList();
        Assert.Equal([Lifecycle.Active, Lifecycle.Suspended], b.Select(item => item.Lifecycle));
        Assert.Equal(Third, b[1].ValidTo);
        Assert.All(b, period => Assert.Null(period.Address));
        Assert.All(b, period => Assert.Null(period.Cui));

        var c = Assert.Single(periods, item => item.RegistrationNumber == "J12/9/2018");
        Assert.Equal((Second, Second), (c.ValidFrom, c.ValidTo));

        Assert.Equal(ImportOutcome.AlreadyImported, (await import.ExecuteAsync(Third, Legacy(Third, "active-office", "DENUMIRE^CUI^COD_INMATRICULARE^EUID^STARE_FIRMA^ADRESA"), _root.FullName, CancellationToken.None)).Outcome);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            import.ExecuteAsync(Second, Legacy(Second, "active-office", "DENUMIRE^CUI^COD_INMATRICULARE^EUID^STARE_FIRMA^ADRESA"), _root.FullName, CancellationToken.None));

        await import.ClearAsync(CancellationToken.None);
        Assert.Equal(0, await database.RegistrationPeriods.CountAsync());
    }

    private List<CatalogFile> Legacy(DateOnly snapshot, string kind, params string[] lines)
    {
        var name = $"{snapshot:yyyyMMdd}-{kind}-{Guid.NewGuid():N}.csv";
        var bytes = Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");
        File.WriteAllBytes(Path.Combine(_root.FullName, name), bytes);
        return [new CatalogFile(name, "onrc-history-" + kind, "Test", name, new Uri("https://example.test/" + name), "Test", "CC BY 4.0",
            snapshot, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), LocalPath: name)];
    }

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<List<HistoryRow>> Read(IReadOnlyList<Stream> parts, bool struckOff, bool withOffice, HistoryReadCounts counts)
    {
        var rows = new List<HistoryRow>();
        await foreach (var row in LegacySnapshotReader.ReadAsync(parts, struckOff, withOffice, counts, CancellationToken.None))
        {
            rows.Add(row);
        }

        return rows;
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
