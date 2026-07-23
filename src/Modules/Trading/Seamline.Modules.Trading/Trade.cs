using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

// Draft -> Confirmed only for now. The full lifecycle (CreditChecked, Active,
// Delivered, Settled, Cancelled, Amended) arrives with the credit-limit saga;
// see the open question on saga ownership in CLAUDE.md history.
internal enum TradeState
{
    Draft = 1,
    Confirmed = 2
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
            State = TradeState.Draft
        };
    }

    public TradeConfirmed Confirm()
    {
        if (State != TradeState.Draft)
            throw new InvalidOperationException($"Trade {Id} cannot be confirmed from state {State}.");

        State = TradeState.Confirmed;

        return new TradeConfirmed(Id, TenantId.Value, CommodityCode, DeliveryPeriod, Direction, Volume, Price, CounterpartyId);
    }
}
