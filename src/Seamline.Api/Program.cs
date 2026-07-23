using System.Text.Json.Serialization;
using MassTransit;
using Seamline.Modules.Reference.Internal;
using Seamline.Modules.Risk.Internal;
using Seamline.Modules.Trading.Internal;
using Seamline.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

builder.Services.AddReferenceModule(builder.Configuration);
builder.Services.AddTradingModule(builder.Configuration);
builder.Services.AddRiskModule(builder.Configuration);

builder.Services.AddMassTransit(x =>
{
    x.AddTradingMassTransitConfiguration();
    x.AddRiskMassTransitConfiguration();

    x.UsingInMemory((context, cfg) =>
    {
        // The credit-limit saga's approval timeout (ADR-0008) uses
        // MassTransit's own Schedule<>, not Hangfire — Hangfire is for work
        // that isn't tied to a specific saga instance (EOD jobs, sweeps).
        cfg.UseDelayedMessageScheduler();

        // Covers the outbox's polling delay: an approve/reject can arrive
        // before the saga instance it targets has been created from the
        // (separately outboxed) TradeApprovalRequested. Combined with
        // OnMissingInstance(Fault()) in TradeApprovalStateMachine, that race
        // becomes a few retried deliveries instead of a silently dropped
        // approval.
        cfg.UseMessageRetry(r => r.Intervals(100, 250, 500, 1000, 2000));

        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

await app.Services.MigrateReferenceModuleAsync();
await app.Services.MigrateTradingModuleAsync();
await app.Services.MigrateRiskModuleAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Every request carries the tenant explicitly — there is no default tenant.
// See docs/adr/0005-multi-tenancy.md.
app.Use(async (context, next) =>
{
    if (!context.Request.Headers.TryGetValue("X-Tenant-Id", out var header) ||
        !Guid.TryParse(header, out var tenantId))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("X-Tenant-Id header is required.");
        return;
    }

    context.RequestServices.GetRequiredService<TenantContext>().SetTenant(new TenantId(tenantId));
    await next();
});

app.MapReferenceEndpoints();
app.MapTradingEndpoints();
app.MapRiskEndpoints();

app.Run();

public partial class Program;
