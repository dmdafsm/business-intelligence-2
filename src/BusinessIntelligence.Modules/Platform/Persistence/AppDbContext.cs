using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Platform.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Each module adds its own configuration here as it gains tables.
    }
}
