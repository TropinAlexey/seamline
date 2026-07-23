using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Seamline.Modules.Risk.Internal;

public static class RiskModuleExtensions
{
    public static IServiceCollection AddRiskModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RiskDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", RiskDbContext.Schema)));

        return services;
    }

    public static async Task MigrateRiskModuleAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RiskDbContext>().Database.MigrateAsync();
    }

    public static void AddRiskMassTransitConfiguration(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumer<TradeConfirmedConsumer>();
    }
}
