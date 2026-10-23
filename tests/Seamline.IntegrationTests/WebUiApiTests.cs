using System.Net;
using System.Net.Http.Json;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Trading.Internal;
using Xunit;

namespace Seamline.IntegrationTests;

// The API surface the web UI reads (ADR-0030): blotter list, history, the
// counterparty list, and the error mapping the UI relies on to tell "your
// request was malformed" (400) from "the trade moved under you" (409).
public sealed class WebUiApiTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Delivering_a_draft_trade_is_a_409_not_a_500()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);
        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-08", "Buy", 10m, 40m, counterparty.Id);

        var response = await client.PostAsync($"/trades/{trade.Id}/deliver", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorDto>();
        Assert.Contains("Deliver requires state Active", error!.Error);
    }

    [Fact]
    public async Task Amending_a_draft_trade_is_a_409()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);
        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-08", "Buy", 10m, 40m, counterparty.Id);

        var response = await client.PostAsJsonAsync($"/trades/{trade.Id}/amend", new { volume = 20m, price = 41m, reason = "n/a" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_trade_for_an_unknown_counterparty_is_a_400()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);

        var response = await client.PostAsJsonAsync("/trades/", new
        {
            commodityCode = "POWER",
            deliveryPeriod = "2027-08",
            direction = "Buy",
            volume = 10m,
            price = 40m,
            counterpartyId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorDto>();
        Assert.Contains("not found", error!.Error);
    }

    [Fact]
    public async Task The_blotter_filters_pages_and_computes_notional_server_side()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);
        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var gas = await CreateTradeAsync(client, "GAS", "2027-09", "Sell", 12.5m, 30.25m, counterparty.Id);
        await CreateTradeAsync(client, "POWER", "2027-09", "Buy", 10m, 40m, counterparty.Id);
        var active = await CreateTradeAsync(client, "POWER", "2027-10", "Buy", 10m, 40m, counterparty.Id);
        (await client.PostAsync($"/trades/{active.Id}/submit", content: null)).EnsureSuccessStatusCode();

        var all = await client.GetFromJsonAsync<TradePageDto>("/trades?pageSize=2&page=1");
        Assert.Equal(3, all!.Total);
        Assert.Equal(2, all.Items.Count);
        Assert.Equal(active.Id, all.Items[0].Id); // most recently changed first

        var drafts = await client.GetFromJsonAsync<TradePageDto>("/trades?state=Draft&commodity=GAS");
        var draft = Assert.Single(drafts!.Items);
        Assert.Equal(gas.Id, draft.Id);
        Assert.Equal(378.125m, draft.Notional);

        var draftOrActive = await client.GetFromJsonAsync<TradePageDto>("/trades?state=Draft&state=Active&period=2027-10");
        Assert.Equal(active.Id, Assert.Single(draftOrActive!.Items).Id);
    }

    [Fact]
    public async Task Another_tenant_sees_an_empty_blotter_and_no_counterparties()
    {
        var clientA = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);
        var counterparty = await CreateCounterpartyAsync(clientA, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(clientA, "POWER", "2027-08", "Buy", 10m, 40m, counterparty.Id);

        var clientB = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);

        var page = await clientB.GetFromJsonAsync<TradePageDto>("/trades");
        Assert.Equal(0, page!.Total);
        Assert.Empty((await clientB.GetFromJsonAsync<List<CounterpartyDto>>("/counterparties"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/trades/{trade.Id}/history")).StatusCode);
    }

    [Fact]
    public async Task Counterparties_are_listed_by_name()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);
        await CreateCounterpartyAsync(client, "Zeta Power", 1m);
        await CreateCounterpartyAsync(client, "Alpha Gas", 2m);

        var counterparties = await client.GetFromJsonAsync<List<CounterpartyDto>>("/counterparties");

        Assert.Equal(["Alpha Gas", "Zeta Power"], counterparties!.Select(c => c.Name));
    }

    [Fact]
    public async Task History_has_one_entry_per_version_starting_at_creation()
    {
        var (client, trader) = await AuthTestHelper.CreateAuthenticatedClientWithLoginAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);
        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-08", "Buy", 10m, 40m, counterparty.Id);
        (await client.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();

        var history = (await client.GetFromJsonAsync<List<HistoryDto>>($"/trades/{trade.Id}/history"))!;

        Assert.Equal([1, 2, 3], history.Select(h => h.Version));
        Assert.Equal(["Draft", "Submitted", "Active"], history.Select(h => h.State));
        Assert.Equal(trader, history[0].ChangedBy);

        var detail = await client.GetFromJsonAsync<TradeDto>($"/trades/{trade.Id}");
        Assert.Equal(history[^1].ValidFrom, detail!.LastChangedAt);
    }

    [Theory]
    [InlineData("approve", "Active")]
    [InlineData("reject", "Rejected")]
    public async Task The_risk_decision_is_recorded_under_the_deciding_users_login(string decision, string expectedState)
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        // A random login, so a hard-coded "risk" can't pass by coincidence.
        var (risk, riskLogin) = await AuthTestHelper.CreateAuthenticatedClientWithLoginAsync(factory, tenantId, IdentityRoles.MiddleOffice);

        var counterparty = await CreateCounterpartyAsync(trader, "Small Cap Co", 1_000m);
        var trade = await CreateTradeAsync(trader, "GAS", "2027-11", "Buy", 1_000m, 50m, counterparty.Id);
        (await trader.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Accepted, (await risk.PostAsync($"/trades/{trade.Id}/{decision}", content: null)).StatusCode);

        // The saga hands the decision back to Trading as a message.
        await factory.Bus.Consumed<TradeApprovalCompletedConsumer, TradeApprovalCompleted>(m => m.TradeId == trade.Id);
        var last = (await trader.GetFromJsonAsync<List<HistoryDto>>($"/trades/{trade.Id}/history"))![^1];
        Assert.Equal(expectedState, last.State);
        Assert.Equal(riskLogin, last.ChangedBy);
    }

    [Fact]
    public async Task Only_middle_office_can_publish_a_curve_point()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var backOffice = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var risk = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);
        var point = new { commodityCode = "POWER", deliveryPeriod = "2027-12", price = 50m };

        Assert.Equal(HttpStatusCode.Forbidden, (await trader.PostAsJsonAsync("/curve-points/", point)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await backOffice.PostAsJsonAsync("/curve-points/", point)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await risk.PostAsJsonAsync("/curve-points/", point)).StatusCode);
    }

    [Fact]
    public async Task Front_office_cannot_approve_and_back_office_cannot_create_trades()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var backOffice = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var counterparty = await CreateCounterpartyAsync(trader, "Acme Energy", 1_000_000m);

        Assert.Equal(HttpStatusCode.Forbidden, (await trader.PostAsync($"/trades/{Guid.NewGuid()}/approve", content: null)).StatusCode);
        var create = await backOffice.PostAsJsonAsync("/trades/", new
        {
            commodityCode = "POWER", deliveryPeriod = "2027-08", direction = "Buy", volume = 1m, price = 1m, counterpartyId = counterparty.Id
        });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData("22222222-2222-2222-2222-222222222222")]
    public async Task Both_demo_tenants_have_the_seeded_demo_users(string tenantId)
    {
        var client = factory.CreateClient();

        foreach (var login in new[] { "trader", "risk", "backoffice" })
        {
            var response = await client.PostAsJsonAsync("/auth/login", new { tenantId = Guid.Parse(tenantId), login, password = "Demo-Password-123!" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
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

    private sealed record ErrorDto(string Error);

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);

    private sealed record TradeIdDto(Guid Id);

    private sealed record TradeDto(Guid Id, string State, decimal Notional, int Version, DateTimeOffset LastChangedAt);

    private sealed record TradePageDto(List<TradeDto> Items, int Total);

    private sealed record HistoryDto(int Version, DateTimeOffset ValidFrom, string ChangedBy, string ChangeReason, string State);
}
