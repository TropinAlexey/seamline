using MassTransit;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

internal sealed class TradeConfirmedConsumer(RiskDbContext db, TenantContext tenantContext) : IConsumer<TradeConfirmed>
{
    public async Task Consume(ConsumeContext<TradeConfirmed> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));

        var signedVolume = message.Direction == TradeDirection.Buy ? message.Volume : -message.Volume;

        var position = await db.Positions.FirstOrDefaultAsync(
            p => p.CommodityCode == message.CommodityCode && p.DeliveryPeriod == message.DeliveryPeriod,
            context.CancellationToken);

        if (position is null)
        {
            position = Position.Create(new TenantId(message.TenantId), message.CommodityCode, message.DeliveryPeriod);
            db.Positions.Add(position);
        }

        position.Apply(signedVolume);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
