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
    public DbSet<Financials.FinancialStatement> FinancialStatements => Set<Financials.FinancialStatement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Each module adds its own configuration here as it gains tables.
        RegistryPersistence.Configure(modelBuilder);
        Financials.FinancialsPersistence.Configure(modelBuilder);
        SnakeCaseNames.Apply(modelBuilder);
    }
}
