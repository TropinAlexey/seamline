namespace Seamline.Modules.Trading.Internal;

// Saga-internal messages. These never cross the module boundary — the
// public integration events other modules consume live in Trading.Contracts.
internal sealed record TradeApprovalRequested(Guid TradeId, Guid TenantId, Guid CounterpartyId, decimal Notional);

// Actor is the MO user's login from the JWT, carried through the saga so the
// resulting history row and audit event name the real approver (ADR-0030).
internal sealed record TradeApprovalGranted(Guid TradeId, string Actor);

internal sealed record TradeApprovalDenied(Guid TradeId, string Actor);

internal sealed record ApprovalTimeoutExpired(Guid TradeId);

internal sealed record TradeApprovalCompleted(Guid TradeId, Guid TenantId, bool Approved, string Actor, string Reason);

// A trader cancels a trade while it's still waiting on risk approval.
// Routed through the saga (not applied to Trade directly) because the saga
// holds the approval timeout schedule and must stop waiting for a decision
// that no longer matters.
internal sealed record TradeCancelRequested(Guid TradeId, string Actor);

internal sealed record TradeApprovalCancelled(Guid TradeId, Guid TenantId, string Actor);
