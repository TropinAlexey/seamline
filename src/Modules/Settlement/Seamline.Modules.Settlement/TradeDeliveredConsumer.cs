using MassTransit;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Settlement.Internal;

internal sealed class TradeDeliveredConsumer(SettlementDbContext db, TenantContext tenantContext) : IConsumer<TradeDelivered>
{
    public async Task Consume(ConsumeContext<TradeDelivered> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));

        // A cargo is invoiced provisionally on the loaded quantity; the
        // quality certificate settles it (ADR-0034). Flow commodities get
        // their one standard invoice, as before.
        var invoice = message.CargoTerms is null
            ? Invoice.CreateForDeliveredTrade(new TenantId(message.TenantId), message.TradeId, message.CounterpartyId, message.Volume, message.Price)
            : Invoice.CreateProvisional(new TenantId(message.TenantId), message.TradeId, message.CounterpartyId, message.Volume, message.Price);
        db.Invoices.Add(invoice);

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
