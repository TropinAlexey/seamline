using System.Net.Http.Json;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Trading.Contracts;
using Risk = Seamline.Modules.Risk.Internal;
using Settlement = Seamline.Modules.Settlement.Internal;
using Xunit;

namespace Seamline.IntegrationTests;

// Two cross-module reads/writes added when MarketData and Settlement gained
// their first entity: Risk's /positions synchronously reads a curve point
// from MarketData (no event, no polling needed — just DI), and Trading's
// /deliver publishes TradeDelivered, which Settlement consumes to create an
// invoice.
public sealed class MarketDataAndSettlementTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task A_position_carries_the_published_curve_price()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        // POST /curve-points is MO-only (ADR-0030).
        var marketDataClient = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-07", "Buy", 100m, 45.5m, counterparty.Id);
        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();

        await factory.Bus.Consumed<Risk.TradeActivatedConsumer, TradeActivated>(m => m.TradeId == trade.Id);

        var curveResponse = await marketDataClient.PostAsJsonAsync(
            "/curve-points/", new { commodityCode = "POWER", deliveryPeriod = "2027-07", price = 48.25m });
        curveResponse.EnsureSuccessStatusCode();

        // The mark price is a synchronous read through ICurvePointDirectory:
        // nothing to wait for once the position exists.
        var position = Assert.Single(await client.GetPositionsAsync<PositionDto>());
        Assert.Equal(48.25m, position.MarkPrice);
    }

    [Fact]
    public async Task Delivering_an_active_trade_creates_an_invoice_for_its_notional()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "GAS", "2027-08", "Buy", 100m, 22.5m, counterparty.Id);
        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();

        var deliverResponse = await client.PostAsync($"/trades/{trade.Id}/deliver", content: null);
        deliverResponse.EnsureSuccessStatusCode();

        // GET /invoices is BO's endpoint (ADR-0013) — a separate role from
        // the FO client that booked and delivered the trade.
        var backOfficeClient = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        await factory.Bus.Consumed<Settlement.TradeDeliveredConsumer, TradeDelivered>(m => m.TradeId == trade.Id);
        var invoices = await backOfficeClient.GetFromJsonAsync<List<InvoiceDto>>("/invoices");
        var invoice = Assert.Single(invoices!, i => i.TradeId == trade.Id);
        Assert.Equal(2_250m, invoice.Amount); // 100 * 22.5
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

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);

    private sealed record TradeIdDto(Guid Id);

    private sealed record PositionDto(string CommodityCode, string DeliveryPeriod, decimal NetVolume, decimal? MarkPrice);

    private sealed record InvoiceDto(Guid TradeId, Guid CounterpartyId, decimal Amount, DateTimeOffset IssuedAt);
}
