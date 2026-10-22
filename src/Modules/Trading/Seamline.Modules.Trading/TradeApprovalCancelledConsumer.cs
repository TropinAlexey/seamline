using MassTransit;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Risk.Contracts;
using Seamline.Modules.Trading.Contracts;
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

        const string reason = "Cancelled while awaiting risk approval";
        // Null for a message queued before Actor existed (ADR-0030).
        var actor = message.Actor ?? "trader";
        var history = trade.Cancel(actor, reason);
        db.TradeHistory.Add(history);
        await creditReservationService.ReleaseAsync(trade.Id, context.CancellationToken);
        await context.Publish(new TradeCancelled(trade.Id, message.TenantId, actor, reason));

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
