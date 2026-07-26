using System.Net.Http.Json;
using Npgsql;
using Seamline.Modules.Identity.Contracts;
using Xunit;

namespace Seamline.IntegrationTests;

// Proves the Audit module's actual wiring: TradeActivated crosses the module
// boundary via the bus and lands as a row in audit.audit_event with a real
// actor — not just that the app boots with the module registered.
public sealed class AuditEventTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Activating_a_trade_records_an_audit_event_with_the_real_actor()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterpartyResponse = await client.PostAsJsonAsync(
            "/counterparties/", new { name = "Acme Energy", creditLimit = 1_000_000m });
        counterpartyResponse.EnsureSuccessStatusCode();
        var counterparty = (await counterpartyResponse.Content.ReadFromJsonAsync<CounterpartyDto>())!;

        var tradeResponse = await client.PostAsJsonAsync("/trades/", new
        {
            commodityCode = "POWER",
            deliveryPeriod = "2027-05",
            direction = "Buy",
            volume = 10m,
            price = 45.5m,
            counterpartyId = counterparty.Id
        });
        tradeResponse.EnsureSuccessStatusCode();
        var trade = (await tradeResponse.Content.ReadFromJsonAsync<TradeIdDto>())!;

        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();

        var row = await PollForAuditEventAsync(tenantId, trade.Id);

        Assert.Equal("system", row.Actor);
        Assert.Equal("TradeActivated", row.Action);
        Assert.Equal("Trade", row.EntityType);
    }

    // The audit consumer runs asynchronously off the bus, same as the
    // position update in TradeToPositionTests — poll rather than assert
    // immediately after submit returns.
    private async Task<AuditRow> PollForAuditEventAsync(Guid tenantId, Guid tradeId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();

        for (var attempt = 0; attempt < 40; attempt++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT actor, action, entity_type FROM audit.audit_event WHERE tenant_id = @tenantId AND entity_id = @tradeId";
            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("tradeId", tradeId);

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return new AuditRow(reader.GetString(0), reader.GetString(1), reader.GetString(2));

            await Task.Delay(250);
        }

        Assert.Fail($"No audit.audit_event row appeared for trade {tradeId} within the poll window.");
        return default!; // unreachable — Assert.Fail throws
    }

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);

    private sealed record TradeIdDto(Guid Id);

    private sealed record AuditRow(string Actor, string Action, string EntityType);
}
