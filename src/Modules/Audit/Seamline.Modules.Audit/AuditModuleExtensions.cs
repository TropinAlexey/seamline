using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seamline.SharedKernel;

namespace Seamline.Modules.Audit.Internal;

public static class AuditModuleExtensions
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuditDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.Schema)));

        return services;
    }

    // Runs migrations against the PostgresMigrator connection string (the
    // owner role) — never the restricted seamline_app role the runtime
    // DbContext above uses. See docs/adr/0006, the REVOKE on trade_history;
    // audit_event gets the same append-only grant.
    public static async Task MigrateAuditModuleAsync(this IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.Schema));

        await using var migrationContext = new AuditDbContext(optionsBuilder.Options, new TenantContext());
        await migrationContext.Database.MigrateAsync();
    }

    public static void AddAuditMassTransitConfiguration(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumer<TradeActivatedAuditConsumer>();
        configurator.AddConsumer<TradeRejectedAuditConsumer>();
        configurator.AddConsumer<TradeAmendedAuditConsumer>();
    }
}
