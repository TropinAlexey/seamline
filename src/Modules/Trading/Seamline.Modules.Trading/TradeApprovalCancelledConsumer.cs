using MassTransit;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Risk.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

// Mirrors TradeApprovalCompletedConsumer's reject branch: the actual Trade
// mutation happens here, after the saga has stopped waiting, not
// synchronously in the /cancel endpoint.
internal sealed class TradeApprovalCancelledConsumer(TradingDbContext db, TenantContext tenantContext, ICreditReservationService creditReservationService)
    : IConsumer<TradeApprovalCancelled>
{
    public async Task Consume(ConsumeContext<TradeApprovalCancelled> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));

        var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == message.TradeId, context.CancellationToken)
            ?? throw new InvalidOperationException($"Trade {message.TradeId} not found.");

        var history = trade.Cancel("trader", "Cancelled while awaiting risk approval");
        db.TradeHistory.Add(history);
        await creditReservationService.ReleaseAsync(trade.Id, context.CancellationToken);

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
