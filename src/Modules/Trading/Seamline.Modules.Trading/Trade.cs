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
    public decimal Price { get; private set; }
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
        decimal price,
        Guid counterpartyId,
        CargoTerms? cargoTerms = null)
    {
        if (string.IsNullOrWhiteSpace(commodityCode))
            throw new ArgumentException("Commodity code is required.", nameof(commodityCode));
        DeliveryPeriodFormat.Validate(deliveryPeriod);
        if (volume <= 0)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be positive.");
        if (price <= 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");
        cargoTerms?.RequireLaycanWithin(deliveryPeriod);

        return new Trade
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CommodityCode = commodityCode,
            DeliveryPeriod = deliveryPeriod,
            Direction = direction,
            Volume = volume,
            Price = price,
            CounterpartyId = counterpartyId,
            CargoTerms = cargoTerms,
            State = TradeState.Draft,
            Version = 1,
            LastChangedAt = DateTimeOffset.UtcNow
        };
    }

    public decimal Notional => Volume * Price;

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
        var activated = new TradeActivated(Id, TenantId.Value, CommodityCode, DeliveryPeriod, Direction, Volume, Price, CounterpartyId, changedBy, changeReason);
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
    public (TradeHistory History, TradeAmended Event) Amend(string changedBy, string changeReason, decimal newVolume, decimal newPrice)
    {
        RequireState(nameof(Amend), TradeState.Active);
        if (newVolume <= 0)
            throw new ArgumentOutOfRangeException(nameof(newVolume), "Volume must be positive.");
        if (newPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(newPrice), "Price must be positive.");

        var oldVolume = Volume;
        var oldPrice = Price;
        Volume = newVolume;
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
        State = TradeState.Delivered;
        Touch();
        var history = TradeHistory.CreateSnapshot(this, changedBy, changeReason);
        var delivered = new TradeDelivered(
            Id, TenantId.Value, CommodityCode, DeliveryPeriod, Direction, CounterpartyId, Volume, Price, changedBy, changeReason,
            ContractVolume: Volume);
        return (history, delivered);
    }

    // FOB cargo: title passes at loading, so the B/L is the delivery
    // (ADR-0034). The loaded quantity must sit inside the contract's
    // tolerance band; outside it is a breach, and changing the contract
    // quantity is a commercial decision that goes through Amend first.
    public (TradeHistory History, TradeDelivered Event) DeliverLoaded(string changedBy, decimal blQuantity, DateOnly blDate)
    {
        RequireState(nameof(DeliverLoaded), TradeState.Active);
        if (CargoTerms is null)
            throw new DomainRuleViolationException($"Trade {Id} is not a cargo trade.");

        var (min, max) = CargoTerms.ToleranceBand(Volume);
        if (blQuantity < min || blQuantity > max)
            throw new DomainRuleViolationException(
                $"B/L quantity {blQuantity:0.000} is outside the contract tolerance {min:0.000}–{max:0.000}. " +
                "Amend the contract quantity first.");

        ActualVolume = blQuantity;
        State = TradeState.Delivered;
        Touch();
        var reason = $"Loaded: B/L {blDate:yyyy-MM-dd}, {blQuantity:0.000}";
        var history = TradeHistory.CreateSnapshot(this, changedBy, reason);
        var delivered = new TradeDelivered(
            Id, TenantId.Value, CommodityCode, DeliveryPeriod, Direction, CounterpartyId, blQuantity, Price, changedBy, reason,
            ContractVolume: Volume, CargoTerms: CargoTerms.ToRef());
        return (history, delivered);
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
