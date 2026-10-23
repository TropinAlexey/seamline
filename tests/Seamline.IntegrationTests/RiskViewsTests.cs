using System.Net;
using System.Net.Http.Json;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Risk.Internal;
using Seamline.Modules.Trading.Contracts;
using Audit = Seamline.Modules.Audit.Internal;
using Xunit;

namespace Seamline.IntegrationTests;

// The risk read models behind the web UI (ADR-0031): live MtM with
// server-side totals, EOD runs identified by one run id, counterparty
// exposure computed exactly as the credit check computes it — and the
// reservation lifecycle fixes that make that exposure truthful.
public sealed class RiskViewsTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    // No curve point is ever published this far out, so exposure is the
    // full notional and easy to assert exactly.
    private const string NoCurvePeriod = "2031-11";

    [Fact]
    public async Task Delivering_a_trade_releases_its_credit_exposure()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var counterparty = await CreateCounterpartyAsync(trader, 1_000_000m);
        var trade = await CreateTradeAsync(trader, "POWER", NoCurvePeriod, 100m, 50m, counterparty.Id);
        (await trader.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();

        Assert.Equal(5_000m, (await GetExposureAsync(trader, counterparty.Id)).CurrentExposure);

        (await trader.PostAsync($"/trades/{trade.Id}/deliver", content: null)).EnsureSuccessStatusCode();

        await factory.Bus.Consumed<TradeDeliveredConsumer, TradeDelivered>(m => m.TradeId == trade.Id);
        var exposure = await GetExposureAsync(trader, counterparty.Id);
        Assert.Equal(0m, exposure.CurrentExposure);
        Assert.Equal(1_000_000m, exposure.CreditLimit);
    }

    [Fact]
    public async Task Amending_a_trade_moves_its_reservation_to_the_new_terms()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var counterparty = await CreateCounterpartyAsync(trader, 1_000_000m);
        var trade = await CreateTradeAsync(trader, "GAS", NoCurvePeriod, 100m, 50m, counterparty.Id);
        (await trader.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();

        (await trader.PostAsJsonAsync($"/trades/{trade.Id}/amend", new { volume = 200m, price = 50m, reason = "Volume correction" }))
            .EnsureSuccessStatusCode();

        await factory.Bus.Consumed<TradeAmendedConsumer, TradeAmended>(m => m.TradeId == trade.Id);
        Assert.Equal(10_000m, (await GetExposureAsync(trader, counterparty.Id)).CurrentExposure);
    }

    [Fact]
    public async Task Exposure_is_for_front_and_middle_office_only()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var backOffice = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var counterparty = await CreateCounterpartyAsync(trader, 1_000m);

        Assert.Equal(HttpStatusCode.Forbidden, (await backOffice.GetAsync($"/counterparties/{counterparty.Id}/exposure")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await trader.GetAsync($"/counterparties/{Guid.NewGuid()}/exposure")).StatusCode);
    }

    [Fact]
    public async Task Live_positions_carry_server_computed_mtm_and_a_total()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var risk = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);
        var counterparty = await CreateCounterpartyAsync(trader, 10_000_000m);
        var trade = await CreateTradeAsync(trader, "POWER", "2031-10", 100m, 40m, counterparty.Id);
        (await trader.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();
        (await risk.PostAsJsonAsync("/curve-points/", new { commodityCode = "POWER", deliveryPeriod = "2031-10", price = 46m }))
            .EnsureSuccessStatusCode();

        await factory.Bus.Consumed<TradeActivatedConsumer, TradeActivated>(m => m.TradeId == trade.Id);
        var positions = (await trader.GetFromJsonAsync<PositionsDto>("/positions"))!;

        var position = Assert.Single(positions.Items);
        Assert.Equal(40m, position.WeightedAvgPrice);
        Assert.Equal(46m, position.MarkPrice);
        Assert.Equal(600m, position.MtmAmount); // (46 − 40) × 100
        Assert.Equal(600m, positions.TotalMtm);
    }

    [Fact]
    public async Task Latest_valuation_and_stress_results_come_from_one_run()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var risk = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);
        var counterparty = await CreateCounterpartyAsync(trader, 10_000_000m);
        var trade = await CreateTradeAsync(trader, "GAS", "2031-09", 100m, 40m, counterparty.Id);
        (await trader.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();
        (await risk.PostAsJsonAsync("/curve-points/", new { commodityCode = "GAS", deliveryPeriod = "2031-09", price = 46m }))
            .EnsureSuccessStatusCode();
        await factory.Bus.Consumed<TradeActivatedConsumer, TradeActivated>(m => m.TradeId == trade.Id);

        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);
        var first = await risk.GetFromJsonAsync<ValuationRunDto>("/valuations/latest");
        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);

        var valuation = await risk.GetFromJsonAsync<ValuationRunDto>("/valuations/latest");
        var stress = await risk.GetFromJsonAsync<StressRunDto>("/stress-results/latest");

        Assert.NotEqual(first!.RunId, valuation!.RunId);
        Assert.Single(valuation.Items); // only the newest run's rows
        Assert.Equal(600m, valuation.TotalMtm);

        Assert.Equal(valuation.RunId, stress!.RunId);
        Assert.Equal(600m, stress.BaseTotalMtm);
        Assert.Equal(4, stress.Scenarios.Count);
        var flatUp = Assert.Single(stress.Scenarios, s => s.ScenarioType == "FlatShock" && s.ShockPercentage == 10m);
        Assert.Equal(1_060m, flatUp.TotalMtm); // (50.6 − 40) × 100
    }

    [Fact]
    public async Task Stress_results_are_hidden_from_front_office()
    {
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);
        Assert.Equal(HttpStatusCode.Forbidden, (await trader.GetAsync("/stress-results/latest")).StatusCode);
    }

    [Fact]
    public async Task The_audit_trail_covers_the_whole_lifecycle_and_stays_in_its_tenant()
    {
        var tenantId = Guid.NewGuid();
        var (trader, traderLogin) = await AuthTestHelper.CreateAuthenticatedClientWithLoginAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var backOffice = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var counterparty = await CreateCounterpartyAsync(trader, 1_000_000m);

        var delivered = await CreateTradeAsync(trader, "POWER", NoCurvePeriod, 10m, 50m, counterparty.Id);
        (await trader.PostAsync($"/trades/{delivered.Id}/submit", content: null)).EnsureSuccessStatusCode();
        (await trader.PostAsync($"/trades/{delivered.Id}/deliver", content: null)).EnsureSuccessStatusCode();

        var cancelled = await CreateTradeAsync(trader, "POWER", NoCurvePeriod, 10m, 50m, counterparty.Id);
        (await trader.PostAsync($"/trades/{cancelled.Id}/cancel", content: null)).EnsureSuccessStatusCode();

        await factory.Bus.Consumed<Audit.TradeCreatedAuditConsumer, TradeCreated>(m => m.TradeId == delivered.Id);
        await factory.Bus.Consumed<Audit.TradeSubmittedAuditConsumer, TradeSubmitted>(m => m.TradeId == delivered.Id);
        await factory.Bus.Consumed<Audit.TradeActivatedAuditConsumer, TradeActivated>(m => m.TradeId == delivered.Id);
        await factory.Bus.Consumed<Audit.TradeDeliveredAuditConsumer, TradeDelivered>(m => m.TradeId == delivered.Id);
        var deliveredTrail = await GetAuditAsync(backOffice, delivered.Id);
        // OccurredAt is when Audit consumed the event, and consumers run
        // concurrently — compare as a set, not a sequence.
        Assert.Equal(
            new[] { "TradeActivated", "TradeCreated", "TradeDelivered", "TradeSubmitted" },
            deliveredTrail.Items.Select(e => e.Action).Order());
        Assert.Equal(traderLogin, deliveredTrail.Items.Single(e => e.Action == "TradeDelivered").Actor);

        await factory.Bus.Consumed<Audit.TradeCreatedAuditConsumer, TradeCreated>(m => m.TradeId == cancelled.Id);
        await factory.Bus.Consumed<Audit.TradeCancelledAuditConsumer, TradeCancelled>(m => m.TradeId == cancelled.Id);
        var cancelledTrail = await GetAuditAsync(backOffice, cancelled.Id);
        Assert.Contains(cancelledTrail.Items, e => e.Action == "TradeCancelled" && e.Actor == traderLogin);

        Assert.Equal(HttpStatusCode.Forbidden, (await trader.GetAsync("/audit-events")).StatusCode);
        var otherTenant = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.MiddleOffice);
        Assert.Equal(0, (await otherTenant.GetFromJsonAsync<AuditPageDto>($"/audit-events?entityId={delivered.Id}"))!.Total);
    }

    private static async Task<ExposureDto> GetExposureAsync(HttpClient client, Guid counterpartyId)
        => (await client.GetFromJsonAsync<ExposureDto>($"/counterparties/{counterpartyId}/exposure"))!;

    private static async Task<AuditPageDto> GetAuditAsync(HttpClient client, Guid entityId)
        => (await client.GetFromJsonAsync<AuditPageDto>($"/audit-events?entityId={entityId}"))!;

    private static async Task<CounterpartyDto> CreateCounterpartyAsync(HttpClient client, decimal creditLimit)
    {
        var response = await client.PostAsJsonAsync("/counterparties/", new { name = "Acme Energy", creditLimit });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CounterpartyDto>())!;
    }

    private static async Task<TradeIdDto> CreateTradeAsync(
        HttpClient client, string commodityCode, string deliveryPeriod, decimal volume, decimal price, Guid counterpartyId)
    {
        var response = await client.PostAsJsonAsync("/trades/", new
        {
            commodityCode, deliveryPeriod, direction = "Buy", volume, price, counterpartyId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TradeIdDto>())!;
    }

    private sealed record CounterpartyDto(Guid Id);

    private sealed record TradeIdDto(Guid Id);

    private sealed record ExposureDto(Guid CounterpartyId, decimal CreditLimit, decimal CurrentExposure);

    private sealed record PositionDto(string CommodityCode, string DeliveryPeriod, decimal NetVolume, decimal WeightedAvgPrice, decimal? MarkPrice, decimal? MtmAmount);

    private sealed record PositionsDto(List<PositionDto> Items, decimal? TotalMtm);

    private sealed record ValuationRunDto(Guid? RunId, DateTimeOffset? RunAt, List<object> Items, decimal TotalMtm);

    private sealed record StressGroupDto(string ScenarioType, decimal ShockPercentage, decimal TotalMtm);

    private sealed record StressRunDto(Guid? RunId, decimal BaseTotalMtm, List<StressGroupDto> Scenarios);

    private sealed record AuditEntryDto(DateTimeOffset OccurredAt, string Actor, string Action);

    private sealed record AuditPageDto(List<AuditEntryDto> Items, int Total);
}
