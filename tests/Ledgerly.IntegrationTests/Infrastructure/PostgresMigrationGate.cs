using Ledgerly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ledgerly.IntegrationTests.Infrastructure;

internal static class PostgresMigrationGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task MigrateAsync(LedgerlyDbContext dbContext)
    {
        // Both collection fixtures initialize independently. Serialize their migration
        // checks so a fresh test database is not migrated twice at the same time.
        await Gate.WaitAsync();
        try
        {
            await dbContext.Database.MigrateAsync();
        }
        finally
        {
            Gate.Release();
        }
    }
}
