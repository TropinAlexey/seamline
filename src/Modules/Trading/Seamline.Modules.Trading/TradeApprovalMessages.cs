namespace Seamline.Modules.Trading.Internal;

// Saga-internal messages. These never cross the module boundary — only
// TradeActivated and TradeRejected (in Trading.Contracts) are the public
// integration events another module ever consumes.
internal sealed record TradeApprovalRequested(Guid TradeId, Guid TenantId, Guid CounterpartyId, decimal Notional);

internal sealed record TradeApprovalGranted(Guid TradeId);

internal sealed record TradeApprovalDenied(Guid TradeId);

internal sealed record ApprovalTimeoutExpired(Guid TradeId);

internal sealed record TradeApprovalCompleted(Guid TradeId, Guid TenantId, bool Approved);
