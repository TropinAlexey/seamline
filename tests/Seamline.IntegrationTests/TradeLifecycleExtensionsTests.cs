using System.Net.Http.Json;
using Seamline.Modules.Identity.Contracts;
using Xunit;

namespace Seamline.IntegrationTests;

// Cancelled/Amended/Delivered on top of the vertical slice in
// TradeToPositionTests — the two paths worth exercising end to end are the
// ones with real cross-module wiring: cancelling out of CreditPending goes
// through the saga (same machinery as approve/reject), and amending an
// active trade has to adjust Risk's position by a delta, not recompute it.
public sealed class TradeLifecycleExtensionsTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Cancelling_a_credit_pending_trade_releases_the_reservation_and_never_creates_a_position()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Small Cap Co", 1_000m);
        var trade = await CreateTradeAsync(client, "GAS", "2027-04", "Buy", 1_000m, 50m, counterparty.Id); // notional 50,000 > limit 1,000

        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<SubmitResultDto>();
        Assert.Equal("CreditPending", submitResult!.State);

        var cancelResponse = await client.PostAsync($"/trades/{trade.Id}/cancel", content: null);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, cancelResponse.StatusCode);

        var cancelled = await PollForTradeStateAsync(client, trade.Id, "Cancelled");
        Assert.Equal("Cancelled", cancelled.State);

        var positions = await client.GetFromJsonAsync<List<PositionDto>>("/positions");
        Assert.Empty(positions!);
    }

    [Fact]
    public async Task Rejecting_a_credit_pending_trade_moves_it_to_rejected_and_never_creates_a_position()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Small Cap Co", 1_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-05", "Buy", 1_000m, 50m, counterparty.Id);

        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<SubmitResultDto>();
        Assert.Equal("CreditPending", submitResult!.State);

        var riskClient = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);
        var rejectResponse = await riskClient.PostAsync($"/trades/{trade.Id}/reject", content: null);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, rejectResponse.StatusCode);

        var rejected = await PollForTradeStateAsync(client, trade.Id, "Rejected");
        Assert.Equal("Rejected", rejected.State);

        var positions = await client.GetFromJsonAsync<List<PositionDto>>("/positions");
        Assert.Empty(positions!);
    }

    [Fact]
    public async Task Cancelling_a_draft_trade_succeeds_inline_without_the_saga()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "GAS", "2027-05", "Sell", 200m, 30m, counterparty.Id);

        var cancelResponse = await client.PostAsync($"/trades/{trade.Id}/cancel", content: null);
        cancelResponse.EnsureSuccessStatusCode();

        var cancelled = await client.GetFromJsonAsync<TradeStateDto>($"/trades/{trade.Id}");
        Assert.Equal("Cancelled", cancelled!.State);
    }

    [Fact]
    public async Task Amending_an_active_trade_adjusts_the_position_by_the_delta()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-06", "Buy", 100m, 45.5m, counterparty.Id);

        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();

        await PollForSinglePositionAsync(client, expectedVolume: 100m);

        var amendResponse = await client.PostAsJsonAsync(
            $"/trades/{trade.Id}/amend", new { volume = 150m, price = 46m, reason = "Volume correction" });
        amendResponse.EnsureSuccessStatusCode();

        var position = await PollForSinglePositionAsync(client, expectedVolume: 150m);
        Assert.Equal("POWER", position.CommodityCode);
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

    private static async Task<TradeStateDto> PollForTradeStateAsync(HttpClient client, Guid tradeId, string expectedState)
    {
        TradeStateDto? trade = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            trade = await client.GetFromJsonAsync<TradeStateDto>($"/trades/{tradeId}");
            if (trade!.State == expectedState)
                return trade;
            await Task.Delay(250);
        }

        return trade!;
    }

    private static async Task<PositionDto> PollForSinglePositionAsync(HttpClient client, decimal expectedVolume)
    {
        List<PositionDto>? positions = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            positions = await client.GetFromJsonAsync<List<PositionDto>>("/positions");
            if (positions is { Count: 1 } && positions[0].NetVolume == expectedVolume)
                break;
            await Task.Delay(250);
        }

        return Assert.Single(positions!);
    }

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);

    private sealed record TradeIdDto(Guid Id);

    private sealed record SubmitResultDto(Guid Id, string State, string Outcome);

    private sealed record TradeStateDto(Guid Id, string State, decimal Volume, decimal Price, int Version);

    private sealed record PositionDto(string CommodityCode, string DeliveryPeriod, decimal NetVolume);
}
