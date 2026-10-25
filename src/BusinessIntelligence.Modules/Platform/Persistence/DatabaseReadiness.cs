using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Platform.Persistence;

public static class DatabaseReadiness
{
    public static async Task<bool> IsReadyAsync(AppDbContext database, CancellationToken cancellationToken)
    {
        try
        {
            return await database.Database.CanConnectAsync(cancellationToken)
                && !(await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
        }
        catch (DbException)
        {
            return false;
        }
    }
}
