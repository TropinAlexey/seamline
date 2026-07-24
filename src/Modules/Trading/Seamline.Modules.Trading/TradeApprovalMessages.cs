namespace Seamline.Modules.Trading.Internal;

// Saga-internal messages. These never cross the module boundary — only
// TradeActivated and TradeRejected (in Trading.Contracts) are the public
// integration events another module ever consumes.
internal sealed record TradeApprovalRequested(Guid TradeId, Guid TenantId, Guid CounterpartyId, decimal Notional);

internal sealed record TradeApprovalGranted(Guid TradeId);

internal sealed record TradeApprovalDenied(Guid TradeId);

internal sealed record ApprovalTimeoutExpired(Guid TradeId);

internal sealed record TradeApprovalCompleted(Guid TradeId, Guid TenantId, bool Approved);

// A trader cancels a trade while it's still waiting on risk approval.
// Routed through the saga (not applied to Trade directly) because the saga
// holds the approval timeout schedule and must stop waiting for a decision
// that no longer matters.
internal sealed record TradeCancelRequested(Guid TradeId);

internal sealed record TradeApprovalCancelled(Guid TradeId, Guid TenantId);
