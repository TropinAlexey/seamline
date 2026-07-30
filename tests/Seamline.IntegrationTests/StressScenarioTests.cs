using System.Net.Http.Json;
using Npgsql;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Risk.Internal;
using Xunit;

namespace Seamline.IntegrationTests;

// Exercises the stress-scenario computation (ADR-0016) through the same
// RunEndOfDayValuationAsync entry point ValuationTests already exercises —
// stress rows are written in the same pass as the real valuation_snapshot,
// reusing the same Position/curve price, not a separate code path.
public sealed class StressScenarioTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task An_EOD_run_writes_four_stress_rows_for_an_open_position()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2028-02", "Buy", 100m, 40m, counterparty.Id);
        (await client.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();
        await PollForSinglePositionAsync(client);

        (await client.PostAsJsonAsync("/curve-points/", new { commodityCode = "POWER", deliveryPeriod = "2028-02", price = 40m }))
            .EnsureSuccessStatusCode();

        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);

        var rows = await PollForStressRowsAsync(tenantId, "POWER", "2028-02", expectedCount: 4);

        // Flat ±10%: shocked price 44 / 36, MtM = (price - 40) * 100.
        AssertRow(rows, "FlatShock", 10m, 44m, 400m);
        AssertRow(rows, "FlatShock", -10m, 36m, -400m);

        // Single-commodity ±25%: shocked price 50 / 30.
        AssertRow(rows, "SingleCommodityShock", 25m, 50m, 1_000m);
        AssertRow(rows, "SingleCommodityShock", -25m, 30m, -1_000m);
    }

    [Fact]
    public async Task An_EOD_run_skips_stress_rows_for_a_flat_position()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Bravo Trading", 1_000_000m);
        var trade = await CreateTradeAsync(client, "GAS", "2028-03", "Buy", 50m, 20m, counterparty.Id);
        (await client.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();
        await PollForSinglePositionAsync(client);

        (await client.PostAsync($"/trades/{trade.Id}/deliver", content: null)).EnsureSuccessStatusCode();
        await PollForFlatPositionAsync(client, "GAS", "2028-03");

        await client.PostAsJsonAsync("/curve-points/", new { commodityCode = "GAS", deliveryPeriod = "2028-03", price = 25m });

        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);

        Assert.Empty(await FetchStressRowsAsync(tenantId, "GAS", "2028-03"));
    }

    private static void AssertRow(List<StressRow> rows, string scenarioType, decimal shockPercentage, decimal shockedPrice, decimal mtmAmount)
    {
        var row = rows.SingleOrDefault(r => r.ScenarioType == scenarioType && r.ShockPercentage == shockPercentage);
        Assert.True(row is not null, $"No {scenarioType} row at {shockPercentage}% found among: {string.Join(", ", rows)}");
        Assert.Equal(shockedPrice, row!.ShockedPrice);
        Assert.Equal(mtmAmount, row.MtmAmount);
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

    private async Task<List<StressRow>> PollForStressRowsAsync(Guid tenantId, string commodityCode, string deliveryPeriod, int expectedCount)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var rows = await FetchStressRowsAsync(tenantId, commodityCode, deliveryPeriod);
            if (rows.Count >= expectedCount)
                return rows;
            await Task.Delay(250);
        }

        Assert.Fail($"Fewer than {expectedCount} stress_scenario_result rows appeared for {commodityCode}/{deliveryPeriod} within the poll window.");
        return default!; // unreachable — Assert.Fail throws
    }

    private async Task<List<StressRow>> FetchStressRowsAsync(Guid tenantId, string commodityCode, string deliveryPeriod)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT scenario_type, shock_percentage, shocked_price, mtm_amount
            FROM risk.stress_scenario_result
            WHERE tenant_id = @tenantId AND commodity_code = @commodityCode AND delivery_period = @deliveryPeriod
            """;
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("commodityCode", commodityCode);
        command.Parameters.AddWithValue("deliveryPeriod", deliveryPeriod);

        var rows = new List<StressRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new StressRow(reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3)));

        return rows;
    }

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);

    private sealed record TradeIdDto(Guid Id);

    private sealed record PositionDto(string CommodityCode, string DeliveryPeriod, decimal NetVolume);

    private sealed record StressRow(string ScenarioType, decimal ShockPercentage, decimal ShockedPrice, decimal MtmAmount);
}
