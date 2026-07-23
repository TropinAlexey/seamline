using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Seamline.Modules.Trading.Internal;

public static class TradingModuleExtensions
{
    public static IServiceCollection AddTradingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TradingDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TradingDbContext.Schema)));

        return services;
    }

    public static async Task MigrateTradingModuleAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TradingDbContext>().Database.MigrateAsync();
    }

    // Called from inside the single AddMassTransit(...) registration in the
    // composition root. Lives here, not in Seamline.Api, because
    // AddEntityFrameworkOutbox<TradingDbContext> needs the concrete DbContext
    // type, which is internal to this assembly.
    public static void AddTradingMassTransitConfiguration(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddEntityFrameworkOutbox<TradingDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
        });
    }
}
