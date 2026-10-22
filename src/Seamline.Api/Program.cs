using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
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

// Build-time OpenAPI generation (Microsoft.Extensions.ApiDescription.Server,
// ADR-0030) runs this entry point up to app.Run() inside a host named
// GetDocument.Insider. Endpoints must still be mapped, but there is no
// database (and no connection string) at build time — touching it would
// fail the build.
var isGeneratingOpenApiDocument = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";


builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // The web defaults also accept numbers sent as strings, which makes the
    // OpenAPI schema describe every decimal as "number | string" and leaks
    // into the generated TS client. Output is unaffected — decimals are
    // still written as JSON numbers (ADR-0030).
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

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

var healthChecks = builder.Services.AddHealthChecks();
if (!isGeneratingOpenApiDocument)
    healthChecks.AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!, name: "postgres");

// One fixed window per client IP, not one shared by every caller — a
// single global window let any client lock everyone else out of login for
// the rest of the minute (ADR-0030). Read per partition from the request's
// configuration so tests can lower it without process-wide env vars.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue("RateLimiting:LoginPermitLimit", 5),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Behind nginx (compose) or ingress + nginx (k8s) every request arrives from
// a proxy, which would collapse the per-IP limiter above back into one
// global window. X-Forwarded-For is trusted only when ReverseProxy:KnownNetworks
// is set (the middleware itself is not registered otherwise — with empty
// known lists ASP.NET Core would accept the header from any caller), and
// only for exactly ReverseProxy:ForwardLimit hops: 1 behind compose's nginx,
// 2 behind Traefik + nginx in k8s. Consuming a fixed number of hops from the
// right means a client-supplied entry further left is never reached, even
// when the client's own address happens to sit inside a trusted range.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = builder.Configuration.GetValue("ReverseProxy:ForwardLimit", 1);
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    var networks = builder.Configuration["ReverseProxy:KnownNetworks"] ?? string.Empty;
    foreach (var cidr in networks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
});

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
        cfg.UseMessageRetry(r => r.Intervals(200, 500, 1000, 2000, 5000));
        cfg.ConfigureEndpoints(context);
    }

    // No broker exists at build time either; in-memory keeps document
    // generation from trying (and retrying) to reach RabbitMQ.
    var transport = isGeneratingOpenApiDocument ? "InMemory" : builder.Configuration["MessageBroker:Transport"];

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

if (!isGeneratingOpenApiDocument)
{
    try
    {
        await EnsureAppRoleAsync(app.Configuration, app.Logger);

        await app.Services.MigrateReferenceModuleAsync();
        await app.Services.MigrateTradingModuleAsync();
        await app.Services.MigrateRiskModuleAsync();
        await app.Services.MigrateAuditModuleAsync();
        await app.Services.MigrateMarketDataModuleAsync();
        await app.Services.MigrateSettlementModuleAsync();
        await app.Services.MigrateIdentityModuleAsync();
    }
    catch (Exception ex)
    {
        // Exit explicitly instead of letting the exception go unhandled: an unhandled
        // exception aborts via SIGABRT, which hangs the process if it runs as PID 1 without
        // an init (ADR-0029). This also gives one readable log line and exit code 1.
        app.Logger.LogCritical(ex, "Database initialization failed");
        await app.DisposeAsync(); // flushes the console logger
        Environment.ExitCode = 1;
        return;
    }
}

if (args.Contains("--migrate-only"))
    return;

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!string.IsNullOrWhiteSpace(app.Configuration["ReverseProxy:KnownNetworks"]))
    app.UseForwardedHeaders();

app.UseHttpsRedirection();

// ArgumentException → 400, DomainRuleViolationException → 409. Nothing
// broader: a bare InvalidOperationException is a bug and stays a 500.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ArgumentException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new ErrorResponse(ex.Message));
    }
    catch (DomainRuleViolationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new ErrorResponse(ex.Message));
    }
});

app.UseRateLimiter();

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
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/detail", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                error = app.Environment.IsDevelopment() ? e.Value.Exception?.Message : null,
            }),
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true }));
    },
}).AllowAnonymous();

app.MapAuthEndpoints();
app.MapReferenceEndpoints();
app.MapTradingEndpoints();
app.MapRiskEndpoints();
app.MapMarketDataEndpoints();
app.MapSettlementEndpoints();
app.MapAuditEndpoints();

app.Run();

// Idempotent: creates the restricted seamline_app role if it doesn't exist.
// In Docker this role is created by docker/postgres-init/01-create-app-role.sql;
// on RDS (or any fresh Postgres) nothing pre-seeds it, but every module
// migration GRANTs permissions to it — so the role must exist before the
// first migration runs.
static async Task EnsureAppRoleAsync(IConfiguration configuration, ILogger logger)
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

    await using var conn = await OpenWithRetryAsync(migratorConn, logger);

    await using var cmd = conn.CreateCommand();
    // Values passed as Npgsql parameters, then injected into the DO block
    // via format(%I/%L) to avoid SQL injection from connection-string values
    // that contain apostrophes or special characters.
    cmd.CommandText = """
        DO $body$
        DECLARE
            v_user text := current_setting('app.ensure_user');
            v_pass text := current_setting('app.ensure_pass');
            v_db   text := current_setting('app.ensure_db');
        BEGIN
            IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = v_user) THEN
                EXECUTE format('CREATE ROLE %I WITH LOGIN PASSWORD %L', v_user, v_pass);
                EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', v_db, v_user);
            END IF;
        END $body$;
        """;
    await using var setVars = conn.CreateCommand();
    setVars.CommandText = "SELECT set_config('app.ensure_user', $1, false), set_config('app.ensure_pass', $2, false), set_config('app.ensure_db', $3, false)";
    setVars.Parameters.AddWithValue(appUser);
    setVars.Parameters.AddWithValue(appPassword);
    setVars.Parameters.AddWithValue(dbName);
    await setVars.ExecuteNonQueryAsync();
    await cmd.ExecuteNonQueryAsync();
}

// Postgres may not accept connections yet when the pod/task starts alongside it
// (fresh k3s cluster, docker compose, RDS failover). Bounded backoff — 2+4+8+16+32 s,
// about a minute — then give up so the orchestrator's own restart policy takes over.
static async Task<Npgsql.NpgsqlConnection> OpenWithRetryAsync(string connectionString, ILogger logger)
{
    const int maxAttempts = 6;
    for (var attempt = 1; ; attempt++)
    {
        var conn = new Npgsql.NpgsqlConnection(connectionString);
        try
        {
            await conn.OpenAsync();
            return conn;
        }
        catch (Npgsql.NpgsqlException ex) when (ex.IsTransient && attempt < maxAttempts)
        {
            await conn.DisposeAsync();
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            logger.LogWarning("Postgres not reachable (attempt {Attempt}/{MaxAttempts}): {Error}. Retrying in {Delay}",
                attempt, maxAttempts, ex.Message, delay);
            await Task.Delay(delay);
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }
}
