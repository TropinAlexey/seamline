namespace Seamline.Modules.Trading.Contracts;

// Published when Trade.Deliver() moves a trade Active -> Delivered (see
// ADR-0011, revisited: Settlement now exists to consume this). Volume and
// Price ride along so Settlement can compute the invoice amount without a
// synchronous call back into Trading. CommodityCode/DeliveryPeriod/Direction
// (ADR-0014, Valuation.Worker) let Risk's own TradeDeliveredConsumer find
// and close out the delivered volume's contribution to Position — Settlement
// doesn't need them, but they're additive to the event, not a breaking
// change to Settlement's existing consumer.
public sealed record TradeDelivered(
    Guid TradeId,
    Guid TenantId,
    string CommodityCode,
    string DeliveryPeriod,
    TradeDirection Direction,
    Guid CounterpartyId,
    decimal Volume,
    decimal Price,
    string Actor,
    string Reason);
