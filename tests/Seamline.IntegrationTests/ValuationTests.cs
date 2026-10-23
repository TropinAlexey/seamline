using System.Net.Http.Json;
using Npgsql;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Risk.Internal;
using Seamline.Modules.Trading.Contracts;
using Xunit;

namespace Seamline.IntegrationTests;

// Exercises the actual EOD mark-to-market computation (ADR-0014) through
// the same public entry point Valuation.Worker calls
// (RiskModuleExtensions.RunEndOfDayValuationAsync) — against
// SeamlineApiFactory's own DI container, which already wires every module
// Seamline.Api does. Hangfire's scheduling itself was smoke-tested manually
// (see the worker's own startup); what's worth an automated test is the
// computation, not the cron plumbing around it.
public sealed class ValuationTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task An_EOD_run_records_MtM_for_an_open_position_against_the_published_curve_price()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        // POST /curve-points is MO-only (ADR-0030).
        var marketDataClient = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-09", "Buy", 100m, 40m, counterparty.Id);
        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();
        await factory.Bus.Consumed<TradeActivatedConsumer, TradeActivated>(m => m.TradeId == trade.Id);

        var curveResponse = await marketDataClient.PostAsJsonAsync(
            "/curve-points/", new { commodityCode = "POWER", deliveryPeriod = "2027-09", price = 46m });
        curveResponse.EnsureSuccessStatusCode();

        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);

        // The EOD run is awaited directly: nothing asynchronous left to wait for.
        var snapshot = await TryFindValuationSnapshotAsync(tenantId, "POWER", "2027-09");
        Assert.NotNull(snapshot);
        Assert.Equal(100m, snapshot.NetVolume);
        Assert.Equal(40m, snapshot.WeightedAvgPrice);
        Assert.Equal(46m, snapshot.CurvePrice);
        Assert.Equal(600m, snapshot.MtmAmount); // (46 - 40) * 100
    }

    [Fact]
    public async Task An_EOD_run_skips_positions_that_have_netted_flat()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        // POST /curve-points is MO-only (ADR-0030).
        var marketDataClient = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Bravo Trading", 1_000_000m);
        var trade = await CreateTradeAsync(client, "GAS", "2027-10", "Buy", 50m, 20m, counterparty.Id);
        await (await client.PostAsync($"/trades/{trade.Id}/submit", content: null)).Content.ReadAsStringAsync();
        await factory.Bus.Consumed<TradeActivatedConsumer, TradeActivated>(m => m.TradeId == trade.Id);

        var deliverResponse = await client.PostAsync($"/trades/{trade.Id}/deliver", content: null);
        deliverResponse.EnsureSuccessStatusCode();
        await factory.Bus.Consumed<TradeDeliveredConsumer, TradeDelivered>(m => m.TradeId == trade.Id);

        await marketDataClient.PostAsJsonAsync("/curve-points/", new { commodityCode = "GAS", deliveryPeriod = "2027-10", price = 25m });

        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);

        var snapshot = await TryFindValuationSnapshotAsync(tenantId, "GAS", "2027-10");
        Assert.Null(snapshot);
    }

    private static async Task<CounterpartyDto> CreateCounterpartyAsync(HttpClient client, string name, decimal creditLimit)
    {
        var response = await client.PostAsJsonAsync("/counterparties/", new { name, creditLimit });
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

    private async Task<ValuationSnapshotRow?> TryFindValuationSnapshotAsync(Guid tenantId, string commodityCode, string deliveryPeriod)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT net_volume, weighted_avg_price, curve_price, mtm_amount
            FROM risk.valuation_snapshot
            WHERE tenant_id = @tenantId AND commodity_code = @commodityCode AND delivery_period = @deliveryPeriod
            """;
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("commodityCode", commodityCode);
        command.Parameters.AddWithValue("deliveryPeriod", deliveryPeriod);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new ValuationSnapshotRow(reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3));
    }

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);

    private sealed record TradeIdDto(Guid Id);

    private sealed record ValuationSnapshotRow(decimal NetVolume, decimal WeightedAvgPrice, decimal CurvePrice, decimal MtmAmount);
}
