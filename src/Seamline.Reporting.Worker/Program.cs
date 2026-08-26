using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Seamline.Modules.Trading.Internal;
using Seamline.Reporting.Worker;
using Seamline.SharedKernel;

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("seamline-reporting-worker"))
    .WithTracing(t => t
        .AddHttpClientInstrumentation()
        .AddSource("Npgsql")
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("Npgsql")
        .AddOtlpExporter());

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!, name: "postgres");

builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<TenantSessionVariableInterceptor>();

builder.Services.AddTradingModule(builder.Configuration);
builder.Services.AddTradingReportingClient(builder.Configuration);
builder.Services.AddScoped<ReportingJob>();

// Hangfire's own job-storage tables live in the same database as
// everything else (ADR-0002 — a process split, not a database split),
// created via the owner connection since Hangfire manages its own DDL on
// first run, the same way EF migrations do for every module.
builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(
        builder.Configuration.GetConnectionString("PostgresMigrator")),
        new PostgreSqlStorageOptions { SchemaName = "hangfire_reporting" }));
builder.Services.AddHangfireServer();

var app = builder.Build();

await app.Services.MigrateTradingModuleAsync();

app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

// Recurring, instance-independent work — Hangfire, not MassTransit
// Schedule<> (ADR-0003). Resolved through DI (IRecurringJobManager) rather
// than the static RecurringJob API — the static API relies on a global
// JobStorage.Current that AddHangfire's service-based registration doesn't
// set.
app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<ReportingJob>(
    "remit-reporting", job => job.RunAsync(CancellationToken.None), Cron.Daily);

await app.RunAsync();
