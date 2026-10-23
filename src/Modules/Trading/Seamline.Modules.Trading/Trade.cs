using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

// Draft -> Submitted -> Active | CreditPending -> Active | Rejected.
// Draft | Submitted | CreditPending -> Cancelled. Active -> Delivered.
// Matches ADR-0008: the saga and the trade lifecycle are the same machine.
// Amended is not a separate state — it's a transition that keeps a trade
// Active while bumping its version (see Trade.Amend). Settled stays out of
// scope: it belongs to the still-empty Settlement module, not Trading.
internal enum TradeState
{
    Draft = 1,
    Submitted = 2,
    CreditPending = 3,
    Active = 4,
    Rejected = 5,
    Cancelled = 6,
    Delivered = 7
}

internal sealed class Trade : TenantOwnedEntity<Guid>
{
    public string CommodityCode { get; private set; } = string.Empty;
    public string DeliveryPeriod { get; private set; } = string.Empty;
    public TradeDirection Direction { get; private set; }
    public decimal Volume { get; private set; }
    // Null for an index-priced trade until its pricing window has fully
    // fixed (ADR-0035): a placeholder would be a fiction every consumer had
    // to know to ignore. Fixed-price trades always have one.
    public decimal? Price { get; private set; }

    // "Average of an index over a window, plus a premium" — cargo
    // commodities only. Null for a fixed-price trade.
    public IndexPricingTerms? Pricing { get; private set; }

    public Guid CounterpartyId { get; private set; }
    public TradeState State { get; private set; }
    public int Version { get; private set; }

    // Equals the latest trade_history.valid_from — stored on the row so the
    // blotter can sort and page without a correlated subquery against the
    // append-only history table (ADR-0030).
    public DateTimeOffset LastChangedAt { get; private set; }

    // Cargo commodities only (ADR-0034). Null for flow commodities.
    public CargoTerms? CargoTerms { get; private set; }

    // The quantity actually delivered, set when a cargo is loaded (the B/L
    // quantity). Volume stays the contract quantity: loading within the
    // tolerance is performance of the contract, not a change to it — a
    // change to it is Amend (ADR-0011, ADR-0034).
    public decimal? ActualVolume { get; private set; }

    private Trade() { }

