using MassTransit;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Risk.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

// Plain consumer, not saga logic — the state machine orchestrates and stays
// free of direct EF Core entity mutation; this is where the Trade entity
// actually changes.
internal sealed class TradeApprovalCompletedConsumer(TradingDbContext db, TenantContext tenantContext, ICreditReservationService creditReservationService)
    : IConsumer<TradeApprovalCompleted>
{
    public async Task Consume(ConsumeContext<TradeApprovalCompleted> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));

        var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == message.TradeId, context.CancellationToken)
            ?? throw new InvalidOperationException($"Trade {message.TradeId} not found.");

        if (message.Approved)
        {
            var (history, activated) = trade.Activate("risk", "Approved after credit limit breach review");
            db.TradeHistory.Add(history);
            await creditReservationService.FinalizeAsync(trade.Id, context.CancellationToken);
            await context.Publish(activated);
        }
        else
        {
            var (history, rejected) = trade.Reject("system", "Rejected: credit limit breach not approved, or approval timed out");
            db.TradeHistory.Add(history);
            await creditReservationService.ReleaseAsync(trade.Id, context.CancellationToken);
            await context.Publish(rejected);
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
