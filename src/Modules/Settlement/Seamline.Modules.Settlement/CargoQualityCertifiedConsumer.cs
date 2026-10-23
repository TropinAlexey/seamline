using MassTransit;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Settlement.Internal;

// The certificate settles the provisional invoice issued at loading
// (ADR-0034). Both events leave Trading's outbox in order, but consumers run
// independently: if the provisional has not landed yet, throwing hands the
// message back to the retry policy instead of losing the final invoice.
//
// An index-priced trade whose window is still open (PricePending) gets its
// final invoice from TradePriceFixedConsumer instead (ADR-0035).
internal sealed class CargoQualityCertifiedConsumer(SettlementDbContext db, TenantContext tenantContext) : IConsumer<CargoQualityCertified>
{
    public async Task Consume(ConsumeContext<CargoQualityCertified> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));
        if (message.PricePending)
            return;

        await FinalInvoice.IssueAsync(db, message.TradeId, message.FinalPrice, message.MeasuredNarKcalKg, message.BaseNarKcalKg, context.CancellationToken);
    }
}
