using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    // Only Valuation.Worker needs these — Seamline.Api never runs curve
    // import. Each ICurveSource is registered under a config-friendly
    // string key so CurveImportRunner can resolve the one
    // MarketData:CurveImport:Sources:{commodity} names, defaulting to
    // Synthetic (ADR-0018). AddStandardResilienceHandler() bundles retry/
    // timeout/circuit-breaker against ENTSO-E/EIA's real production
    // flakiness — same reasoning as Trading's AddTradingReportingClient
    // (ADR-0015).
    public static IServiceCollection AddMarketDataCurveImportSources(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddKeyedScoped<ICurveSource, SyntheticCurveSource>("Synthetic");

        services.AddHttpClient<EntsoECurveSource>(client =>
        {
            client.BaseAddress = new Uri("https://web-api.tp.entsoe.eu/api");
        }).AddStandardResilienceHandler();
        services.AddKeyedScoped<ICurveSource>("EntsoE", (sp, _) => sp.GetRequiredService<EntsoECurveSource>());

        services.AddHttpClient<EiaCurveSource>(client =>
        {
            client.BaseAddress = new Uri("https://api.eia.gov/v2/");
        }).AddStandardResilienceHandler();
        services.AddKeyedScoped<ICurveSource>("Eia", (sp, _) => sp.GetRequiredService<EiaCurveSource>());

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

    // Entry point Valuation.Worker calls (ADR-0018) — the actual logic
    // lives in the internal CurveImportRunner, same reasoning as
    // MigrateMarketDataModuleAsync above.
    public static Task RunCurveImportAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<CurveImportRunner>();
        return new CurveImportRunner(services, logger).RunAsync(cancellationToken);
    }
}
