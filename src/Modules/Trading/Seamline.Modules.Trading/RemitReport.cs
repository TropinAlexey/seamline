using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

internal enum RemitAction
{
    New = 1,
    Modify = 2,
    Terminate = 3
}

// Append-only, same shape as TradeHistory (ADR-0006) and audit_event
// (ADR-0010) — inserted only after a successful ack from acer-stub, never
// updated. No Pending/Failed status: a trade_history row with no matching
// RemitReport row just means "not yet reported", found again by the next
// EOD run's LEFT JOIN. See ADR-0015.
internal sealed class RemitReport
{
    public Guid Id { get; private init; }
    public TenantId TenantId { get; private init; }
    public Guid TradeId { get; private init; }
    public int Version { get; private init; }
    public RemitAction Action { get; private init; }
    public DateTimeOffset SubmittedAt { get; private init; }
    public string AckId { get; private init; } = string.Empty;

    private RemitReport() { }

    public static RemitReport CreateAcknowledged(TenantId tenantId, Guid tradeId, int version, RemitAction action, string ackId)
    {
        return new RemitReport
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TradeId = tradeId,
            Version = version,
            Action = action,
            SubmittedAt = DateTimeOffset.UtcNow,
            AckId = ackId
        };
    }
}
