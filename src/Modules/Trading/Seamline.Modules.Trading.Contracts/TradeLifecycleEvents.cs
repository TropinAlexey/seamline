namespace Seamline.Modules.Trading.Contracts;

// Lifecycle steps no other module needs to react to today, published so the
// audit trail is complete rather than starting at activation (ADR-0031).
// Same outbox path as TradeActivated: written in the same transaction as
// the state change they describe.

public sealed record TradeCreated(Guid TradeId, Guid TenantId, string Actor, string Reason);

public sealed record TradeSubmitted(Guid TradeId, Guid TenantId, string Actor, string Reason);

public sealed record TradeCancelled(Guid TradeId, Guid TenantId, string Actor, string Reason);
