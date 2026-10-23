using Microsoft.EntityFrameworkCore;

namespace Seamline.Modules.Settlement.Internal;

// Shared by the two events that can complete a cargo's settlement: the
// certificate (price already known) and the price fixing (certificate
// already in). Trading decides which one carries both halves.
internal static class FinalInvoice
{
    public static async Task IssueAsync(
        SettlementDbContext db, Guid tradeId, decimal? tradePrice, decimal measuredNar, decimal baseNar, CancellationToken cancellationToken)
    {
        var invoices = await db.Invoices
            .Where(i => i.TradeId == tradeId)
            .ToListAsync(cancellationToken);
        if (invoices.Any(i => i.Kind == InvoiceKind.Final))
            return; // redelivery

        var provisional = invoices.SingleOrDefault(i => i.Kind == InvoiceKind.Provisional)
            ?? throw new InvalidOperationException($"No provisional invoice yet for trade {tradeId}; retrying.");

        // A message from before ADR-0035 carries no price: the provisional
        // was at the contract price, so adjust that.
        db.Invoices.Add(Invoice.CreateFinal(provisional, tradePrice ?? provisional.UnitPrice!.Value, measuredNar, baseNar));
        await db.SaveChangesAsync(cancellationToken);
    }
}
