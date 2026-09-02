using System.Net.Http.Json;
using Seamline.Modules.Identity.Contracts;

namespace Seamline.IntegrationTests;

public sealed class CreditConcurrencyTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Concurrent_submissions_against_same_counterparty_do_not_double_breach_the_limit()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        // Limit is 7,000. Each trade's notional exposure (no curve) = 100 * 50 = 5,000.
        // Individually each fits (5,000 < 7,000), but together they breach (10,000 > 7,000).
        // Without serialization both would read zero existing exposure and both get Reserved.
        var counterparty = await CreateCounterpartyAsync(client, "Tight Limit Co", 7_000m);

        var tradeA = await CreateTradeAsync(client, "POWER", "2027-06", "Buy", 100m, 50m, counterparty.Id);
        var tradeB = await CreateTradeAsync(client, "POWER", "2027-07", "Buy", 100m, 50m, counterparty.Id);

        var submitA = client.PostAsync($"/trades/{tradeA.Id}/submit", content: null);
        var submitB = client.PostAsync($"/trades/{tradeB.Id}/submit", content: null);

        var results = await Task.WhenAll(submitA, submitB);
        var outcomeA = await results[0].Content.ReadFromJsonAsync<SubmitResultDto>();
        var outcomeB = await results[1].Content.ReadFromJsonAsync<SubmitResultDto>();

        var outcomes = new[] { outcomeA!.Outcome, outcomeB!.Outcome };

        // Exactly one Reserved, one Breached — whichever goes first wins the lock,
        // the second sees the first's reservation and the sum exceeds the limit.
        Assert.Contains("Reserved", outcomes);
        Assert.Contains("Breached", outcomes);
    }

    [Fact]
    public async Task Concurrent_submissions_against_different_counterparties_proceed_independently()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var cpA = await CreateCounterpartyAsync(client, "Alpha Corp", 1_000_000m);
        var cpB = await CreateCounterpartyAsync(client, "Beta Corp", 1_000_000m);

        var tradeA = await CreateTradeAsync(client, "POWER", "2027-06", "Buy", 10m, 45m, cpA.Id);
        var tradeB = await CreateTradeAsync(client, "GAS", "2027-06", "Sell", 10m, 30m, cpB.Id);

        var submitA = client.PostAsync($"/trades/{tradeA.Id}/submit", content: null);
        var submitB = client.PostAsync($"/trades/{tradeB.Id}/submit", content: null);

        var results = await Task.WhenAll(submitA, submitB);
        var outcomeA = await results[0].Content.ReadFromJsonAsync<SubmitResultDto>();
        var outcomeB = await results[1].Content.ReadFromJsonAsync<SubmitResultDto>();

        Assert.Equal("Reserved", outcomeA!.Outcome);
        Assert.Equal("Reserved", outcomeB!.Outcome);
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
        var response = await client.PostAsJsonAsync("/trades/", new { commodityCode, deliveryPeriod, direction, volume, price, counterpartyId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TradeIdDto>())!;
    }

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);
    private sealed record TradeIdDto(Guid Id);
    private sealed record SubmitResultDto(Guid Id, string State, string Outcome);
}
