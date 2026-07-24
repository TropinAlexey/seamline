namespace Seamline.Modules.Audit.Internal;

// ADR-0006: "audit.audit_event — cross-module actor/action/timestamp/context
// record." Append-only, like trading.trade_history — rows are written once by
// the consumers below and never updated, enforced the same way (seamline_app
// gets GRANT SELECT, INSERT only, see the InitialCreate migration).
internal sealed class AuditEvent
{
    public Guid Id { get; private init; }
    public Guid TenantId { get; private init; }
    public DateTimeOffset OccurredAt { get; private init; }
    public string Actor { get; private init; } = string.Empty;
    public string Action { get; private init; } = string.Empty;
    public string EntityType { get; private init; } = string.Empty;
    public Guid EntityId { get; private init; }
    public string Context { get; private init; } = string.Empty;

    public static AuditEvent Create(
        Guid tenantId, string actor, string action, string entityType, Guid entityId, string context) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OccurredAt = DateTimeOffset.UtcNow,
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Context = context
        };
}
