using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Seamline.Modules.Trading.Internal;

// Serializes the cargo steps that read one half of the trade's state and
// write the other: loading, the quality certificate and the price fixing.
// Trading decides which event completes the final invoice from what the
// trade and its shipment already say (ADR-0038). Without a lock, a
// certificate and a fixing arriving together would each see the other half
// missing, and neither event would carry both — no final invoice, ever.
// The lock is the trade row itself, held until the step commits; every read
// after it sees what the previous step committed (READ COMMITTED).
internal static class TradeRowLock
{
    public static async Task<IDbContextTransaction> AcquireAsync(TradingDbContext db, Guid tradeId, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM trading.trade WHERE "Id" = {tradeId} FOR UPDATE""", ct);
        return transaction;
    }
}
