using MassTransit;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

internal sealed class TradeActivatedConsumer(RiskDbContext db, TenantContext tenantContext) : IConsumer<TradeActivated>
{
    public async Task Consume(ConsumeContext<TradeActivated> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));

        var signedVolume = message.Direction == TradeDirection.Buy ? message.Volume : -message.Volume;

        // Index-priced: valued per trade, never blended into the position's
        // cost basis (ADR-0035).
        if (message.Pricing is { } pricing)
        {
            if (!await db.FormulaTrades.AnyAsync(f => f.TradeId == message.TradeId, context.CancellationToken))
                db.FormulaTrades.Add(FormulaTrade.Open(
                    new TenantId(message.TenantId), message.TradeId, message.CommodityCode, message.DeliveryPeriod, signedVolume, pricing));
            await db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        var position = await PositionLookup.FindOrCreateAsync(
            db, new TenantId(message.TenantId), message.CommodityCode, message.DeliveryPeriod, context.CancellationToken);

        position.Apply(signedVolume, message.Price!.Value);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
