using Microsoft.EntityFrameworkCore;
using MassTransit;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// The window has fixed: valuation marks the trade against its final price
// from now on instead of recomputing the expected one (ADR-0035).
internal sealed class TradePriceFixedConsumer(RiskDbContext db, TenantContext tenantContext) : IConsumer<TradePriceFixed>
{
    public async Task Consume(ConsumeContext<TradePriceFixed> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));

        // Activation and fixing leave the same outbox in order, but
        // consumers run independently: retry until the activation landed.
        var trade = await db.FormulaTrades.FirstOrDefaultAsync(f => f.TradeId == message.TradeId, context.CancellationToken)
            ?? throw new InvalidOperationException($"Formula trade {message.TradeId} not activated in Risk yet; retrying.");
        trade.Fix(message.FinalPrice);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
