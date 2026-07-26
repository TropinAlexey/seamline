using System.Net.Http.Json;
using Seamline.Modules.Identity.Contracts;
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
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-07", "Buy", 100m, 45.5m, counterparty.Id);
        var submitResponse = await client.PostAsync($"/trades/{trade.Id}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();

        await PollForSinglePositionAsync(client);

        var curveResponse = await client.PostAsJsonAsync(
            "/curve-points/", new { commodityCode = "POWER", deliveryPeriod = "2027-07", price = 48.25m });
        curveResponse.EnsureSuccessStatusCode();

        var position = await PollForMarkPriceAsync(client);
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
        var invoice = await PollForInvoiceAsync(backOfficeClient, trade.Id);
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

    private static async Task PollForSinglePositionAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var positions = await client.GetFromJsonAsync<List<PositionDto>>("/positions");
            if (positions is { Count: 1 })
                return;
            await Task.Delay(250);
        }
    }

    private static async Task<PositionDto> PollForMarkPriceAsync(HttpClient client)
    {
        List<PositionDto>? positions = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            positions = await client.GetFromJsonAsync<List<PositionDto>>("/positions");
            if (positions is { Count: 1 } && positions[0].MarkPrice is not null)
                break;
            await Task.Delay(250);
        }

        return Assert.Single(positions!);
    }

    private static async Task<InvoiceDto> PollForInvoiceAsync(HttpClient client, Guid tradeId)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var invoices = await client.GetFromJsonAsync<List<InvoiceDto>>("/invoices");
            var match = invoices?.FirstOrDefault(i => i.TradeId == tradeId);
            if (match is not null)
                return match;
            await Task.Delay(250);
        }

        Assert.Fail($"No invoice appeared for trade {tradeId} within the poll window.");
        return default!; // unreachable — Assert.Fail throws
    }

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);

    private sealed record TradeIdDto(Guid Id);

    private sealed record PositionDto(string CommodityCode, string DeliveryPeriod, decimal NetVolume, decimal? MarkPrice);

    private sealed record InvoiceDto(Guid TradeId, Guid CounterpartyId, decimal Amount, DateTimeOffset IssuedAt);
}
