using System.Net;
using System.Net.Http.Json;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Risk.Internal;
using Seamline.Modules.Trading.Contracts;
using Audit = Seamline.Modules.Audit.Internal;
using Settlement = Seamline.Modules.Settlement.Internal;
using Xunit;

namespace Seamline.IntegrationTests;

// Formula pricing (ADR-0035, ADR-0038, spec 03): index fixings, the expected
// price, valuation of the fixed share, and the final price feeding the
// cargo's final invoice. The main test is spec 03's worked example.
public sealed class FormulaPricingTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    private static readonly object CargoTerms = new { tolerancePct = 10m, laycanFrom = "2027-03-10", laycanTo = "2027-03-20" };
    private static readonly object MarchAverage = new { indexCode = "COAL-IDX", windowFrom = "2027-03-01", windowTo = "2027-03-31", premium = 1.5m };

    [Fact]
    public async Task The_worked_example_from_booking_to_final_invoice()
    {
        var (trader, risk, ops) = await ClientsAsync();
        await PublishCurvePointAsync(risk, 106m);
        var tradeId = await BookActiveIndexTradeAsync(trader, MarchAverage);

        // Ten fixings averaging 104.2000.
        var days = WeekdaysOfMarch2027();
        for (var i = 0; i < 10; i++)
            await PublishFixingAsync(risk, days[i], i % 2 == 0 ? 104m : 104.4m);

        var pricing = (await trader.GetFromJsonAsync<PricingDto>($"/trades/{tradeId}/pricing"))!;
        Assert.Equal((23, 10, 104.2m, 106m), (pricing.PricingDays, pricing.FixedDays, pricing.AverageFixing, pricing.Forward));
        Assert.Equal(106.7174m, pricing.ExpectedPrice);
        Assert.Equal((32_608.696m, 42_391.304m), (pricing.FixedQuantity, pricing.UnfixedQuantity));
        Assert.Equal((null, null), (pricing.FormulaFinalPrice, pricing.ConfirmedPrice));

        // Not in the position: valued per trade, against the forward.
        await factory.Bus.Consumed<TradeActivatedConsumer, TradeActivated>(m => m.TradeId == tradeId);
        var exposure = (await risk.GetFromJsonAsync<PricingExposureDto>("/pricing-exposure"))!;
        Assert.Equal(-53_804.35m, exposure.Items.Single(i => i.TradeId == tradeId).MtmAmount);
        Assert.DoesNotContain(await trader.GetPositionsAsync<PositionDto>(), p => p.CommodityCode == "COAL");

        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);
        var valuation = (await risk.GetFromJsonAsync<ValuationRunDto>("/valuations/latest"))!;
        var row = valuation.Items.Single(v => v.TradeId == tradeId);
        Assert.Equal((75_000m, 106.7174m, -53_804.35m), (row.NetVolume, row.WeightedAvgPrice, row.MtmAmount));
        var stress = (await risk.GetFromJsonAsync<StressRunDto>("/stress-results/latest"))!;
        var flatUp = stress.Scenarios.Single(s => s.ScenarioType == "FlatShock" && s.ShockPercentage == 10m);
        // Only the fixed share responds: +345,652.17 exactly, +345,652.18
        // between the two figures each rounded once.
        Assert.Equal(291_847.83m, flatUp.Items.Single(i => i.TradeId == tradeId).MtmAmount);

        // Loading mid-window: provisional at the day's expected price.
        await NominateAndLoadAsync(ops, tradeId);
        await factory.Bus.Consumed<Settlement.TradeDeliveredConsumer, TradeDelivered>(m => m.TradeId == tradeId);
        var provisional = Assert.Single(await InvoicesAsync(ops, tradeId));
        Assert.Equal(("Provisional", 106.7174m, 8_377_315.90m), (provisional.Kind, provisional.UnitPrice, provisional.Amount));

        // The certificate comes in while the window is still open: no final yet.
        await CertifyAsync(ops, tradeId);
        var early = await ops.PostAsync($"/trades/{tradeId}/fix-price", content: null);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Contains("10 of 23", (await early.Content.ReadFromJsonAsync<ErrorDto>())!.Error);

        // The other thirteen days bring the average to 105.0000.
        for (var i = 10; i < 23; i++)
            await PublishFixingAsync(risk, days[i], i < 22 ? 105.6m : 105.8m);
        var fixedResponse = await ops.PostAsync($"/trades/{tradeId}/fix-price", content: null);
        Assert.Equal(HttpStatusCode.OK, fixedResponse.StatusCode);
        Assert.Equal(106.5m, (await fixedResponse.Content.ReadFromJsonAsync<TradeDto>())!.Price);

        var history = (await trader.GetFromJsonAsync<List<HistoryDto>>($"/trades/{tradeId}/history"))!;
        Assert.Equal("Pricing window fixed: average 105.0000 +1.5000 premium = 106.5000", history[^1].ChangeReason);
        Assert.Equal(106.5m, history[^1].Price);

        // TradePriceFixed carried the certificate: the final invoice follows.
        await factory.Bus.Consumed<Settlement.TradePriceFixedConsumer, TradePriceFixed>(m => m.TradeId == tradeId);
        var final = (await InvoicesAsync(ops, tradeId)).Single(i => i.Kind == "Final");
        Assert.Equal(105.08m, final.UnitPrice);          // 106.5000 × 5,920 / 6,000
        Assert.Equal(8_248_780.00m, final.Amount);       // 78,500 × 105.0800
        Assert.Equal(-128_535.90m, final.Balance);

        // Delivered and fixed: no price risk left.
        // (Risk's TradePriceFixed consumer shares the endpoint Settlement's
        // used above, so it has run too.)
        Assert.DoesNotContain((await risk.GetFromJsonAsync<PricingExposureDto>("/pricing-exposure"))!.Items, i => i.TradeId == tradeId);
        await factory.Bus.Consumed<Audit.TradePriceFixedAuditConsumer, TradePriceFixed>(m => m.TradeId == tradeId);
        var audit = (await risk.GetFromJsonAsync<AuditPageDto>($"/audit-events?entityId={tradeId}"))!;
        Assert.Contains(audit.Items, e => e.Action == "TradePriceFixed" && e.Actor != "system");
    }

    [Fact]
    public async Task Fixed_before_loading_the_cargo_is_invoiced_at_the_final_price_and_the_certificate_settles_it()
    {
        var (trader, risk, ops) = await ClientsAsync();
        await PublishCurvePointAsync(risk, 106m);
        var tradeId = await BookActiveIndexTradeAsync(trader,
            new { indexCode = "COAL-IDX", windowFrom = "2027-03-01", windowTo = "2027-03-02", premium = 0m });
        await PublishFixingAsync(risk, new DateOnly(2027, 3, 1), 100m);
        await PublishFixingAsync(risk, new DateOnly(2027, 3, 2), 102m);
        (await ops.PostAsync($"/trades/{tradeId}/fix-price", content: null)).EnsureSuccessStatusCode();

        await NominateAndLoadAsync(ops, tradeId);
        await factory.Bus.Consumed<Settlement.TradeDeliveredConsumer, TradeDelivered>(m => m.TradeId == tradeId);
        var provisional = Assert.Single(await InvoicesAsync(ops, tradeId));
        Assert.Equal(101m, provisional.UnitPrice);

        await CertifyAsync(ops, tradeId);
        await factory.Bus.Consumed<Settlement.CargoQualityCertifiedConsumer, CargoQualityCertified>(m => m.TradeId == tradeId);
        var final = (await InvoicesAsync(ops, tradeId)).Single(i => i.Kind == "Final");
        Assert.Equal(99.6533m, final.UnitPrice);         // 101 × 5,920 / 6,000, to 4 dp
    }

    [Fact]
    public async Task A_sell_index_trade_is_valued_from_the_sellers_side()
    {
        var (trader, risk, _) = await ClientsAsync();
        await PublishCurvePointAsync(risk, 106m);
        var tradeId = await BookActiveIndexTradeAsync(trader, MarchAverage, direction: "Sell");
        var days = WeekdaysOfMarch2027();
        for (var i = 0; i < 10; i++)
            await PublishFixingAsync(risk, days[i], i % 2 == 0 ? 104m : 104.4m);

        await factory.Bus.Consumed<TradeActivatedConsumer, TradeActivated>(m => m.TradeId == tradeId);
        var item = (await risk.GetFromJsonAsync<PricingExposureDto>("/pricing-exposure"))!.Items.Single(i => i.TradeId == tradeId);
        Assert.Equal(53_804.35m, item.MtmAmount);
    }

    // The certificate and the price fixing each read the half the other
    // writes (ADR-0038). Sent at the same moment, the trade row lock makes
    // one of them see the other, so exactly one event carries both halves
    // and the final invoice is issued — every time, not by luck of timing.
    [Fact]
    public async Task A_certificate_and_a_price_fixing_at_the_same_moment_still_settle_the_cargo()
    {
        var (trader, risk, ops) = await ClientsAsync();
        await PublishCurvePointAsync(risk, 106m);
        await PublishFixingAsync(risk, new DateOnly(2027, 3, 1), 100m);
        await PublishFixingAsync(risk, new DateOnly(2027, 3, 2), 102m);

        for (var round = 0; round < 5; round++)
        {
            var tradeId = await BookActiveIndexTradeAsync(trader,
                new { indexCode = "COAL-IDX", windowFrom = "2027-03-01", windowTo = "2027-03-02", premium = 0m });
            await NominateAndLoadAsync(ops, tradeId);

            var certify = ops.PostAsJsonAsync($"/shipments/{tradeId}/certify", new { measuredNarKcalKg = 5_920.0m, sulphurPct = 0.85m, ashPct = 14.20m });
            var fix = ops.PostAsync($"/trades/{tradeId}/fix-price", content: null);
            await Task.WhenAll(certify, fix);
            (await certify).EnsureSuccessStatusCode();
            (await fix).EnsureSuccessStatusCode();

            // Whichever event carried both halves, Settlement turns it into
            // the final invoice; wait for both consumers, then look.
            await factory.Bus.Consumed<Settlement.CargoQualityCertifiedConsumer, CargoQualityCertified>(m => m.TradeId == tradeId);
            await factory.Bus.Consumed<Settlement.TradePriceFixedConsumer, TradePriceFixed>(m => m.TradeId == tradeId);
            var final = Assert.Single(await InvoicesAsync(ops, tradeId), i => i.Kind == "Final");
            Assert.Equal(99.6533m, final.UnitPrice);
        }
    }

    [Fact]
    public async Task Booking_and_submit_rules()
    {
        var (trader, risk, _) = await ClientsAsync();
        var counterpartyId = await CreateCounterpartyAsync(trader);

        // Flow commodities are not index-priced (REMIT, ADR-0035).
        var gas = await trader.PostAsJsonAsync("/trades/", new
        {
            commodityCode = "GAS", deliveryPeriod = "2027-03", direction = "Buy", volume = 100m, counterpartyId,
            indexPricing = MarchAverage
        });
        Assert.Equal(HttpStatusCode.BadRequest, gas.StatusCode);

        var unknownIndex = await CreateIndexTradeAsync(trader, counterpartyId,
            new { indexCode = "API2", windowFrom = "2027-03-01", windowTo = "2027-03-31", premium = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, unknownIndex.StatusCode);

        var both = await trader.PostAsJsonAsync("/trades/", new
        {
            commodityCode = "COAL", deliveryPeriod = "2027-03", direction = "Buy", volume = 75_000m, price = 110m, counterpartyId,
            cargoTerms = CargoTerms, indexPricing = MarchAverage
        });
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);

        // No forward and no fixing: nothing to credit-check it at.
        var created = await CreateIndexTradeAsync(trader, counterpartyId, MarchAverage);
        var tradeId = (await created.Content.ReadFromJsonAsync<IdDto>())!.Id;
        var submit = await trader.PostAsync($"/trades/{tradeId}/submit", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);

        // A fixed-price trade has no pricing view.
        Assert.Equal(HttpStatusCode.NotFound, (await risk.GetAsync($"/trades/{Guid.NewGuid()}/pricing")).StatusCode);
    }

    [Fact]
    public async Task Fixings_are_append_only_weekday_values_published_by_middle_office()
    {
        var (trader, risk, _) = await ClientsAsync();
        var monday = new DateOnly(2027, 3, 1);

        await PublishFixingAsync(risk, monday, 104m);
        var again = await risk.PostAsJsonAsync("/indices/COAL-IDX/fixings", new { fixingDate = monday, value = 105m });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var saturday = await risk.PostAsJsonAsync("/indices/COAL-IDX/fixings", new { fixingDate = new DateOnly(2027, 3, 6), value = 105m });
        Assert.Equal(HttpStatusCode.BadRequest, saturday.StatusCode);

        var byTrader = await trader.PostAsJsonAsync("/indices/COAL-IDX/fixings", new { fixingDate = new DateOnly(2027, 3, 2), value = 105m });
        Assert.Equal(HttpStatusCode.Forbidden, byTrader.StatusCode);

        var unknown = await risk.PostAsJsonAsync("/indices/NOPE/fixings", new { fixingDate = monday, value = 105m });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var fixings = (await trader.GetFromJsonAsync<List<FixingDto>>("/indices/COAL-IDX/fixings?from=2027-03-01&to=2027-03-31"))!;
        Assert.Equal(104m, Assert.Single(fixings).Value);
        Assert.Contains(await trader.GetFromJsonAsync<List<IndexDto>>("/indices") ?? [], i => i.Code == "COAL-IDX" && i.CommodityCode == "COAL");
    }

    [Fact]
    public async Task Only_back_office_fixes_a_price()
    {
        var (trader, risk, _) = await ClientsAsync();
        await PublishCurvePointAsync(risk, 106m);
        var tradeId = await BookActiveIndexTradeAsync(trader, MarchAverage);

        Assert.Equal(HttpStatusCode.Forbidden, (await trader.PostAsync($"/trades/{tradeId}/fix-price", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await risk.PostAsync($"/trades/{tradeId}/fix-price", content: null)).StatusCode);
    }

    private async Task<(HttpClient Trader, HttpClient Risk, HttpClient Ops)> ClientsAsync()
    {
        var tenantId = Guid.NewGuid();
        return (
            await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice),
            await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice),
            await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice));
    }

    private static async Task PublishCurvePointAsync(HttpClient risk, decimal price) =>
        (await risk.PostAsJsonAsync("/curve-points/", new { commodityCode = "COAL", deliveryPeriod = "2027-03", price }))
            .EnsureSuccessStatusCode();

    private static async Task PublishFixingAsync(HttpClient risk, DateOnly date, decimal value) =>
        (await risk.PostAsJsonAsync("/indices/COAL-IDX/fixings", new { fixingDate = date, value })).EnsureSuccessStatusCode();

    private static async Task<Guid> BookActiveIndexTradeAsync(HttpClient trader, object pricing, string direction = "Buy")
    {
        var counterpartyId = await CreateCounterpartyAsync(trader);
        var response = await CreateIndexTradeAsync(trader, counterpartyId, pricing, direction);
        response.EnsureSuccessStatusCode();
        var tradeId = (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
        var submit = await trader.PostAsync($"/trades/{tradeId}/submit", content: null);
        submit.EnsureSuccessStatusCode();
        Assert.Equal("Active", (await submit.Content.ReadFromJsonAsync<SubmitDto>())!.State);
        return tradeId;
    }

    private static Task<HttpResponseMessage> CreateIndexTradeAsync(HttpClient trader, Guid counterpartyId, object pricing, string direction = "Buy") =>
        trader.PostAsJsonAsync("/trades/", new
        {
            commodityCode = "COAL",
            deliveryPeriod = "2027-03",
            direction,
            volume = 75_000m,
            counterpartyId,
            cargoTerms = CargoTerms,
            indexPricing = pricing
        });

    private static async Task NominateAndLoadAsync(HttpClient ops, Guid tradeId)
    {
        (await ops.PostAsJsonAsync($"/shipments/{tradeId}/nominate", new { vesselName = "MV Demo Carrier", imo = "9876543", eta = "2027-03-12" }))
            .EnsureSuccessStatusCode();
        (await ops.PostAsJsonAsync($"/shipments/{tradeId}/load", new { blQuantity = 78_500m, blDate = "2027-03-14" }))
            .EnsureSuccessStatusCode();
    }

    private static async Task CertifyAsync(HttpClient ops, Guid tradeId) =>
        (await ops.PostAsJsonAsync($"/shipments/{tradeId}/certify", new { measuredNarKcalKg = 5_920.0m, sulphurPct = 0.85m, ashPct = 14.20m }))
            .EnsureSuccessStatusCode();

    private static async Task<Guid> CreateCounterpartyAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/counterparties/", new { name = "Bulk Mining Co", creditLimit = 100_000_000m });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static List<DateOnly> WeekdaysOfMarch2027() =>
        Enumerable.Range(1, 31).Select(d => new DateOnly(2027, 3, d))
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();

    private static async Task<List<InvoiceDto>> InvoicesAsync(HttpClient ops, Guid tradeId) =>
        (await ops.GetFromJsonAsync<List<InvoiceDto>>($"/invoices?tradeId={tradeId}"))!;

    private sealed record IdDto(Guid Id);

    private sealed record SubmitDto(string State);

    private sealed record ErrorDto(string Error);

    private sealed record TradeDto(Guid Id, decimal? Price);

    private sealed record HistoryDto(int Version, string ChangeReason, decimal? Price);

    private sealed record PricingDto(
        int PricingDays, int FixedDays, decimal? AverageFixing, decimal? Forward, decimal? ExpectedPrice,
        decimal FixedQuantity, decimal UnfixedQuantity, decimal? FormulaFinalPrice, decimal? ConfirmedPrice);

    private sealed record PricingExposureDto(List<PricingExposureItemDto> Items);

    private sealed record PricingExposureItemDto(Guid TradeId, decimal? MtmAmount);

    private sealed record PositionDto(string CommodityCode, decimal NetVolume);

    private sealed record ValuationRunDto(List<ValuationItemDto> Items);

    private sealed record ValuationItemDto(Guid? TradeId, decimal NetVolume, decimal WeightedAvgPrice, decimal MtmAmount);

    private sealed record StressRunDto(List<StressScenarioDto> Scenarios);

    private sealed record StressScenarioDto(string ScenarioType, decimal ShockPercentage, List<StressItemDto> Items);

    private sealed record StressItemDto(Guid? TradeId, decimal MtmAmount);

    private sealed record InvoiceDto(string Kind, decimal? UnitPrice, decimal Amount, decimal? Balance);

    private sealed record FixingDto(DateOnly FixingDate, decimal Value);

    private sealed record IndexDto(string Code, string CommodityCode);

    private sealed record AuditPageDto(List<AuditEventDto> Items);

    private sealed record AuditEventDto(string Action, string Actor);
}
