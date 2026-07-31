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
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("seamline-api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Npgsql")
        .AddSource("MassTransit")
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!, name: "postgres");

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

    // Shared by both transports (ADR-0017) — only the host/credentials setup
    // below differs between them.
    void ConfigurePipeline<TEndpoint>(IBusFactoryConfigurator<TEndpoint> cfg, IBusRegistrationContext context)
        where TEndpoint : IReceiveEndpointConfigurator
    {
        // The credit-limit saga's approval timeout (ADR-0008) uses
        // MassTransit's own Schedule<>, not Hangfire — Hangfire is for work
        // that isn't tied to a specific saga instance (EOD jobs, sweeps).
        // On RabbitMQ this needs the delayed-message-exchange plugin
        // (docker/rabbitmq/Dockerfile).
        cfg.UseDelayedMessageScheduler();

        // Covers the outbox's polling delay: an approve/reject can arrive
        // before the saga instance it targets has been created from the
        // (separately outboxed) TradeApprovalRequested. Combined with
        // OnMissingInstance(Fault()) in TradeApprovalStateMachine, that race
        // becomes a few retried deliveries instead of a silently dropped
        // approval.
        cfg.UseMessageRetry(r => r.Intervals(100, 250, 500, 1000, 2000));

        cfg.ConfigureEndpoints(context);
    }

    // Tests set this to "InMemory" (SeamlineApiFactory) so every existing
    // integration test keeps running without a real broker; local dev and
    // everything else defaults to RabbitMQ (ADR-0017).
    if (builder.Configuration["MessageBroker:Transport"] == "InMemory")
    {
        x.UsingInMemory((context, cfg) => ConfigurePipeline(cfg, context));
    }
    else
    {
        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host(
                builder.Configuration["MessageBroker:RabbitMq:Host"] ?? "localhost",
                builder.Configuration["MessageBroker:RabbitMq:VirtualHost"] ?? "/",
                h =>
                {
                    h.Username(builder.Configuration["MessageBroker:RabbitMq:Username"] ?? "guest");
                    h.Password(builder.Configuration["MessageBroker:RabbitMq:Password"] ?? "guest");
                });

            ConfigurePipeline(cfg, context);
        });
    }
});

var app = builder.Build();

await EnsureAppRoleAsync(app.Configuration);

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

app.MapHealthChecks("/health").AllowAnonymous();

app.MapAuthEndpoints();
app.MapReferenceEndpoints();
app.MapTradingEndpoints();
app.MapRiskEndpoints();
app.MapMarketDataEndpoints();
app.MapSettlementEndpoints();

app.Run();

// Idempotent: creates the restricted seamline_app role if it doesn't exist.
// In Docker this role is created by docker/postgres-init/01-create-app-role.sql;
// on RDS (or any fresh Postgres) nothing pre-seeds it, but every module
// migration GRANTs permissions to it — so the role must exist before the
// first migration runs.
static async Task EnsureAppRoleAsync(IConfiguration configuration)
{
    var migratorConn = configuration.GetConnectionString("PostgresMigrator")
        ?? throw new InvalidOperationException("PostgresMigrator connection string is required.");
    var appConn = configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Postgres connection string is required.");

    var appConnBuilder = new Npgsql.NpgsqlConnectionStringBuilder(appConn);
    var migratorConnBuilder = new Npgsql.NpgsqlConnectionStringBuilder(migratorConn);
    var appUser = appConnBuilder.Username ?? "seamline_app";
    var appPassword = appConnBuilder.Password ?? "seamline_app";
    var dbName = migratorConnBuilder.Database ?? "seamline";

    await using var conn = new Npgsql.NpgsqlConnection(migratorConn);
    await conn.OpenAsync();

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = $"""
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{appUser}') THEN
                EXECUTE format('CREATE ROLE %I WITH LOGIN PASSWORD %L', '{appUser}', '{appPassword}');
                EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', '{dbName}', '{appUser}');
            END IF;
        END $$;
        """;
    await cmd.ExecuteNonQueryAsync();
}
