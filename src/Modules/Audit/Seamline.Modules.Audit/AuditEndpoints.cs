using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Identity.Contracts;

namespace Seamline.Modules.Audit.Internal;

public static class AuditEndpoints
{
    private const int MaxPageSize = 100;

    // Read-only window onto audit.audit_event (ADR-0010), newest first.
    // Tenant-scoped by the same query filter + RLS as every other table.
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/audit-events", async (
            [FromQuery(Name = "entityId")] Guid? entityId,
            [FromQuery(Name = "page")] int? page,
            [FromQuery(Name = "pageSize")] int? pageSize,
            AuditDbContext db,
            CancellationToken ct) =>
        {
            var pageNumber = Math.Max(page ?? 1, 1);
            var size = Math.Clamp(pageSize ?? 50, 1, MaxPageSize);

            var events = db.AuditEvents.AsNoTracking();
            if (entityId is { } id)
                events = events.Where(e => e.EntityId == id);

            var total = await events.CountAsync(ct);
            var items = await events
                .OrderByDescending(e => e.OccurredAt).ThenBy(e => e.Id)
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .Select(e => new AuditEventEntry(e.Id, e.OccurredAt, e.Actor, e.Action, e.EntityType, e.EntityId, e.Context))
                .ToListAsync(ct);

            return Results.Ok(new AuditEventPage(items, total));
        })
        .WithTags("Audit")
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.MiddleOffice, IdentityRoles.BackOffice))
        .Produces<AuditEventPage>();

        return app;
    }
}

internal sealed record AuditEventEntry(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Actor,
    string Action,
    string EntityType,
    Guid EntityId,
    string Context);

internal sealed record AuditEventPage(IReadOnlyList<AuditEventEntry> Items, int Total);
