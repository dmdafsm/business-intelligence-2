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
    public DbSet<Registry.Geography.AreaOutline> AreaOutlines => Set<Registry.Geography.AreaOutline>();
    public DbSet<Representative> Representatives => Set<Representative>();
    public DbSet<RegistrationPeriod> RegistrationPeriods => Set<RegistrationPeriod>();
    public DbSet<HistorySnapshot> HistorySnapshots => Set<HistorySnapshot>();
    public DbSet<Financials.FinancialStatement> FinancialStatements => Set<Financials.FinancialStatement>();
    public DbSet<Fiscal.TaxpayerPeriod> TaxpayerPeriods => Set<Fiscal.TaxpayerPeriod>();
    public DbSet<Fiscal.FiscalRelease> FiscalReleases => Set<Fiscal.FiscalRelease>();
    public DbSet<Fiscal.TaxpayerKind> TaxpayerKinds => Set<Fiscal.TaxpayerKind>();
    public DbSet<Licences.Licence> RegulatorLicences => Set<Licences.Licence>();
    public DbSet<Funds.FundProject> FundProjects => Set<Funds.FundProject>();
    public DbSet<Anaf.AnafStatus> AnafStatuses => Set<Anaf.AnafStatus>();
    public DbSet<Procurement.ProcurementYearTotal> ProcurementYearTotals => Set<Procurement.ProcurementYearTotal>();
    public DbSet<Procurement.ProcurementAward> ProcurementAwards => Set<Procurement.ProcurementAward>();
    public DbSet<Search.SearchCompany> SearchCompanies => Set<Search.SearchCompany>();
    public DbSet<Search.SearchName> SearchNames => Set<Search.SearchName>();
    public DbSet<Search.SearchRegistration> SearchRegistrations => Set<Search.SearchRegistration>();
    public DbSet<Search.SearchCount> SearchCounts => Set<Search.SearchCount>();
    public DbSet<Search.SearchBuild> SearchBuilds => Set<Search.SearchBuild>();
    public DbSet<Jobs.DiscoveredFile> DiscoveredFiles => Set<Jobs.DiscoveredFile>();
    public DbSet<Jobs.JobRun> JobRuns => Set<Jobs.JobRun>();
    public DbSet<Jobs.JobRunLine> JobRunLines => Set<Jobs.JobRunLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Each module adds its own configuration here as it gains tables.
        RegistryPersistence.Configure(modelBuilder);
        Financials.FinancialsPersistence.Configure(modelBuilder);
        Fiscal.FiscalPersistence.Configure(modelBuilder);
        Search.SearchPersistence.Configure(modelBuilder);
        Procurement.ProcurementPersistence.Configure(modelBuilder);
        Anaf.AnafPersistence.Configure(modelBuilder);
        Funds.FundsPersistence.Configure(modelBuilder);
        Licences.LicencesPersistence.Configure(modelBuilder);
        Jobs.JobsPersistence.Configure(modelBuilder);
        SnakeCaseNames.Apply(modelBuilder);
    }
}
