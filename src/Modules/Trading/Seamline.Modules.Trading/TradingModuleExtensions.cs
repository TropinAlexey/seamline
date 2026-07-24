using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
