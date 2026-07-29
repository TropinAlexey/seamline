using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Seamline.Modules.Risk.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

public static class RiskModuleExtensions
{
    public static IServiceCollection AddRiskModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RiskDbContext>((sp, options) =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"),
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", RiskDbContext.Schema))
                .AddInterceptors(sp.GetRequiredService<TenantSessionVariableInterceptor>()));

        services.AddScoped<ICreditReservationService, CreditReservationService>();

        return services;
    }

    // Runs migrations against the PostgresMigrator connection string (the
    // owner role) — never the restricted seamline_app role the runtime
    // DbContext above uses. See docs/adr/0006, the REVOKE on trade_history.
    public static async Task MigrateRiskModuleAsync(this IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<RiskDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", RiskDbContext.Schema));

        await using var migrationContext = new RiskDbContext(optionsBuilder.Options, new TenantContext());
        await migrationContext.Database.MigrateAsync();
    }

    public static void AddRiskMassTransitConfiguration(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumer<TradeActivatedConsumer>();
        configurator.AddConsumer<TradeAmendedConsumer>();
        configurator.AddConsumer<TradeDeliveredConsumer>();
    }

    // Entry point Valuation.Worker calls (ADR-0002, ADR-0014) — the actual
    // logic lives in the internal EndOfDayValuationRunner, same reasoning
    // as MigrateRiskModuleAsync above: Position/RiskDbContext/
    // ValuationSnapshot are internal by design, so a separate process
    // reaches them through one public extension method, not by referencing
    // internals directly.
    public static Task RunEndOfDayValuationAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<EndOfDayValuationRunner>();
        return new EndOfDayValuationRunner(services, logger).RunAsync(cancellationToken);
    }
}
