using Microsoft.EntityFrameworkCore;

namespace Seamline.SharedKernel;

// Several processes migrate the same module on startup: the API migrates
// every module, Reporting.Worker migrates Trading, Valuation.Worker migrates
// Risk and MarketData. On an empty database they race — both compute the
// same pending list and the loser fails half way (e.g. renaming an outbox
// table the winner already moved). One Postgres session-level advisory lock
// serialises all of them; the pending list is computed only after it is
// held, so the second process finds nothing left to do.
public static class MigrationLock
{
    // Arbitrary, but fixed: every module shares the one key, so migrations
    // across the whole database run one at a time.
    private const string Lock = "SELECT pg_advisory_lock(7253011904)";
    private const string Unlock = "SELECT pg_advisory_unlock(7253011904)";

    public static async Task MigrateWithLockAsync(this DbContext context, CancellationToken cancellationToken = default)
    {
        var database = context.Database;
        await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await database.ExecuteSqlRawAsync(Lock, cancellationToken);
            try
            {
                await database.MigrateAsync(cancellationToken);
            }
            finally
            {
                await database.ExecuteSqlRawAsync(Unlock, CancellationToken.None);
            }
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }
}
