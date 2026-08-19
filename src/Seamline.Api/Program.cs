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
        .AddRuntimeInstrumentation()
        .AddMeter("Npgsql")
        .AddMeter("MassTransit")
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

    // Transport-agnostic pipeline (ADR-0017, ADR-0020). Message scheduler is
    // transport-specific and configured inside each transport branch below.
    void ConfigurePipeline<TEndpoint>(IBusFactoryConfigurator<TEndpoint> cfg, IBusRegistrationContext context)
        where TEndpoint : IReceiveEndpointConfigurator
    {
        cfg.UseMessageRetry(r => r.Intervals(100, 250, 500, 1000, 2000));
        cfg.ConfigureEndpoints(context);
    }

    var transport = builder.Configuration["MessageBroker:Transport"];

    // The message scheduler is load-bearing: the credit-limit saga (ADR-0008)
    // uses Schedule<> for approval timeouts. Each transport provides its own
    // delayed-delivery mechanism (ADR-0020 §2).
    if (transport == "InMemory")
    {
        x.UsingInMemory((context, cfg) =>
        {
            cfg.UseDelayedMessageScheduler();
            ConfigurePipeline(cfg, context);
        });
    }
    else if (transport == "AzureServiceBus")
    {
        x.UsingAzureServiceBus((context, cfg) =>
        {
            cfg.Host(builder.Configuration["MessageBroker:AzureServiceBus:ConnectionString"]
                ?? throw new InvalidOperationException(
                    "MessageBroker:AzureServiceBus:ConnectionString is required " +
                    "when Transport is AzureServiceBus."));

            // Topology pre-provisioned in Bicep; runtime identity is send/listen
            // only, no Manage claim (ADR-0020 §4).
            cfg.DeployTopologyOnly = false;

            cfg.UseServiceBusMessageScheduler();
            ConfigurePipeline(cfg, context);
        });
    }
    else if (transport is null or "RabbitMQ")
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

            // RabbitMQ delayed-message-exchange plugin (docker/rabbitmq/Dockerfile)
            cfg.UseDelayedMessageScheduler();
            ConfigurePipeline(cfg, context);
        });
    }
    else
    {
        throw new InvalidOperationException(
            $"Unknown MessageBroker:Transport '{transport}'. " +
            "Expected: InMemory, RabbitMQ, or AzureServiceBus.");
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
