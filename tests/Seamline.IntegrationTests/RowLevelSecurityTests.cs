using System.Net.Http.Json;
using Npgsql;
using Xunit;

namespace Seamline.IntegrationTests;

// ADR-0005 layer 2: RLS is meant to hold even when nothing goes through EF
// Core's query filter at all — a raw SQL connection as the restricted
// seamline_app role is exactly the "forgotten filter or raw SQL statement"
// scenario the ADR calls out. TradeToPositionTests.Tenants_do_not_see_each_others_positions
// proves isolation through the app's normal path (layer 1); this proves the
// database itself refuses to cross tenants even without layer 1 involved.
public sealed class RowLevelSecurityTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task A_raw_query_as_the_app_role_sees_only_the_tenant_set_in_the_session_variable()
    {
        var client = factory.CreateClient();
        var ownerTenantId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", ownerTenantId.ToString());

        var response = await client.PostAsJsonAsync("/counterparties/", new { name = "Acme Energy", creditLimit = 1_000_000m });
        response.EnsureSuccessStatusCode();

        // A fresh NpgsqlConnection on the same connection string can still
        // hand back a physical connection the app's own EF Core pool
        // previously used — Npgsql pools by connection string, not by
        // logical owner — so every assertion here sets its own session
        // variable explicitly rather than relying on "nothing set yet".
        await using var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();

        await using (var setWrongTenant = connection.CreateCommand())
        {
            setWrongTenant.CommandText = $"SET app.tenant_id = '{Guid.NewGuid():D}';";
            await setWrongTenant.ExecuteNonQueryAsync();
        }
        Assert.Equal(0, await CountCounterpartiesAsync(connection));

        await using (var setOwnerTenant = connection.CreateCommand())
        {
            setOwnerTenant.CommandText = $"SET app.tenant_id = '{ownerTenantId:D}';";
            await setOwnerTenant.ExecuteNonQueryAsync();
        }
        Assert.Equal(1, await CountCounterpartiesAsync(connection));
    }

    [Fact]
    public async Task A_raw_insert_as_the_app_role_with_a_mismatched_tenant_id_is_rejected()
    {
        factory.CreateClient(); // forces host startup (and migrations) — this test never calls the app otherwise

        await using var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();

        await using var setSessionTenant = connection.CreateCommand();
        setSessionTenant.CommandText = $"SET app.tenant_id = '{Guid.NewGuid():D}';";
        await setSessionTenant.ExecuteNonQueryAsync();

        // Row's tenant_id deliberately does not match the session variable —
        // this is what the Audit consumer bug (see ADR-0005) looked like
        // from the database's side before it was fixed.
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO reference.counterparty ("Id", tenant_id, name, credit_limit)
            VALUES (gen_random_uuid(), gen_random_uuid(), 'Mismatched Tenant Co', 1000);
            """;

        var exception = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal("42501", exception.SqlState);
    }

    private static async Task<long> CountCounterpartiesAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM reference.counterparty;";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
