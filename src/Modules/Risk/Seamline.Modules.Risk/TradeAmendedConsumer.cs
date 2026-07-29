using MassTransit;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// CommodityCode/DeliveryPeriod/Direction never change on an amendment, so
// the position key is stable. Volume is still a delta (same Position.Apply
// used by TradeActivatedConsumer), but cost basis (ADR-0014) isn't: the old
// contribution has to be removed at OldPrice and the new one added at
// NewPrice as two separate Apply calls — a single combined delta can't
// carry two different prices.
internal sealed class TradeAmendedConsumer(RiskDbContext db, TenantContext tenantContext) : IConsumer<TradeAmended>
{
    public async Task Consume(ConsumeContext<TradeAmended> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));

        var sign = message.Direction == TradeDirection.Buy ? 1m : -1m;
        var oldSignedVolume = message.OldVolume * sign;
        var newSignedVolume = message.NewVolume * sign;

        var position = await PositionLookup.FindOrCreateAsync(
            db, new TenantId(message.TenantId), message.CommodityCode, message.DeliveryPeriod, context.CancellationToken);

        position.Apply(-oldSignedVolume, message.OldPrice);
        position.Apply(newSignedVolume, message.NewPrice);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
