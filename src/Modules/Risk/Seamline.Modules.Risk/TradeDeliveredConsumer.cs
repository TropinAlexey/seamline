using Microsoft.EntityFrameworkCore;
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

        // Delivery ends the credit exposure too, not just the position —
        // without this a delivered trade kept counting against its
        // counterparty's limit forever (ADR-0031).
        var reservation = await db.CreditReservations.FirstOrDefaultAsync(r => r.TradeId == message.TradeId, context.CancellationToken);
        reservation?.Release();

        var formulaTrade = await db.FormulaTrades.FirstOrDefaultAsync(f => f.TradeId == message.TradeId, context.CancellationToken);
        var position = formulaTrade is null
            ? await PositionLookup.FindAsync(db, message.CommodityCode, message.DeliveryPeriod, context.CancellationToken)
            : null;
        formulaTrade?.MarkDelivered();
        if (position is not null)
        {
            // Close what activation opened: the contract quantity. For a cargo
            // the loaded quantity may differ within the tolerance; that
            // difference is physical fulfilment, not exposure (ADR-0034).
            var contractVolume = message.ContractVolume ?? message.Volume;
            var signedVolume = message.Direction == TradeDirection.Buy ? contractVolume : -contractVolume;
            position.Apply(-signedVolume, message.Price);
        }
        // No position is a data inconsistency elsewhere, not this consumer's problem.

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
