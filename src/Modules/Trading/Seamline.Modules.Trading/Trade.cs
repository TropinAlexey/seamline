using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

// Draft -> Submitted -> Active | CreditPending -> Active | Rejected.
// Matches ADR-0008: the saga and the trade lifecycle are the same machine.
// Cancelled/Amended/Delivered/Settled are out of scope for now.
internal enum TradeState
{
    Draft = 1,
    Submitted = 2,
    CreditPending = 3,
    Active = 4,
    Rejected = 5
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
        if (string.IsNullOrWhiteSpace(deliveryPeriod))
            throw new ArgumentException("Delivery period is required.", nameof(deliveryPeriod));
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
        var activated = new TradeActivated(Id, TenantId.Value, CommodityCode, DeliveryPeriod, Direction, Volume, Price, CounterpartyId);
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
        var rejected = new TradeRejected(Id, TenantId.Value);
        return (history, rejected);
    }

    private void RequireState(string methodName, params ReadOnlySpan<TradeState> allowed)
    {
        if (!allowed.Contains(State))
            throw new InvalidOperationException(
                $"Trade {Id}: {methodName} requires state {string.Join(" or ", allowed.ToArray())}, was {State}.");
    }
}
