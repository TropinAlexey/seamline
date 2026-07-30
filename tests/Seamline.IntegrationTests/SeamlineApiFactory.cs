using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Seamline.IntegrationTests;

public sealed class SeamlineApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Program.cs reads MessageBroker:Transport synchronously, before
    // builder.Build() — by which point ConfigureWebHost's
    // ConfigureAppConfiguration override hasn't been applied yet (it's
    // wired in at Build() time). An env var, read as part of
    // WebApplication.CreateBuilder(args) itself, is early enough; set it
    // once, statically, before any host in this test run gets created.
    // Process-wide and never unset — safe only because every
    // SeamlineApiFactory in this test run wants the same value.
    static SeamlineApiFactory()
    {
        Environment.SetEnvironmentVariable("MessageBroker__Transport", "InMemory");
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithUsername("seamline")
        .WithPassword("seamline")
        .WithDatabase("seamline")
        .Build();

    // Exposed so tests can assert against tables no module exposes over HTTP
    // (e.g. audit.audit_event) without punching an InternalsVisibleTo hole
    // through the module boundary just for test access.
    public string ConnectionString => _postgres.GetConnectionString();

    // Same restricted role the app itself connects as — for tests that need
    // to prove the RLS policies (ADR-0005 layer 2) hold even for a raw SQL
    // connection that never goes through EF Core's query filter at all.
    public string AppConnectionString => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
    {
        Username = "seamline_app",
        Password = "seamline_app"
    }.ConnectionString;

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
