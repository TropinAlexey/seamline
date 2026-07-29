using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

public static class TradingModuleExtensions
{
    public static IServiceCollection AddTradingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TradingDbContext>((sp, options) =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"),
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TradingDbContext.Schema))
                .AddInterceptors(sp.GetRequiredService<TenantSessionVariableInterceptor>()));

        return services;
    }

    // Separate from AddTradingModule — only Seamline.Reporting.Worker needs
    // this HttpClient, not Seamline.Api. AddStandardResilienceHandler()
    // (Microsoft.Extensions.Http.Resilience, Polly v8 underneath) bundles
    // retry/timeout/circuit-breaker against acer-stub's simulated flakiness
    // — see ADR-0015 on why this over a hand-rolled retry loop.
    public static IServiceCollection AddTradingReportingClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IRemitSubmissionClient, RemitSubmissionClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["AcerStub:BaseUrl"]!);
        }).AddStandardResilienceHandler();

        return services;
    }

    // Runs migrations against the PostgresMigrator connection string (the
    // owner role) — never the restricted seamline_app role the runtime
    // DbContext above uses. See docs/adr/0006, the REVOKE on trade_history.
    public static async Task MigrateTradingModuleAsync(this IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<TradingDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TradingDbContext.Schema));

        await using var migrationContext = new TradingDbContext(optionsBuilder.Options, new TenantContext());
        await migrationContext.Database.MigrateAsync();
    }

    // Entry point Reporting.Worker calls (ADR-0002, ADR-0015) — the actual
    // logic lives in the internal RemitReportingRunner, same reasoning as
    // MigrateTradingModuleAsync and Risk's RunEndOfDayValuationAsync:
    // Trade/TradeHistory/RemitReport are internal by design, so a separate
    // process reaches them through one public extension method.
    public static Task RunReportingBatchAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<RemitReportingRunner>();
        return new RemitReportingRunner(services, logger).RunAsync(cancellationToken);
    }

    // Called from inside the single AddMassTransit(...) registration in the
    // composition root. Lives here, not in Seamline.Api, because
    // AddEntityFrameworkOutbox<TradingDbContext> and the saga's EF Core
    // repository both need the concrete DbContext type, which is internal
    // to this assembly.
    public static void AddTradingMassTransitConfiguration(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddEntityFrameworkOutbox<TradingDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
        });

        configurator.AddConsumer<TradeApprovalCompletedConsumer>();
        configurator.AddConsumer<TradeApprovalCancelledConsumer>();

        configurator.AddSagaStateMachine<TradeApprovalStateMachine, TradeApprovalState>()
            .EntityFrameworkRepository(r =>
            {
                r.ExistingDbContext<TradingDbContext>();
                r.UsePostgres();
            });
    }
}
