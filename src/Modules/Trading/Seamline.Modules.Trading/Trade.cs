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

    private Trade() { }

    public static Trade CreateDraft(
        TenantId tenantId,
        string commodityCode,
        string deliveryPeriod,
        TradeDirection direction,
        decimal volume,
        decimal price,
        Guid counterpartyId)
    {
        if (string.IsNullOrWhiteSpace(commodityCode))
            throw new ArgumentException("Commodity code is required.", nameof(commodityCode));
        DeliveryPeriodFormat.Validate(deliveryPeriod);
        if (volume <= 0)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be positive.");
        if (price <= 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");

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
            State = TradeState.Draft,
            Version = 1
        };
    }

    public decimal Notional => Volume * Price;

    public TradeHistory Submit(string changedBy, string changeReason)
    {
        RequireState(nameof(Submit), TradeState.Draft);
        State = TradeState.Submitted;
        Version++;
        return TradeHistory.CreateSnapshot(this, changedBy, changeReason);
    }

    public (TradeHistory History, TradeActivated Event) Activate(string changedBy, string changeReason)
    {
        RequireState(nameof(Activate), TradeState.Submitted, TradeState.CreditPending);
        State = TradeState.Active;
        Version++;
        var history = TradeHistory.CreateSnapshot(this, changedBy, changeReason);
        var activated = new TradeActivated(Id, TenantId.Value, CommodityCode, DeliveryPeriod, Direction, Volume, Price, CounterpartyId, changedBy, changeReason);
        return (history, activated);
    }

    public TradeHistory EnterCreditPending(string changedBy, string changeReason)
    {
        RequireState(nameof(EnterCreditPending), TradeState.Submitted);
        State = TradeState.CreditPending;
        Version++;
        return TradeHistory.CreateSnapshot(this, changedBy, changeReason);
    }

    public (TradeHistory History, TradeRejected Event) Reject(string changedBy, string changeReason)
    {
        RequireState(nameof(Reject), TradeState.Submitted, TradeState.CreditPending);
        State = TradeState.Rejected;
        Version++;
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
        Version++;
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
        Version++;
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
        State = TradeState.Delivered;
        Version++;
        var history = TradeHistory.CreateSnapshot(this, changedBy, changeReason);
        var delivered = new TradeDelivered(
            Id, TenantId.Value, CommodityCode, DeliveryPeriod, Direction, CounterpartyId, Volume, Price, changedBy, changeReason);
        return (history, delivered);
    }

    private void RequireState(string methodName, params ReadOnlySpan<TradeState> allowed)
    {
        if (!allowed.Contains(State))
            throw new InvalidOperationException(
                $"Trade {Id}: {methodName} requires state {string.Join(" or ", allowed.ToArray())}, was {State}.");
    }
}
