namespace Seamline.Modules.Trading.Contracts;

// Integration event, published transactionally with the trade's state change
// via the MassTransit EF Core outbox (see ADR-0004, when written). Consumed
// by Risk to update the derived position — Risk never writes trade data
// itself, it only reacts to this.
public sealed record TradeActivated(
    Guid TradeId,
    Guid TenantId,
    string CommodityCode,
    string DeliveryPeriod,
    TradeDirection Direction,
    decimal Volume,
    decimal Price,
    Guid CounterpartyId);
