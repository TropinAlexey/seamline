using MassTransit;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// CommodityCode/DeliveryPeriod/Direction never change on an amendment, so
// the position key is stable — only the signed volume delta needs applying,
// same Position.Apply used by TradeActivatedConsumer.
internal sealed class TradeAmendedConsumer(RiskDbContext db, TenantContext tenantContext) : IConsumer<TradeAmended>
{
    public async Task Consume(ConsumeContext<TradeAmended> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));

        var sign = message.Direction == TradeDirection.Buy ? 1m : -1m;
        var delta = (message.NewVolume - message.OldVolume) * sign;

        var position = await db.Positions.FirstOrDefaultAsync(
            p => p.CommodityCode == message.CommodityCode && p.DeliveryPeriod == message.DeliveryPeriod,
            context.CancellationToken);

        if (position is null)
        {
            position = Position.Create(new TenantId(message.TenantId), message.CommodityCode, message.DeliveryPeriod);
            db.Positions.Add(position);
        }

        position.Apply(delta);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
