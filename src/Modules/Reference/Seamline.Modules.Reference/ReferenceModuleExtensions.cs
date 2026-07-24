using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seamline.Modules.Reference.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Reference.Internal;

public static class ReferenceModuleExtensions
{
    public static IServiceCollection AddReferenceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ReferenceDbContext>((sp, options) =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"),
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ReferenceDbContext.Schema))
                .AddInterceptors(sp.GetRequiredService<TenantSessionVariableInterceptor>()));

        services.AddScoped<ICounterpartyDirectory, CounterpartyDirectory>();

        return services;
    }

    // migrate-on-startup, not a separate deploy step — fine for a
    // demo project's single environment; a real deployment would run
    // migrations as their own pipeline step before swapping traffic.
    //
    // Runs against PostgresMigrator (the owner role), never the restricted
    // seamline_app role the runtime DbContext above uses.
    public static async Task MigrateReferenceModuleAsync(this IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<ReferenceDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ReferenceDbContext.Schema));

        await using var migrationContext = new ReferenceDbContext(optionsBuilder.Options, new TenantContext());
        await migrationContext.Database.MigrateAsync();
    }
}
