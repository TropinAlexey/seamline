using MassTransit;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Settlement.Internal;

// The certificate settles the provisional invoice issued at loading
// (ADR-0034). Both events leave Trading's outbox in order, but consumers run
// independently: if the provisional has not landed yet, throwing hands the
// message back to the retry policy instead of losing the final invoice.
internal sealed class CargoQualityCertifiedConsumer(SettlementDbContext db, TenantContext tenantContext) : IConsumer<CargoQualityCertified>
{
    public async Task Consume(ConsumeContext<CargoQualityCertified> context)
    {
        var message = context.Message;
        tenantContext.SetTenant(new TenantId(message.TenantId));

        var invoices = await db.Invoices
            .Where(i => i.TradeId == message.TradeId)
            .ToListAsync(context.CancellationToken);
        if (invoices.Any(i => i.Kind == InvoiceKind.Final))
            return; // redelivery

        var provisional = invoices.SingleOrDefault(i => i.Kind == InvoiceKind.Provisional)
            ?? throw new InvalidOperationException($"No provisional invoice yet for trade {message.TradeId}; retrying.");

        db.Invoices.Add(Invoice.CreateFinal(provisional, message.MeasuredNarKcalKg, message.BaseNarKcalKg));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
