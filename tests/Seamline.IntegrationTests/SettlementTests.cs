using System.Net;
using System.Net.Http.Json;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Settlement.Internal;
using Seamline.Modules.Trading.Contracts;
using Xunit;

namespace Seamline.IntegrationTests;

// Settlement-specific tests. The happy path (deliver → invoice) lives in
// MarketDataAndSettlementTests; these cover the gaps: tenant isolation on
// the /invoices endpoint and role authorization (only BackOffice sees invoices).
public sealed class SettlementTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Invoices_endpoint_returns_403_for_FrontOffice()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);

        var response = await client.GetAsync("/invoices");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invoices_endpoint_returns_403_for_MiddleOffice()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.MiddleOffice);

        var response = await client.GetAsync("/invoices");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invoices_are_tenant_isolated()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var foA = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantA, IdentityRoles.FrontOffice);
        var foB = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantB, IdentityRoles.FrontOffice);

        var tradeA = await DeliverTradeAsync(foA, "POWER", "2027-09", "Buy", 100m, 50m);
        var tradeB = await DeliverTradeAsync(foB, "GAS", "2027-09", "Sell", 200m, 30m);
        await factory.Bus.Consumed<TradeDeliveredConsumer, TradeDelivered>(m => m.TradeId == tradeA);
        await factory.Bus.Consumed<TradeDeliveredConsumer, TradeDelivered>(m => m.TradeId == tradeB);

        var boA = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantA, IdentityRoles.BackOffice);
        var boB = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantB, IdentityRoles.BackOffice);

        var invoicesA = (await boA.GetFromJsonAsync<List<InvoiceDto>>("/invoices"))!;
        var invoicesB = (await boB.GetFromJsonAsync<List<InvoiceDto>>("/invoices"))!;

        Assert.Single(invoicesA);
        Assert.Equal(5_000m, invoicesA[0].Amount); // 100 × 50

        Assert.Single(invoicesB);
        Assert.Equal(6_000m, invoicesB[0].Amount); // 200 × 30
    }

    private static async Task<Guid> DeliverTradeAsync(
        HttpClient client, string commodityCode, string deliveryPeriod, string direction, decimal volume, decimal price)
    {
        var counterparty = await CreateCounterpartyAsync(client, 1_000_000m);
        var trade = await CreateTradeAsync(client, commodityCode, deliveryPeriod, direction, volume, price, counterparty.Id);

        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();

        var deliverResponse = await client.PostAsync($"/trades/{trade.Id}/deliver", content: null);
        deliverResponse.EnsureSuccessStatusCode();

        return trade.Id;
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

    private sealed record InvoiceDto(Guid TradeId, Guid CounterpartyId, decimal Amount, DateTimeOffset IssuedAt);
}
