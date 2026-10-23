using MassTransit;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Settlement.Internal;

// An index-priced cargo whose certificate came in before its window fixed:
// Trading puts the certificate on this event, and the final invoice is
// issued now (ADR-0035). Without it, the certificate is still to come and
// will carry the final price itself — or the trade is not a delivered cargo
// yet, and its provisional invoice will be at the final price anyway.
internal sealed class TradePriceFixedConsumer(SettlementDbContext db, TenantContext tenantContext) : IConsumer<TradePriceFixed>
{
    public async Task Consume(ConsumeContext<TradePriceFixed> context)
    {
        var message = context.Message;
        if (message is not { MeasuredNarKcalKg: { } measuredNar, BaseNarKcalKg: { } baseNar })
            return;

        tenantContext.SetTenant(new TenantId(message.TenantId));
        await FinalInvoice.IssueAsync(db, message.TradeId, message.FinalPrice, measuredNar, baseNar, context.CancellationToken);
    }
}
