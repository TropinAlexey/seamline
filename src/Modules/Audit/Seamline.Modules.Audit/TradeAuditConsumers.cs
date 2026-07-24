using MassTransit;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Audit.Internal;

// Audit only observes events that already cross a module boundary — it never
// reaches into another module's schema, and it never publishes anything of
// its own. Actor/reason ride along on TradeActivated/TradeRejected
// specifically so Audit doesn't need a separate query back to Trading.
//
// tenantContext.SetTenant must happen before db is touched: the RLS policy
// on audit_event (ADR-0005 layer 2) checks the inserted row's tenant_id
// against the Postgres session variable TenantSessionVariableInterceptor
// sets from this same tenantContext when the connection opens. Skipping
// this — as these three consumers originally did — left tenantContext at
// its default (empty tenant), which the EF Core query filter (layer 1)
// never caught because it only applies to SELECT, not INSERT. RLS is what
// actually surfaced the gap.
internal static class AuditRecorder
{
    public static async Task RecordAsync(
        AuditDbContext db, TenantContext tenantContext,
        Guid tenantId, string actor, string action, Guid entityId, string reason,
        CancellationToken cancellationToken)
    {
        tenantContext.SetTenant(new TenantId(tenantId));
        db.AuditEvents.Add(AuditEvent.Create(tenantId, actor, action, "Trade", entityId, reason));
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class TradeActivatedAuditConsumer(AuditDbContext db, TenantContext tenantContext) : IConsumer<TradeActivated>
{
    public Task Consume(ConsumeContext<TradeActivated> context)
    {
        var message = context.Message;
        return AuditRecorder.RecordAsync(
            db, tenantContext, message.TenantId, message.Actor, nameof(TradeActivated), message.TradeId, message.Reason,
            context.CancellationToken);
    }
}

internal sealed class TradeRejectedAuditConsumer(AuditDbContext db, TenantContext tenantContext) : IConsumer<TradeRejected>
{
    public Task Consume(ConsumeContext<TradeRejected> context)
    {
        var message = context.Message;
        return AuditRecorder.RecordAsync(
            db, tenantContext, message.TenantId, message.Actor, nameof(TradeRejected), message.TradeId, message.Reason,
            context.CancellationToken);
    }
}

internal sealed class TradeAmendedAuditConsumer(AuditDbContext db, TenantContext tenantContext) : IConsumer<TradeAmended>
{
    public Task Consume(ConsumeContext<TradeAmended> context)
    {
        var message = context.Message;
        return AuditRecorder.RecordAsync(
            db, tenantContext, message.TenantId, message.Actor, nameof(TradeAmended), message.TradeId, message.Reason,
            context.CancellationToken);
    }
}
