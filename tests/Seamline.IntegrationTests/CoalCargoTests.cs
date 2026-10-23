using System.Net;
using System.Net.Http.Json;
using Seamline.Modules.Identity.Contracts;
using Xunit;

namespace Seamline.IntegrationTests;

// Coal cargo (ADR-0034, ADR-0037, spec 01): nomination, loading as FOB
// delivery within the contract tolerance, quality certificate, and the
// provisional/final invoices. The main test is spec 01's worked example,
// to the cent.
public sealed class CoalCargoTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    private static readonly object WorkedExampleTerms = new { tolerancePct = 10m, laycanFrom = "2027-03-10", laycanTo = "2027-03-20" };

    [Fact]
    public async Task The_worked_example_from_nomination_to_final_invoice()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var tradeId = await BookActiveCoalTradeAsync(trader);
        await PollAsync(() => trader.GetPositionsAsync<PositionDto>(), p => p.Any(x => x.CommodityCode == "COAL" && x.NetVolume == 75_000m));

        (await ops.PostAsJsonAsync($"/shipments/{tradeId}/nominate", new { vesselName = "MV Demo Carrier", imo = "9876543", eta = "2027-03-12" }))
            .EnsureSuccessStatusCode();
        var loaded = await ops.PostAsJsonAsync($"/shipments/{tradeId}/load", new { blQuantity = 78_500m, blDate = "2027-03-14" });
        Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);
        var shipment = (await loaded.Content.ReadFromJsonAsync<ShipmentDto>())!;
        Assert.Equal(("Loaded", 67_500m, 82_500m), (shipment.State, shipment.ToleranceMin, shipment.ToleranceMax));

        // Loading is delivery: the trade keeps its contract volume and
        // records the loaded one next to it.
        var trade = (await trader.GetFromJsonAsync<TradeDto>($"/trades/{tradeId}"))!;
        Assert.Equal(("Delivered", 75_000m, 78_500m), (trade.State, trade.Volume, trade.ActualVolume));

        var provisional = Assert.Single(await PollInvoicesAsync(ops, tradeId, 1));
        Assert.Equal(("Provisional", 78_500m, 110m, 8_635_000.00m), (provisional.Kind, provisional.Quantity, provisional.UnitPrice, provisional.Amount));

        var certified = await ops.PostAsJsonAsync($"/shipments/{tradeId}/certify",
            new { measuredNarKcalKg = 5_920.0m, sulphurPct = 0.85m, ashPct = 14.20m });
        Assert.Equal("QualityCertified", (await certified.Content.ReadFromJsonAsync<ShipmentDto>())!.State);

        var final = (await PollInvoicesAsync(ops, tradeId, 2)).Single(i => i.Kind == "Final");
        Assert.Equal(108.5333m, final.UnitPrice);         // 110 × 5,920 / 6,000, rounded to 4 dp
        Assert.Equal(8_519_864.05m, final.Amount);        // 78,500 × 108.5333
        Assert.Equal(-115_135.95m, final.Balance);        // owed back to the buyer

        // The position opened on 75,000 t is closed on 75,000 t, not on the
        // loaded 78,500 t.
        var positions = await PollAsync(() => trader.GetPositionsAsync<PositionDto>(), p => p.All(x => x.CommodityCode != "COAL"));
        Assert.DoesNotContain(positions, p => p.CommodityCode == "COAL");
    }

    [Fact]
    public async Task A_cargo_trade_cannot_be_delivered_by_hand()
    {
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);
        var tradeId = await BookActiveCoalTradeAsync(trader);

        var response = await trader.PostAsync($"/trades/{tradeId}/deliver", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_bill_of_lading_outside_the_tolerance_is_a_409_and_changes_nothing()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var tradeId = await BookActiveCoalTradeAsync(trader);
        await NominateAsync(ops, tradeId);

        var response = await ops.PostAsJsonAsync($"/shipments/{tradeId}/load", new { blQuantity = 83_000m, blDate = "2027-03-14" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Amend the contract quantity first", (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error);
        Assert.Equal("Nominated", (await ops.GetFromJsonAsync<ShipmentDto>($"/shipments/{tradeId}"))!.State);
        Assert.Equal("Active", (await trader.GetFromJsonAsync<TradeDto>($"/trades/{tradeId}"))!.State);
    }

    [Fact]
    public async Task A_certificate_outside_the_limits_rejects_the_cargo_and_issues_no_final_invoice()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var tradeId = await BookActiveCoalTradeAsync(trader);
        await NominateAsync(ops, tradeId);
        (await ops.PostAsJsonAsync($"/shipments/{tradeId}/load", new { blQuantity = 75_000m, blDate = "2027-03-14" })).EnsureSuccessStatusCode();
        await PollInvoicesAsync(ops, tradeId, 1);

        var response = await ops.PostAsJsonAsync($"/shipments/{tradeId}/certify",
            new { measuredNarKcalKg = 5_920.0m, sulphurPct = 1.20m, ashPct = 14.20m });

        Assert.Equal("QualityRejected", (await response.Content.ReadFromJsonAsync<ShipmentDto>())!.State);
        await Task.Delay(1_000);
        var invoice = Assert.Single(await ops.GetFromJsonAsync<List<InvoiceDto>>($"/invoices?tradeId={tradeId}") ?? []);
        Assert.Equal("Provisional", invoice.Kind);
    }

    [Fact]
    public async Task Booking_rules_for_cargo_terms()
    {
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);
        var counterpartyId = await CreateCounterpartyAsync(trader);

        var coalWithoutTerms = await CreateTradeAsync(trader, "COAL", counterpartyId, cargoTerms: null);
        var gasWithTerms = await CreateTradeAsync(trader, "GAS", counterpartyId, WorkedExampleTerms);
        var laycanOutsidePeriod = await CreateTradeAsync(trader, "COAL", counterpartyId,
            new { tolerancePct = 10m, laycanFrom = "2027-03-28", laycanTo = "2027-04-03" });
        var toleranceTooWide = await CreateTradeAsync(trader, "COAL", counterpartyId,
            new { tolerancePct = 25m, laycanFrom = "2027-03-10", laycanTo = "2027-03-20" });

        Assert.Equal(HttpStatusCode.BadRequest, coalWithoutTerms.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, gasWithTerms.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, laycanOutsidePeriod.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toleranceTooWide.StatusCode);
    }

    [Fact]
    public async Task Nomination_rules_and_roles()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var tradeId = await BookActiveCoalTradeAsync(trader);

        var byTrader = await trader.PostAsJsonAsync($"/shipments/{tradeId}/nominate", new { vesselName = "MV X", imo = "9876543", eta = "2027-03-12" });
        var etaOutsideLaycan = await ops.PostAsJsonAsync($"/shipments/{tradeId}/nominate", new { vesselName = "MV X", imo = "9876543", eta = "2027-03-25" });
        var badImo = await ops.PostAsJsonAsync($"/shipments/{tradeId}/nominate", new { vesselName = "MV X", imo = "98765", eta = "2027-03-12" });
        var awaiting = (await ops.GetFromJsonAsync<ShipmentDto>($"/shipments/{tradeId}"))!;
        await NominateAsync(ops, tradeId);
        var twice = await ops.PostAsJsonAsync($"/shipments/{tradeId}/nominate", new { vesselName = "MV Y", imo = "9876543", eta = "2027-03-12" });
        var certifyBeforeLoading = await ops.PostAsJsonAsync($"/shipments/{tradeId}/certify", new { measuredNarKcalKg = 6_000m, sulphurPct = 0.5m, ashPct = 10m });

        Assert.Equal(HttpStatusCode.Forbidden, byTrader.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, etaOutsideLaycan.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badImo.StatusCode);
        Assert.Null(awaiting.State); // awaiting nomination: no shipment row yet
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, certifyBeforeLoading.StatusCode);
    }

    [Fact]
    public async Task Shipment_steps_reach_the_audit_trail()
    {
        var tenantId = Guid.NewGuid();
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var (ops, opsLogin) = await AuthTestHelper.CreateAuthenticatedClientWithLoginAsync(factory, tenantId, IdentityRoles.BackOffice);
        var tradeId = await BookActiveCoalTradeAsync(trader);
        await NominateAsync(ops, tradeId);
        (await ops.PostAsJsonAsync($"/shipments/{tradeId}/load", new { blQuantity = 75_000m, blDate = "2027-03-14" })).EnsureSuccessStatusCode();
        (await ops.PostAsJsonAsync($"/shipments/{tradeId}/certify", new { measuredNarKcalKg = 6_000m, sulphurPct = 0.5m, ashPct = 10m }))
            .EnsureSuccessStatusCode();

        var page = await PollAsync(
            async () => (await ops.GetFromJsonAsync<AuditPageDto>($"/audit-events?entityId={tradeId}&pageSize=50"))!,
            p => p.Items.Any(e => e.Action == "CargoQualityCertified"));

        Assert.Contains(page.Items, e => e.Action == "CargoNominated" && e.Actor == opsLogin);
        Assert.Contains(page.Items, e => e.Action == "TradeDelivered" && e.Actor == opsLogin);
        Assert.Contains(page.Items, e => e.Action == "CargoQualityCertified" && e.Actor == opsLogin);
    }

    private static async Task<Guid> BookActiveCoalTradeAsync(HttpClient trader)
    {
        var counterpartyId = await CreateCounterpartyAsync(trader);
        var response = await CreateTradeAsync(trader, "COAL", counterpartyId, WorkedExampleTerms);
        response.EnsureSuccessStatusCode();
        var tradeId = (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
        (await trader.PostAsync($"/trades/{tradeId}/submit", content: null)).EnsureSuccessStatusCode();
        return tradeId;
    }

    private static async Task NominateAsync(HttpClient ops, Guid tradeId) =>
        (await ops.PostAsJsonAsync($"/shipments/{tradeId}/nominate", new { vesselName = "MV Demo Carrier", imo = "9876543", eta = "2027-03-12" }))
            .EnsureSuccessStatusCode();

    private static async Task<Guid> CreateCounterpartyAsync(HttpClient client)
    {
        // Large enough that the worked example activates within the limit.
        var response = await client.PostAsJsonAsync("/counterparties/", new { name = "Bulk Mining Co", creditLimit = 100_000_000m });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static Task<HttpResponseMessage> CreateTradeAsync(HttpClient client, string commodityCode, Guid counterpartyId, object? cargoTerms) =>
        client.PostAsJsonAsync("/trades/", new
        {
            commodityCode,
            deliveryPeriod = "2027-03",
            direction = "Buy",
            volume = 75_000m,
            price = 110m,
            counterpartyId,
            cargoTerms
        });

    private static Task<List<InvoiceDto>> PollInvoicesAsync(HttpClient ops, Guid tradeId, int count) =>
        PollAsync(async () => (await ops.GetFromJsonAsync<List<InvoiceDto>>($"/invoices?tradeId={tradeId}"))!, i => i.Count == count);

    private static async Task<T> PollAsync<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var value = await read();
        for (var attempt = 0; attempt < 60 && !done(value); attempt++)
        {
            await Task.Delay(250);
            value = await read();
        }
        Assert.True(done(value), "Condition not reached within the poll window.");
        return value;
    }

    private sealed record IdDto(Guid Id);

    private sealed record ErrorDto(string Error);

    private sealed record TradeDto(Guid Id, string State, decimal Volume, decimal? ActualVolume);

    private sealed record ShipmentDto(string? State, decimal ToleranceMin, decimal ToleranceMax, decimal? BlQuantity);

    private sealed record InvoiceDto(string Kind, decimal? Quantity, decimal? UnitPrice, decimal Amount, decimal? Balance);

    private sealed record PositionDto(string CommodityCode, decimal NetVolume);

    private sealed record AuditPageDto(List<AuditEventDto> Items, int Total);

    private sealed record AuditEventDto(string Action, string Actor);
}
