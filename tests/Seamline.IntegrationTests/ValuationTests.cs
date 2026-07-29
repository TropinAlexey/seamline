using System.Net.Http.Json;
using Npgsql;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Risk.Internal;
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

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-09", "Buy", 100m, 40m, counterparty.Id);
        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();
        await PollForSinglePositionAsync(client);

        var curveResponse = await client.PostAsJsonAsync(
            "/curve-points/", new { commodityCode = "POWER", deliveryPeriod = "2027-09", price = 46m });
        curveResponse.EnsureSuccessStatusCode();

        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);

        var snapshot = await PollForValuationSnapshotAsync(tenantId, "POWER", "2027-09");
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

        var counterparty = await CreateCounterpartyAsync(client, "Bravo Trading", 1_000_000m);
        var trade = await CreateTradeAsync(client, "GAS", "2027-10", "Buy", 50m, 20m, counterparty.Id);
        await (await client.PostAsync($"/trades/{trade.Id}/submit", content: null)).Content.ReadAsStringAsync();
        await PollForSinglePositionAsync(client);

        var deliverResponse = await client.PostAsync($"/trades/{trade.Id}/deliver", content: null);
        deliverResponse.EnsureSuccessStatusCode();
        await PollForFlatPositionAsync(client, "GAS", "2027-10");

        await client.PostAsJsonAsync("/curve-points/", new { commodityCode = "GAS", deliveryPeriod = "2027-10", price = 25m });

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

    private static async Task PollForSinglePositionAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var positions = await client.GetFromJsonAsync<List<PositionDto>>("/positions");
            if (positions is { Count: 1 })
                return;
            await Task.Delay(250);
        }

        Assert.Fail("No position appeared within the poll window.");
    }

    // Delivering a trade doesn't remove its Position row — GET /positions
    // returns it with NetVolume back at zero (Risk's own TradeDeliveredConsumer
    // closes it out, see ADR-0014), not an empty list.
    private static async Task PollForFlatPositionAsync(HttpClient client, string commodityCode, string deliveryPeriod)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var positions = await client.GetFromJsonAsync<List<PositionDto>>("/positions");
            var match = positions?.FirstOrDefault(p => p.CommodityCode == commodityCode && p.DeliveryPeriod == deliveryPeriod);
            if (match is { NetVolume: 0m })
                return;
            await Task.Delay(250);
        }

        Assert.Fail($"Position for {commodityCode}/{deliveryPeriod} never nettled back to zero within the poll window.");
    }

    // The valuation run itself is synchronous (awaited directly, not
    // published over the bus), but Deliver's TradeDelivered dispatch and
    // this test's own PollForEmptyPositionsAsync above already establish
    // eventual consistency for the position; a short poll here just
    // absorbs any residual timing slack from the outbox.
    private async Task<ValuationSnapshotRow> PollForValuationSnapshotAsync(Guid tenantId, string commodityCode, string deliveryPeriod)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var snapshot = await TryFindValuationSnapshotAsync(tenantId, commodityCode, deliveryPeriod);
            if (snapshot is not null)
                return snapshot;
            await Task.Delay(250);
        }

        Assert.Fail($"No valuation_snapshot row appeared for {commodityCode}/{deliveryPeriod} within the poll window.");
        return default!; // unreachable — Assert.Fail throws
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

    private sealed record PositionDto(string CommodityCode, string DeliveryPeriod, decimal NetVolume);

    private sealed record ValuationSnapshotRow(decimal NetVolume, decimal WeightedAvgPrice, decimal CurvePrice, decimal MtmAmount);
}
