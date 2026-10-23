using Microsoft.EntityFrameworkCore;
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
internal sealed class TradeAmendedConsumer(RiskDbContext db, TenantContext tenantContext, CreditExposureCalculator exposureCalculator) : IConsumer<TradeAmended>
{
    public async Task Consume(ConsumeContext<TradeAmended> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));

        var sign = message.Direction == TradeDirection.Buy ? 1m : -1m;
        var oldSignedVolume = message.OldVolume * sign;
        var newSignedVolume = message.NewVolume * sign;

        var formulaTrade = await db.FormulaTrades.FirstOrDefaultAsync(f => f.TradeId == message.TradeId, context.CancellationToken);
        if (formulaTrade is not null)
        {
            // Volume only: an index trade's pricing terms never change by
            // amendment (ADR-0035), and it has no place in the position.
            formulaTrade.Amend(newSignedVolume);
        }
        else
        {
            var position = await PositionLookup.FindOrCreateAsync(
                db, new TenantId(message.TenantId), message.CommodityCode, message.DeliveryPeriod, context.CancellationToken);

            position.Apply(-oldSignedVolume, message.OldPrice!.Value);
            position.Apply(newSignedVolume, message.NewPrice!.Value);
        }

        // The reservation follows the amendment, so exposure is measured on
        // the trade's current terms (ADR-0031).
        var reservation = await db.CreditReservations.FirstOrDefaultAsync(r => r.TradeId == message.TradeId, context.CancellationToken);
        if (reservation is not null)
        {
            var exposure = await exposureCalculator.ForTradeAsync(
                message.CommodityCode, message.DeliveryPeriod, newSignedVolume, message.NewPrice, reservation.Pricing, context.CancellationToken);
            reservation.Amend(newSignedVolume, message.NewPrice, exposure);
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
