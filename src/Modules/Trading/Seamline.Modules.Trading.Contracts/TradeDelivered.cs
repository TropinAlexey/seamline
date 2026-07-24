namespace Seamline.Modules.Trading.Contracts;

// Published when Trade.Deliver() moves a trade Active -> Delivered (see
// ADR-0011, revisited: Settlement now exists to consume this). Volume and
// Price ride along so Settlement can compute the invoice amount without a
// synchronous call back into Trading.
public sealed record TradeDelivered(
    Guid TradeId,
    Guid TenantId,
    Guid CounterpartyId,
    decimal Volume,
    decimal Price,
    string Actor,
    string Reason);
