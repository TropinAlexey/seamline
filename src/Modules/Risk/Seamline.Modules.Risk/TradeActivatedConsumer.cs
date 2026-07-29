using MassTransit;
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

        var position = await PositionLookup.FindOrCreateAsync(
            db, new TenantId(message.TenantId), message.CommodityCode, message.DeliveryPeriod, context.CancellationToken);

        position.Apply(signedVolume, message.Price);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
