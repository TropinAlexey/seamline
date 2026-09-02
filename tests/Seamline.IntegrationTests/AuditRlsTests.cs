using System.Net.Http.Json;
using Npgsql;
using Seamline.Modules.Identity.Contracts;
using Xunit;

namespace Seamline.IntegrationTests;

// ADR-0005 layer 2 for audit: proves RLS on audit.audit_event isolates
// tenants at the database level, not just through EF Core's query filter.
// Complements RowLevelSecurityTests (which covers reference.counterparty).
public sealed class AuditRlsTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Audit_events_are_invisible_to_other_tenants_via_raw_sql()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var clientA = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantA, IdentityRoles.FrontOffice);

        var cp = await CreateCounterpartyAsync(clientA, 1_000_000m);
        var trade = await CreateTradeAsync(clientA, "GAS", "2028-03", "Buy", 10m, 30m, cp.Id);
        (await clientA.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();

        await PollForAuditRowAsync(tenantA, trade.Id);

        await using var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();

        // Tenant B sees nothing
        await SetTenantAsync(connection, tenantB);
        Assert.Equal(0, await CountAuditEventsAsync(connection, trade.Id));

        // Tenant A sees the row
        await SetTenantAsync(connection, tenantA);
        Assert.True(await CountAuditEventsAsync(connection, trade.Id) > 0);
    }

    [Fact]
    public async Task Inserting_an_audit_event_with_mismatched_tenant_is_rejected()
    {
        factory.CreateClient();

        await using var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();

        var sessionTenant = Guid.NewGuid();
        await SetTenantAsync(connection, sessionTenant);

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO audit.audit_event ("Id", tenant_id, occurred_at, actor, action, entity_type, entity_id, context)
            VALUES (gen_random_uuid(), gen_random_uuid(), now(), 'attacker', 'Fake', 'Trade', gen_random_uuid(), '{}');
            """;

        var ex = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState);
    }

    private async Task PollForAuditRowAsync(Guid tenantId, Guid tradeId)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();

        for (var i = 0; i < 40; i++)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM audit.audit_event WHERE tenant_id = @t AND entity_id = @e";
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("e", tradeId);
            if ((long)(await cmd.ExecuteScalarAsync())! > 0) return;
            await Task.Delay(250);
        }

        Assert.Fail("Audit event did not appear within the poll window.");
    }

    private static async Task SetTenantAsync(NpgsqlConnection connection, Guid tenantId)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SET app.tenant_id = '{tenantId:D}';";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAuditEventsAsync(NpgsqlConnection connection, Guid entityId)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM audit.audit_event WHERE entity_id = @e";
        cmd.Parameters.AddWithValue("e", entityId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<CounterpartyDto> CreateCounterpartyAsync(HttpClient client, decimal creditLimit)
    {
        var response = await client.PostAsJsonAsync("/counterparties/", new { name = "Acme Energy", creditLimit });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CounterpartyDto>())!;
    }

    private static async Task<TradeIdDto> CreateTradeAsync(
        HttpClient client, string commodityCode, string deliveryPeriod, string direction, decimal volume, decimal price, Guid counterpartyId)
    {
        var response = await client.PostAsJsonAsync("/trades/", new { commodityCode, deliveryPeriod, direction, volume, price, counterpartyId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TradeIdDto>())!;
    }

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);
    private sealed record TradeIdDto(Guid Id);
}