    public static Trade CreateDraft(
        TenantId tenantId,
        string commodityCode,
        string deliveryPeriod,
        TradeDirection direction,
        decimal volume,
        decimal? price,
        Guid counterpartyId,
        CargoTerms? cargoTerms = null,
        IndexPricingTerms? pricing = null)
    {
        if (string.IsNullOrWhiteSpace(commodityCode))
            throw new ArgumentException("Commodity code is required.", nameof(commodityCode));
        DeliveryPeriodFormat.Validate(deliveryPeriod);
        if (volume <= 0)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be positive.");
        if (pricing is null)
        {
            if (price is not > 0)
                throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");
        }
        else
        {
            RequireValidIndexPricing(pricing, price, cargoTerms, deliveryPeriod);
        }
        cargoTerms?.RequireLaycanWithin(deliveryPeriod);

        return new Trade
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CommodityCode = commodityCode,
            DeliveryPeriod = deliveryPeriod,
            Direction = direction,
            Volume = volume,
            Price = pricing is null ? price : null,
            Pricing = pricing,
            CounterpartyId = counterpartyId,
            CargoTerms = cargoTerms,
            State = TradeState.Draft,
            Version = 1,
            LastChangedAt = DateTimeOffset.UtcNow
        };
    }

    // Null while an index-priced trade is unfixed.
    public decimal? Notional => Volume * Price;

    // Index pricing is offered on cargo commodities only (ADR-0035): an
    // index-priced power or gas trade would need REMIT price-formula fields
    // the simplified report does not carry. The window lies inside the
    // delivery month, so the month's forward prices its unfixed days.
    private static void RequireValidIndexPricing(
        IndexPricingTerms pricing, decimal? price, CargoTerms? cargoTerms, string deliveryPeriod)
    {
        if (price is not null)
            throw new ArgumentException("An index-priced trade has no fixed price.", nameof(price));
        if (cargoTerms is null)
            throw new ArgumentException("Index pricing is offered on cargo commodities only.", nameof(pricing));
        if (string.IsNullOrWhiteSpace(pricing.IndexCode))
            throw new ArgumentException("Index code is required.", nameof(pricing));
        if (pricing.WindowFrom > pricing.WindowTo)
            throw new ArgumentException("Pricing window start must not be after its end.", nameof(pricing));
        if (!DeliveryPeriodFormat.Contains(deliveryPeriod, pricing.WindowFrom) || !DeliveryPeriodFormat.Contains(deliveryPeriod, pricing.WindowTo))
            throw new ArgumentException($"The pricing window must lie within the delivery period {deliveryPeriod}.", nameof(pricing));
        if (pricing.PricingDays().Count == 0)
            throw new ArgumentException("The pricing window has no weekday to fix on.", nameof(pricing));
    }

    public TradeHistory Submit(string changedBy, string changeReason)
    {
        RequireState(nameof(Submit), TradeState.Draft);
        State = TradeState.Submitted;
        Touch();
        return TradeHistory.CreateSnapshot(this, changedBy, changeReason);
    }

    public (TradeHistory History, TradeActivated Event) Activate(string changedBy, string changeReason)
    {
        RequireState(nameof(Activate), TradeState.Submitted, TradeState.CreditPending);
        State = TradeState.Active;
        Touch();
        var history = TradeHistory.CreateSnapshot(this, changedBy, changeReason);
        var activated = new TradeActivated(
            Id, TenantId.Value, CommodityCode, DeliveryPeriod, Direction, Volume, Price, CounterpartyId, changedBy, changeReason, Pricing);
        return (history, activated);
    }

    public TradeHistory EnterCreditPending(string changedBy, string changeReason)
    {
        RequireState(nameof(EnterCreditPending), TradeState.Submitted);
        State = TradeState.CreditPending;
        Touch();
        return TradeHistory.CreateSnapshot(this, changedBy, changeReason);
    }

    public (TradeHistory History, TradeRejected Event) Reject(string changedBy, string changeReason)
    {
        RequireState(nameof(Reject), TradeState.Submitted, TradeState.CreditPending);
        State = TradeState.Rejected;
        Touch();
        var history = TradeHistory.CreateSnapshot(this, changedBy, changeReason);
        var rejected = new TradeRejected(Id, TenantId.Value, changedBy, changeReason);
        return (history, rejected);
    }

    // Cancellable before the trade has ever affected a position: Draft and
    // Submitted never reserved credit, CreditPending only holds a
    // provisional reservation. Active is excluded on purpose — cancelling a
    // live trade is Amend (adjust) or a future Settlement-driven reversal,
    // not a plain Cancel.
    public TradeHistory Cancel(string changedBy, string changeReason)
    {
        RequireState(nameof(Cancel), TradeState.Draft, TradeState.Submitted, TradeState.CreditPending);
        State = TradeState.Cancelled;
        Touch();
        return TradeHistory.CreateSnapshot(this, changedBy, changeReason);
    }

    // Corrects volume/price on an already-active trade — same Trade.Id, new
    // version, State stays Active. Deliberately does not re-run the credit
    // check: re-validating exposure on every amendment would resurrect the
    // full breach saga (ADR-0008) for what's meant to be a lightweight
    // correction. TradeAmended carries both OldVolume and the new Volume so
    // Risk can adjust the position by the delta instead of recomputing it.
    //
    // An index-priced trade amends its volume only: its price comes from the
    // formula, and changing the formula is a cancel and rebook (ADR-0035).
    public (TradeHistory History, TradeAmended Event) Amend(string changedBy, string changeReason, decimal newVolume, decimal? newPrice)
    {
        RequireState(nameof(Amend), TradeState.Active);
        if (newVolume <= 0)
            throw new ArgumentOutOfRangeException(nameof(newVolume), "Volume must be positive.");
        if (Pricing is not null)
        {
            if (newPrice is not null && newPrice != Price)
                throw new ArgumentException("An index-priced trade's price comes from its formula; amend the volume only.", nameof(newPrice));
        }
        else if (newPrice is not > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newPrice), "Price must be positive.");
        }

        var oldVolume = Volume;
        var oldPrice = Price;
        Volume = newVolume;
        if (Pricing is null)
            Price = newPrice;
        Touch();
        var history = TradeHistory.CreateSnapshot(this, changedBy, changeReason);
        var amended = new TradeAmended(
            Id, TenantId.Value, CommodityCode, DeliveryPeriod, Direction, oldVolume, Volume, oldPrice, Price, CounterpartyId, changedBy, changeReason);
        return (history, amended);
    }

    // Physical delivery for the period has happened. Publishes TradeDelivered
    // now that Settlement exists to consume it (see ADR-0011's revisit
    // criteria) — Volume/Price ride along so Settlement can compute the
    // invoice amount without a synchronous call back into Trading.
    public (TradeHistory History, TradeDelivered Event) Deliver(string changedBy, string changeReason)
    {
        RequireState(nameof(Deliver), TradeState.Active);
        if (CargoTerms is not null)
            throw new DomainRuleViolationException(
                $"Trade {Id} is a cargo trade: it is delivered by recording the loading (B/L), not directly.");
        var price = Price ?? throw new DomainRuleViolationException($"Trade {Id} has no price to deliver at.");
        State = TradeState.Delivered;
        Touch();
        var history = TradeHistory.CreateSnapshot(this, changedBy, changeReason);
        var delivered = new TradeDelivered(
            Id, TenantId.Value, CommodityCode, DeliveryPeriod, Direction, CounterpartyId, Volume, price, changedBy, changeReason,
            ContractVolume: Volume);
        return (history, delivered);
    }

    // FOB cargo: title passes at loading, so the B/L is the delivery
    // (ADR-0034). The loaded quantity must sit inside the contract's
    // tolerance band; outside it is a breach, and changing the contract
    // quantity is a commercial decision that goes through Amend first.
    //
    // An index-priced trade loaded before its window has fixed is invoiced
    // provisionally at the expected price of the day (ADR-0035), which the
    // caller gets from Risk; once fixed, its own Price is used.
    public (TradeHistory History, TradeDelivered Event) DeliverLoaded(
        string changedBy, decimal blQuantity, DateOnly blDate, decimal? expectedPrice = null)
    {
        RequireState(nameof(DeliverLoaded), TradeState.Active);
        if (CargoTerms is null)
            throw new DomainRuleViolationException($"Trade {Id} is not a cargo trade.");

        var (min, max) = CargoTerms.ToleranceBand(Volume);
        if (blQuantity < min || blQuantity > max)
            throw new DomainRuleViolationException(
                $"B/L quantity {blQuantity:0.000} is outside the contract tolerance {min:0.000}–{max:0.000}. " +
                "Amend the contract quantity first.");

        var invoicePrice = Price ?? expectedPrice
            ?? throw new DomainRuleViolationException($"Trade {Id} has no price yet: an expected price is needed to invoice it provisionally.");

        ActualVolume = blQuantity;
        State = TradeState.Delivered;
        Touch();
        var reason = $"Loaded: B/L {blDate:yyyy-MM-dd}, {blQuantity:0.000}";
        var history = TradeHistory.CreateSnapshot(this, changedBy, reason);
        var delivered = new TradeDelivered(
            Id, TenantId.Value, CommodityCode, DeliveryPeriod, Direction, CounterpartyId, blQuantity, invoicePrice, changedBy, reason,
            ContractVolume: Volume, CargoTerms: CargoTerms.ToRef());
        return (history, delivered);
    }

    // Every pricing day has fixed: the formula price becomes the trade's
    // price, as a new version with actor and reason (ADR-0006, ADR-0035).
    // A person confirms it (ADR-0038); Risk computed it. Allowed after
    // delivery too — a cargo often loads mid-window. The certificate data,
    // when the cargo already has one, rides along so Settlement can issue
    // the final invoice from this event.
    public (TradeHistory History, TradePriceFixed Event) FixPrice(
        string changedBy, decimal finalPrice, decimal averageFixing, CargoCertificate? certificate = null)
    {
        RequireState(nameof(FixPrice), TradeState.Active, TradeState.Delivered);
        if (Pricing is null)
            throw new DomainRuleViolationException($"Trade {Id} is fixed-price; there is no window to fix.");
        if (Price is not null)
            throw new DomainRuleViolationException($"Trade {Id}'s price is already fixed at {Price:0.0000}.");
        if (finalPrice <= 0)
            throw new DomainRuleViolationException($"Final price {finalPrice:0.0000} is not positive; check the premium.");

        Price = finalPrice;
        Touch();
        var reason = $"Pricing window fixed: average {averageFixing:0.0000} {Pricing.Premium:+0.0000;-0.0000} premium = {finalPrice:0.0000}";
        var history = TradeHistory.CreateSnapshot(this, changedBy, reason);
        var fixedEvent = new TradePriceFixed(
            Id, TenantId.Value, finalPrice, changedBy, reason, certificate?.MeasuredNarKcalKg, certificate?.BaseNarKcalKg);
        return (history, fixedEvent);
    }

    // Every transition is a new version; TradeHistory.CreateSnapshot copies
    // LastChangedAt as the version's ValidFrom so the two never disagree.
    private void Touch()
    {
        Version++;
        LastChangedAt = DateTimeOffset.UtcNow;
    }

    private void RequireState(string methodName, params ReadOnlySpan<TradeState> allowed)
    {
        if (!allowed.Contains(State))
            throw new DomainRuleViolationException(
                $"Trade {Id}: {methodName} requires state {string.Join(" or ", allowed.ToArray())}, was {State}.");
    }
}
