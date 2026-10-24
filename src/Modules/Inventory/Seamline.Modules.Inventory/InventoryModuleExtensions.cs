using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seamline.Modules.Inventory.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Inventory.Internal;

public static class InventoryModuleExtensions
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<InventoryDbContext>((sp, options) =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"),
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", InventoryDbContext.Schema))
                .AddInterceptors(sp.GetRequiredService<TenantSessionVariableInterceptor>()));

        services.AddScoped<IInventoryDirectory, InventoryDirectory>();

        return services;
    }

    // Runs migrations against the PostgresMigrator connection string (the
    // owner role) — never the restricted seamline_app role the runtime
    // DbContext above uses. See docs/adr/0006, the grants on the ledger.
    public static async Task MigrateInventoryModuleAsync(this IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", InventoryDbContext.Schema));

        await using var migrationContext = new InventoryDbContext(optionsBuilder.Options, new TenantContext());
        await migrationContext.MigrateWithLockAsync();
    }
}
