using System.Net.Http.Json;
using Xunit;

namespace Seamline.IntegrationTests;

// Proves the vertical slice end to end: create a counterparty, book a trade,
// confirm it (publishing TradeConfirmed via the transactional outbox), and
// see the position Risk derived from it — across three modules that only
// ever talk through Contracts and the bus, never a direct reference.
public sealed class TradeToPositionTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Confirming_a_trade_updates_the_tenants_position()
    {
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());

        var counterpartyResponse = await client.PostAsJsonAsync("/counterparties/",
            new { name = "Acme Energy", creditLimit = 1_000_000m });
        counterpartyResponse.EnsureSuccessStatusCode();
        var counterparty = await counterpartyResponse.Content.ReadFromJsonAsync<CounterpartyDto>();

        var tradeResponse = await client.PostAsJsonAsync("/trades/", new
        {
            commodityCode = "POWER",
            deliveryPeriod = "2027-03",
            direction = "Buy",
            volume = 100m,
            price = 45.5m,
            counterpartyId = counterparty!.Id
        });
        tradeResponse.EnsureSuccessStatusCode();
        var trade = await tradeResponse.Content.ReadFromJsonAsync<TradeIdDto>();

        var confirmResponse = await client.PostAsync($"/trades/{trade!.Id}/confirm", content: null);
        confirmResponse.EnsureSuccessStatusCode();

        // Outbox dispatch and the Risk consumer run asynchronously; poll
        // briefly rather than assert immediately after confirm returns.
        List<PositionDto>? positions = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            positions = await client.GetFromJsonAsync<List<PositionDto>>("/positions");
            if (positions is { Count: > 0 })
                break;
            await Task.Delay(250);
        }

        var position = Assert.Single(positions!);
        Assert.Equal("POWER", position.CommodityCode);
        Assert.Equal("2027-03", position.DeliveryPeriod);
        Assert.Equal(100m, position.NetVolume);
    }

    [Fact]
    public async Task Tenants_do_not_see_each_others_positions()
    {
        var owner = factory.CreateClient();
        var ownerTenantId = Guid.NewGuid();
        owner.DefaultRequestHeaders.Add("X-Tenant-Id", ownerTenantId.ToString());

        var counterpartyResponse = await owner.PostAsJsonAsync("/counterparties/",
            new { name = "Bravo Trading", creditLimit = 500_000m });
        var counterparty = await counterpartyResponse.Content.ReadFromJsonAsync<CounterpartyDto>();

        var tradeResponse = await owner.PostAsJsonAsync("/trades/", new
        {
            commodityCode = "GAS",
            deliveryPeriod = "2027-04",
            direction = "Sell",
            volume = 50m,
            price = 22m,
            counterpartyId = counterparty!.Id
        });
        var trade = await tradeResponse.Content.ReadFromJsonAsync<TradeIdDto>();
        await owner.PostAsync($"/trades/{trade!.Id}/confirm", content: null);

        var stranger = factory.CreateClient();
        stranger.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var strangerPositions = await stranger.GetFromJsonAsync<List<PositionDto>>("/positions");

        Assert.Empty(strangerPositions!);
    }

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);

    private sealed record TradeIdDto(Guid Id);

    private sealed record PositionDto(string CommodityCode, string DeliveryPeriod, decimal NetVolume);
}
