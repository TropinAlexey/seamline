using System.Net.Http.Json;
using Npgsql;
using Seamline.Modules.Identity.Contracts;
using Xunit;

namespace Seamline.IntegrationTests;

// Proves the Audit module's actual wiring: trade lifecycle events cross the
// module boundary via the bus and land as rows in audit.audit_event with a
// real actor — not just that the app boots with the module registered.
public sealed class AuditEventTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Activating_a_trade_records_an_audit_event_with_the_real_actor()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-05", "Buy", 10m, 45.5m, counterparty.Id);

        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();

        var row = await PollForAuditEventAsync(tenantId, trade.Id, "TradeActivated");

        Assert.Equal("system", row.Actor);
        Assert.Equal("TradeActivated", row.Action);
        Assert.Equal("Trade", row.EntityType);
    }

    [Fact]
    public async Task Rejecting_a_credit_pending_trade_records_a_TradeRejected_audit_event()
    {
        var tenantId = Guid.NewGuid();
        var foClient = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var moClient = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);

        var counterparty = await CreateCounterpartyAsync(foClient, 1_000m);
        var trade = await CreateTradeAsync(foClient, "POWER", "2027-06", "Buy", 1_000m, 50m, counterparty.Id);

        var submitResponse = await foClient.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();

        var rejectResponse = await moClient.PostAsync($"/trades/{trade.Id}/reject", content: null);
        rejectResponse.EnsureSuccessStatusCode();

        var row = await PollForAuditEventAsync(tenantId, trade.Id, "TradeRejected");

        Assert.Equal("system", row.Actor);
        Assert.Equal("TradeRejected", row.Action);
        Assert.Equal("Trade", row.EntityType);
    }

    [Fact]
    public async Task Amending_an_active_trade_records_a_TradeAmended_audit_event()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, 1_000_000m);
        var trade = await CreateTradeAsync(client, "GAS", "2027-07", "Sell", 200m, 30m, counterparty.Id);

        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();

        var amendResponse = await client.PostAsJsonAsync(
            $"/trades/{trade.Id}/amend", new { volume = 250m, price = 31m, reason = "Volume correction" });
        amendResponse.EnsureSuccessStatusCode();

        var row = await PollForAuditEventAsync(tenantId, trade.Id, "TradeAmended");

        Assert.Equal("trader", row.Actor);
        Assert.Equal("TradeAmended", row.Action);
        Assert.Equal("Trade", row.EntityType);
    }

    private async Task<AuditRow> PollForAuditEventAsync(Guid tenantId, Guid tradeId, string expectedAction)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();

        for (var attempt = 0; attempt < 40; attempt++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT actor, action, entity_type FROM audit.audit_event WHERE tenant_id = @tenantId AND entity_id = @tradeId AND action = @action";
            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("tradeId", tradeId);
            command.Parameters.AddWithValue("action", expectedAction);

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return new AuditRow(reader.GetString(0), reader.GetString(1), reader.GetString(2));

            await Task.Delay(250);
        }

        Assert.Fail($"No audit.audit_event row with action '{expectedAction}' appeared for trade {tradeId} within the poll window.");
        return default!;
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
        var response = await client.PostAsJsonAsync("/trades/", new
        {
            commodityCode,
            deliveryPeriod,
            direction,
            volume,
            price,
            counterpartyId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TradeIdDto>())!;
    }

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);

    private sealed record TradeIdDto(Guid Id);

    private sealed record AuditRow(string Actor, string Action, string EntityType);
}
