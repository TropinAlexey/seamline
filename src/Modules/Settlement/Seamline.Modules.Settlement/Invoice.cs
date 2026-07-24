using Seamline.SharedKernel;

namespace Seamline.Modules.Settlement.Internal;

// One invoice per delivered trade, created directly in its final form —
// no Draft/Issued/Paid lifecycle yet. Adding payment/netting workflow
// states is a real decision (see CLAUDE.md: "any architectural decision ->
// a new ADR") deliberately deferred until Settlement has more than one
// consumer to design a lifecycle around.
internal sealed class Invoice : TenantOwnedEntity<Guid>
{
    public Guid TradeId { get; private set; }
    public Guid CounterpartyId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }

    private Invoice() { }

    public static Invoice CreateForDeliveredTrade(TenantId tenantId, Guid tradeId, Guid counterpartyId, decimal volume, decimal price)
    {
        // ADR-0007: rounding is explicit, named, at the point it happens —
        // never implicit via column scale. Money is 2 decimal places,
        // MidpointRounding.ToEven, exactly once, here.
        var amount = Math.Round(volume * price, 2, MidpointRounding.ToEven);

        return new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TradeId = tradeId,
            CounterpartyId = counterpartyId,
            Amount = amount,
            IssuedAt = DateTimeOffset.UtcNow
        };
    }
}
