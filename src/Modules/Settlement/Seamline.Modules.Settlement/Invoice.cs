using Seamline.SharedKernel;

namespace Seamline.Modules.Settlement.Internal;

// Standard: flow commodities, one invoice on delivery (ADR-0012).
// Provisional and Final: cargo commodities (ADR-0034) — provisional at
// loading on the B/L quantity and the contract price, final once the
// quality certificate has adjusted the price; the final carries the balance
// still owed (negative: owed back to the buyer).
internal enum InvoiceKind
{
    Standard = 1,
    Provisional = 2,
    Final = 3
}

// Created directly in its final form per kind — no Draft/Issued/Paid
// payment lifecycle; that is still deferred (ADR-0012).
internal sealed class Invoice : TenantOwnedEntity<Guid>
{
    public Guid TradeId { get; private set; }
    public Guid CounterpartyId { get; private set; }
    public InvoiceKind Kind { get; private set; }

    // Null only on invoices issued before ADR-0034, which stored the amount
    // alone; every invoice since carries the quantity and unit price it was
    // computed from.
    public decimal? Quantity { get; private set; }
    public decimal? UnitPrice { get; private set; }

    public decimal Amount { get; private set; }
    public decimal? Balance { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }

    private Invoice() { }

    public static Invoice CreateForDeliveredTrade(TenantId tenantId, Guid tradeId, Guid counterpartyId, decimal volume, decimal price) =>
        Create(tenantId, tradeId, counterpartyId, InvoiceKind.Standard, volume, price);

    public static Invoice CreateProvisional(TenantId tenantId, Guid tradeId, Guid counterpartyId, decimal blQuantity, decimal contractPrice) =>
        Create(tenantId, tradeId, counterpartyId, InvoiceKind.Provisional, blQuantity, contractPrice);

    // Pro-rata calorific value adjustment (spec 01). The adjusted price is an
    // invoice unit price, so it is rounded to the price scale (4 dp) first,
    // then the amount once to cents — two named rounding points (ADR-0007).
    // tradePrice is what is adjusted: the contract price, or an index-priced
    // trade's final price (ADR-0035) — its provisional invoice used the
    // expected price of the loading day instead.
    public static Invoice CreateFinal(Invoice provisional, decimal tradePrice, decimal measuredNar, decimal baseNar)
    {
        if (provisional.Kind != InvoiceKind.Provisional)
            throw new InvalidOperationException("A final invoice settles a provisional one.");
        if (measuredNar <= 0 || baseNar <= 0)
            throw new ArgumentException("Calorific values must be positive.");

        var adjustedPrice = Math.Round(tradePrice * measuredNar / baseNar, 4, MidpointRounding.ToEven);
        var final = Create(provisional.TenantId, provisional.TradeId, provisional.CounterpartyId, InvoiceKind.Final,
            provisional.Quantity!.Value, adjustedPrice);
        final.Balance = final.Amount - provisional.Amount;
        return final;
    }

    private static Invoice Create(
        TenantId tenantId, Guid tradeId, Guid counterpartyId, InvoiceKind kind, decimal quantity, decimal unitPrice)
    {
        // ADR-0007: rounding is explicit, named, at the point it happens —
        // never implicit via column scale. Money is 2 decimal places,
        // MidpointRounding.ToEven, exactly once, here.
        var amount = Math.Round(quantity * unitPrice, 2, MidpointRounding.ToEven);

        return new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TradeId = tradeId,
            CounterpartyId = counterpartyId,
            Kind = kind,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount,
            IssuedAt = DateTimeOffset.UtcNow
        };
    }
}
