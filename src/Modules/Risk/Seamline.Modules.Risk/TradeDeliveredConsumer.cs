using MassTransit;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// Closes out the delivered volume's contribution to Position — without
// this, delivered trades stayed in NetVolume forever and Valuation.Worker
// would keep marking exposure that no longer exists (ADR-0014). Settlement
// has its own TradeDeliveredConsumer for Invoice creation; this is a
// second, independent consumer of the same event, not a replacement.
internal sealed class TradeDeliveredConsumer(RiskDbContext db, TenantContext tenantContext) : IConsumer<TradeDelivered>
{
    public async Task Consume(ConsumeContext<TradeDelivered> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));

        var position = await PositionLookup.FindAsync(db, message.CommodityCode, message.DeliveryPeriod, context.CancellationToken);
        if (position is null)
            return; // Nothing to close — a delivery with no live position is a data inconsistency elsewhere, not this consumer's problem.

        var signedVolume = message.Direction == TradeDirection.Buy ? message.Volume : -message.Volume;
        position.Apply(-signedVolume, message.Price);

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
