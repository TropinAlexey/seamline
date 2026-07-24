using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Seamline.IntegrationTests;

public sealed class SeamlineApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithUsername("seamline")
        .WithPassword("seamline")
        .WithDatabase("seamline")
        .Build();

    // Exposed so tests can assert against tables no module exposes over HTTP
    // (e.g. audit.audit_event) without punching an InternalsVisibleTo hole
    // through the module boundary just for test access.
    public string ConnectionString => _postgres.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var migratorConnectionString = _postgres.GetConnectionString();

        var appConnectionStringBuilder = new NpgsqlConnectionStringBuilder(migratorConnectionString)
        {
            Username = "seamline_app",
            Password = "seamline_app"
        };

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgresMigrator"] = migratorConnectionString,
                ["ConnectionStrings:Postgres"] = appConnectionStringBuilder.ConnectionString
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Mirrors docker/postgres-init/01-create-app-role.sql — Testcontainers
        // doesn't run docker-entrypoint-initdb.d scripts by default, so the
        // restricted runtime role is created explicitly here instead.
        await _postgres.ExecScriptAsync(
            """
            CREATE ROLE seamline_app WITH LOGIN PASSWORD 'seamline_app';
            GRANT CONNECT ON DATABASE seamline TO seamline_app;
            """);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
