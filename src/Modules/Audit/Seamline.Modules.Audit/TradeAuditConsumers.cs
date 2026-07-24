using MassTransit;
using Seamline.Modules.Trading.Contracts;

namespace Seamline.Modules.Audit.Internal;

// Audit only observes events that already cross a module boundary — it never
// reaches into another module's schema, and it never publishes anything of
// its own. Actor/reason ride along on TradeActivated/TradeRejected
// specifically so Audit doesn't need a separate query back to Trading.
internal sealed class TradeActivatedAuditConsumer(AuditDbContext db) : IConsumer<TradeActivated>
{
    public async Task Consume(ConsumeContext<TradeActivated> context)
    {
        var message = context.Message;
        db.AuditEvents.Add(AuditEvent.Create(
            message.TenantId, message.Actor, nameof(TradeActivated), "Trade", message.TradeId, message.Reason));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

internal sealed class TradeRejectedAuditConsumer(AuditDbContext db) : IConsumer<TradeRejected>
{
    public async Task Consume(ConsumeContext<TradeRejected> context)
    {
        var message = context.Message;
        db.AuditEvents.Add(AuditEvent.Create(
            message.TenantId, message.Actor, nameof(TradeRejected), "Trade", message.TradeId, message.Reason));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

internal sealed class TradeAmendedAuditConsumer(AuditDbContext db) : IConsumer<TradeAmended>
{
    public async Task Consume(ConsumeContext<TradeAmended> context)
    {
        var message = context.Message;
        db.AuditEvents.Add(AuditEvent.Create(
            message.TenantId, message.Actor, nameof(TradeAmended), "Trade", message.TradeId, message.Reason));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
