using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Seamline.Modules.MarketData.Internal;
using Seamline.Modules.Reference.Internal;
using Seamline.Modules.Risk.Internal;
using Seamline.SharedKernel;
using Seamline.Valuation.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<TenantSessionVariableInterceptor>();

// Reference is wired even though this worker never calls it directly —
// AddRiskModule registers ICreditReservationService, which depends on
// ICounterpartyDirectory. Same full-module-graph composition Seamline.Api
// already does (ADR-0002): a worker hosts a module's logic, not a curated
// subset of its DI registrations.
builder.Services.AddReferenceModule(builder.Configuration);
builder.Services.AddRiskModule(builder.Configuration);
builder.Services.AddMarketDataModule(builder.Configuration);
builder.Services.AddScoped<ValuationJob>();

// Hangfire's own job-storage tables live in the same database as
// everything else (ADR-0002 — a process split, not a database split),
// created via the owner connection since Hangfire manages its own DDL on
// first run, the same way EF migrations do for every module.
builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(
        builder.Configuration.GetConnectionString("PostgresMigrator"))));
builder.Services.AddHangfireServer();

var host = builder.Build();

await host.Services.MigrateRiskModuleAsync();
await host.Services.MigrateMarketDataModuleAsync();

// Recurring, instance-independent work — Hangfire, not MassTransit
// Schedule<> (that's for a timeout owned by one specific saga/process
// instance, e.g. the credit-approval timeout in ADR-0008). See ADR-0003
// for the rule once it's written. Resolved through DI (IRecurringJobManager)
// rather than the static RecurringJob API — the static API relies on a
// global JobStorage.Current that AddHangfire's service-based registration
// doesn't set.
host.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<ValuationJob>(
    "eod-valuation", job => job.RunAsync(CancellationToken.None), Cron.Daily);

await host.RunAsync();
