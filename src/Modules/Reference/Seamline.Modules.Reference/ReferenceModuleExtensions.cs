using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seamline.Modules.Reference.Contracts;

namespace Seamline.Modules.Reference.Internal;

public static class ReferenceModuleExtensions
{
    public static IServiceCollection AddReferenceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ReferenceDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ReferenceDbContext.Schema)));

        services.AddScoped<ICounterpartyDirectory, CounterpartyDirectory>();

        return services;
    }

    // ponytail: migrate-on-startup, not a separate deploy step — fine for a
    // demo project's single environment; a real deployment would run
    // migrations as their own pipeline step before swapping traffic.
    public static async Task MigrateReferenceModuleAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReferenceDbContext>().Database.MigrateAsync();
    }
}
