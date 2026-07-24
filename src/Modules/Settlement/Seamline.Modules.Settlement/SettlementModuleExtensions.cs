using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seamline.SharedKernel;

namespace Seamline.Modules.Settlement.Internal;

public static class SettlementModuleExtensions
{
    public static IServiceCollection AddSettlementModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SettlementDbContext>((sp, options) =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"),
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", SettlementDbContext.Schema))
                .AddInterceptors(sp.GetRequiredService<TenantSessionVariableInterceptor>()));

        return services;
    }

    // Runs migrations against the PostgresMigrator connection string (the
    // owner role) — never the restricted seamline_app role the runtime
    // DbContext above uses. See docs/adr/0005, the RLS policy on this
    // table.
    public static async Task MigrateSettlementModuleAsync(this IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<SettlementDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", SettlementDbContext.Schema));

        await using var migrationContext = new SettlementDbContext(optionsBuilder.Options, new TenantContext());
        await migrationContext.Database.MigrateAsync();
    }

    // Settlement never publishes — it's a pure sink for TradeDelivered, same
    // shape as Audit's consumers (ADR-0010).
    public static void AddSettlementMassTransitConfiguration(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumer<TradeDeliveredConsumer>();
    }
}
