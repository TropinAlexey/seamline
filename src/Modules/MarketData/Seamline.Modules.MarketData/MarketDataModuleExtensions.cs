using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seamline.Modules.MarketData.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.MarketData.Internal;

public static class MarketDataModuleExtensions
{
    public static IServiceCollection AddMarketDataModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MarketDataDbContext>((sp, options) =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"),
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", MarketDataDbContext.Schema))
                .AddInterceptors(sp.GetRequiredService<TenantSessionVariableInterceptor>()));

        services.AddScoped<ICurvePointDirectory, CurvePointDirectory>();

        return services;
    }

    // Runs migrations against the PostgresMigrator connection string (the
    // owner role) — never the restricted seamline_app role the runtime
    // DbContext above uses. See docs/adr/0005, the RLS policy on this
    // table.
    public static async Task MigrateMarketDataModuleAsync(this IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<MarketDataDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", MarketDataDbContext.Schema));

        await using var migrationContext = new MarketDataDbContext(optionsBuilder.Options, new TenantContext());
        await migrationContext.Database.MigrateAsync();
    }
}
