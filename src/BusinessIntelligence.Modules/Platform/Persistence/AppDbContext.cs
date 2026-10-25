using BusinessIntelligence.Modules.Registry;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Platform.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SourceFile> SourceFiles => Set<SourceFile>();
    public DbSet<PublishedDataset> PublishedDatasets => Set<PublishedDataset>();
    public DbSet<CaenEntry> Caen => Set<CaenEntry>();
    public DbSet<CompanyStatusCode> StatusCodes => Set<CompanyStatusCode>();
    public DbSet<SirutaPlace> Siruta => Set<SirutaPlace>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<RegistrationStatus> RegistrationStatuses => Set<RegistrationStatus>();
    public DbSet<CompanyActivity> CompanyActivities => Set<CompanyActivity>();
    public DbSet<Representative> Representatives => Set<Representative>();
    public DbSet<RegistrationPeriod> RegistrationPeriods => Set<RegistrationPeriod>();
    public DbSet<HistorySnapshot> HistorySnapshots => Set<HistorySnapshot>();
    public DbSet<Financials.FinancialStatement> FinancialStatements => Set<Financials.FinancialStatement>();
    public DbSet<Fiscal.TaxpayerPeriod> TaxpayerPeriods => Set<Fiscal.TaxpayerPeriod>();
    public DbSet<Fiscal.FiscalRelease> FiscalReleases => Set<Fiscal.FiscalRelease>();
    public DbSet<Fiscal.TaxpayerKind> TaxpayerKinds => Set<Fiscal.TaxpayerKind>();
    public DbSet<Search.SearchCompany> SearchCompanies => Set<Search.SearchCompany>();
    public DbSet<Search.SearchName> SearchNames => Set<Search.SearchName>();
    public DbSet<Search.SearchRegistration> SearchRegistrations => Set<Search.SearchRegistration>();
    public DbSet<Search.SearchCount> SearchCounts => Set<Search.SearchCount>();
    public DbSet<Search.SearchBuild> SearchBuilds => Set<Search.SearchBuild>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Each module adds its own configuration here as it gains tables.
        RegistryPersistence.Configure(modelBuilder);
        Financials.FinancialsPersistence.Configure(modelBuilder);
        Fiscal.FiscalPersistence.Configure(modelBuilder);
        Search.SearchPersistence.Configure(modelBuilder);
        SnakeCaseNames.Apply(modelBuilder);
    }
}
