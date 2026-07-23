using MassTransit;

namespace Seamline.Modules.Trading.Internal;

// CorrelationId = TradeId, so redelivery of any event resolves to the same
// instance — idempotency by correlation, not by a separate dedup table.
// See ADR-0008.
internal sealed class TradeApprovalState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid CounterpartyId { get; set; }
    public decimal Notional { get; set; }
    public Guid? ApprovalTimeoutTokenId { get; set; }
}
