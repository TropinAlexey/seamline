using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Seamline.SharedKernel;
using Seamline.Modules.Risk.Internal;
using System.Text.Json.Serialization;
using Seamline.Modules.Audit.Internal;
using Seamline.Modules.Trading.Internal;
using Seamline.Modules.Reference.Internal;
using Seamline.Modules.MarketData.Internal;
using Seamline.Modules.Settlement.Internal;
using Seamline.Modules.Identity.Internal;
using Seamline.Modules.Identity.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!)),
            ValidateLifetime = true
        };
    });

// Every endpoint requires a valid JWT unless it opts out with
// AllowAnonymous() (only /auth/login does) — see docs/adr/0013.
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<TenantSessionVariableInterceptor>();

builder.Services.AddReferenceModule(builder.Configuration);
builder.Services.AddTradingModule(builder.Configuration);
builder.Services.AddRiskModule(builder.Configuration);
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddMarketDataModule(builder.Configuration);
builder.Services.AddSettlementModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);

builder.Services.AddMassTransit(x =>
{
    x.AddTradingMassTransitConfiguration();
    x.AddRiskMassTransitConfiguration();
    x.AddAuditMassTransitConfiguration();
    x.AddSettlementMassTransitConfiguration();

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
await app.Services.MigrateAuditModuleAsync();
await app.Services.MigrateMarketDataModuleAsync();
await app.Services.MigrateSettlementModuleAsync();
await app.Services.MigrateIdentityModuleAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();

// Every authenticated request carries its tenant as a signed JWT claim, set
// once at /auth/login — there is no default tenant, and (unlike the header
// this replaced) it can no longer be set to an arbitrary value by the
// caller. See docs/adr/0005-multi-tenancy.md and docs/adr/0013. /auth/login
// itself is the one endpoint with no authenticated context yet to read a
// tenant claim from — it sets TenantContext explicitly from its request
// body instead (see AuthEndpoints).
app.Use(async (context, next) =>
{
    var tenantClaim = context.User.FindFirst(IdentityClaimTypes.TenantId)?.Value;
    if (tenantClaim is not null && Guid.TryParse(tenantClaim, out var tenantId))
    {
        context.RequestServices.GetRequiredService<TenantContext>().SetTenant(new TenantId(tenantId));
    }

    await next();
});

app.UseAuthorization();

app.MapAuthEndpoints();
app.MapReferenceEndpoints();
app.MapTradingEndpoints();
app.MapRiskEndpoints();
app.MapMarketDataEndpoints();
app.MapSettlementEndpoints();

app.Run();
