using System.Net;
using System.Net.Http.Json;
using Seamline.Modules.Identity.Contracts;
using Xunit;

namespace Seamline.IntegrationTests;

// Proves the vertical slice end to end: create a counterparty, book a trade,
// submit it (publishing TradeActivated via the transactional outbox on the
// within-limit path, or engaging the credit-limit saga on a breach), and see
// the position Risk derived from it — across three modules that only ever
// talk through Contracts and the bus, never a direct reference.
public sealed class TradeToPositionTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Submitting_a_trade_within_credit_limit_updates_the_tenants_position()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-03", "Buy", 100m, 45.5m, counterparty.Id);

        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();

        var position = await PollForSinglePositionAsync(client);
        Assert.Equal("POWER", position.CommodityCode);
        Assert.Equal("2027-03", position.DeliveryPeriod);
        Assert.Equal(100m, position.NetVolume);
    }

    [Fact]
    public async Task Tenants_do_not_see_each_others_positions()
    {
        var owner = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(owner, "Bravo Trading", 500_000m);
        var trade = await CreateTradeAsync(owner, "GAS", "2027-04", "Sell", 50m, 22m, counterparty.Id);
        await owner.PostAsync($"/trades/{trade.Id}/submit", content: null);

        var stranger = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);

        var strangerPositions = await stranger.GetFromJsonAsync<List<PositionDto>>("/positions");

        Assert.Empty(strangerPositions!);
    }

    [Fact]
    public async Task A_trade_that_breaches_the_credit_limit_waits_for_risk_approval_before_updating_the_position()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Small Cap Co", 1_000m);
        var trade = await CreateTradeAsync(client, "GAS", "2027-04", "Buy", 1_000m, 50m, counterparty.Id); // notional 50,000 > limit 1,000

        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<SubmitResultDto>();
        Assert.Equal("CreditPending", submitResult!.State);
        Assert.Equal("Breached", submitResult.Outcome);

        // No position yet — the trade is parked pending approval, not active.
        var positionsBeforeApproval = await client.GetFromJsonAsync<List<PositionDto>>("/positions");
        Assert.Empty(positionsBeforeApproval!);

        // Wrong actor: the trader (FO) who booked the trade is not MO.
        var unauthorizedApproval = await client.PostAsync($"/trades/{trade.Id}/approve", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedApproval.StatusCode);

        var riskClient = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);
        var approveResponse = await riskClient.PostAsync($"/trades/{trade.Id}/approve", content: null);
        approveResponse.EnsureSuccessStatusCode();

        var position = await PollForSinglePositionAsync(client);
        Assert.Equal("GAS", position.CommodityCode);
        Assert.Equal(1_000m, position.NetVolume);
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

    // Outbox dispatch and the saga/consumers run asynchronously; poll briefly
    // rather than assert immediately after the triggering call returns.
    private static async Task<PositionDto> PollForSinglePositionAsync(HttpClient client)
    {
        List<PositionDto>? positions = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            positions = await client.GetFromJsonAsync<List<PositionDto>>("/positions");
            if (positions is { Count: > 0 })
                break;
            await Task.Delay(250);
        }

        return Assert.Single(positions!);
    }

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);

    private sealed record TradeIdDto(Guid Id);

    private sealed record SubmitResultDto(Guid Id, string State, string Outcome);

    private sealed record PositionDto(string CommodityCode, string DeliveryPeriod, decimal NetVolume);
}
