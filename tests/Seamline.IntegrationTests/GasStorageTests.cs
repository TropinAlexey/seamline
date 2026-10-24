using System.Net;
using System.Net.Http.Json;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Risk.Internal;
using Xunit;

namespace Seamline.IntegrationTests;

// Gas storage (ADR-0036, ADR-0040, spec 02): contracts, the movement ledger
// with its WACOG, the rate and capacity limits, and the EOD valuation of
// stock. The main test is spec 02's worked example.
public sealed class GasStorageTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    private static readonly DateOnly Day1 = new(2027, 4, 1);

    [Fact]
    public async Task The_worked_example_from_injection_to_unrealized_pnl()
    {
        var tenantId = Guid.NewGuid();
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var risk = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);
        var contractId = await RegisterAsync(ops);

        for (var d = 0; d < 20; d++)
            await InjectAsync(ops, contractId, Day1.AddDays(d), 2_000m, 28m);
        for (var d = 20; d < 30; d++)
            await InjectAsync(ops, contractId, Day1.AddDays(d), 2_000m, 31m);
        StorageDto contract = null!;
        for (var d = 30; d < 35; d++)
            contract = await WithdrawAsync(ops, contractId, Day1.AddDays(d), 3_000m);

        Assert.Equal((45_000m, 29m, 45m), (contract.Quantity, contract.Wacog, contract.FillPct));
        Assert.Equal(1_305_000.00m, contract.StockCost);

        var ledger = (await ops.GetFromJsonAsync<List<MovementDto>>($"/storage-contracts/{contractId}/movements"))!;
        Assert.Equal(35, ledger.Count);
        var lastWithdrawal = ledger[^1];
        Assert.Equal(("Withdrawal", 29m, 45_000m), (lastWithdrawal.Kind, lastWithdrawal.UnitCost, lastWithdrawal.QuantityAfter));

        // EOD against the GAS curve of the run's month at 33.0000:
        // (33 − 29) × 45,000 = 180,000.00.
        await PublishCurrentMonthGasAsync(risk, 33m);
        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);
        var valuation = (await risk.GetFromJsonAsync<InventoryValuationRunDto>("/inventory/valuations/latest"))!;
        Assert.NotNull(valuation.RunId);
        var row = Assert.Single(valuation.Items);
        Assert.Equal((45_000m, 29m, 33m, 180_000.00m), (row.Quantity, row.Wacog, row.CurvePrice, row.UnrealizedAmount));
        Assert.Equal(180_000.00m, valuation.TotalUnrealized);
    }

    [Fact]
    public async Task Limits_return_409_and_change_nothing()
    {
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.BackOffice);
        var contractId = await RegisterAsync(ops);
        await InjectAsync(ops, contractId, Day1, 2_000m, 28m);

        var aboveRate = await ops.PostAsJsonAsync($"/storage-contracts/{contractId}/injections",
            new { gasDay = Day1.AddDays(1), quantity = 2_500m, unitCost = 28m });
        var aboveStock = await ops.PostAsJsonAsync($"/storage-contracts/{contractId}/withdrawals",
            new { gasDay = Day1.AddDays(2), quantity = 2_500m });
        var outsideContract = await ops.PostAsJsonAsync($"/storage-contracts/{contractId}/injections",
            new { gasDay = new DateOnly(2028, 4, 1), quantity = 100m, unitCost = 28m });

        Assert.Equal(HttpStatusCode.Conflict, aboveRate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, aboveStock.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, outsideContract.StatusCode);
        var contract = (await ops.GetFromJsonAsync<List<StorageDto>>("/storage-contracts"))!.Single(c => c.Id == contractId);
        Assert.Equal((2_000m, 2), (contract.Quantity, contract.Version)); // one movement since registration
    }

    // The daily-rate check reads the day's movements and then writes one.
    // Two injections at the same moment must not both pass against each
    // other's absence: the contract row is locked for the transaction.
    [Fact]
    public async Task Two_injections_at_the_same_moment_cannot_both_exceed_the_daily_rate()
    {
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.BackOffice);
        var contractId = await RegisterAsync(ops);

        var first = ops.PostAsJsonAsync($"/storage-contracts/{contractId}/injections", new { gasDay = Day1, quantity = 1_500m, unitCost = 28m });
        var second = ops.PostAsJsonAsync($"/storage-contracts/{contractId}/injections", new { gasDay = Day1, quantity = 1_500m, unitCost = 30m });
        await Task.WhenAll(first, second);

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
            new[] { (await first).StatusCode, (await second).StatusCode }.Order());
        var contract = (await ops.GetFromJsonAsync<List<StorageDto>>("/storage-contracts"))!.Single(c => c.Id == contractId);
        Assert.Equal(1_500m, contract.Quantity);
    }

    [Fact]
    public async Task Only_back_office_moves_gas_and_only_a_flow_commodity_in_MWh_is_stored()
    {
        var tenantId = Guid.NewGuid();
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var trader = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var contractId = await RegisterAsync(ops);

        Assert.Equal(HttpStatusCode.Forbidden, (await trader.PostAsJsonAsync("/storage-contracts/", Contract("GAS"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await trader.PostAsJsonAsync($"/storage-contracts/{contractId}/injections",
            new { gasDay = Day1, quantity = 100m, unitCost = 28m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await trader.GetAsync("/inventory/valuations/latest")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ops.PostAsJsonAsync("/storage-contracts/", Contract("COAL"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ops.PostAsJsonAsync($"/storage-contracts/{Guid.NewGuid()}/injections",
            new { gasDay = Day1, quantity = 100m, unitCost = 28m })).StatusCode);

        // Everyone reads stock; other tenants see none of it.
        Assert.Single((await trader.GetFromJsonAsync<List<StorageDto>>("/storage-contracts"))!);
        var stranger = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.BackOffice);
        Assert.Empty((await stranger.GetFromJsonAsync<List<StorageDto>>("/storage-contracts"))!);
    }

    [Fact]
    public async Task Without_a_curve_point_for_the_month_the_stock_is_not_valued()
    {
        var tenantId = Guid.NewGuid();
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var contractId = await RegisterAsync(ops);
        await InjectAsync(ops, contractId, Day1, 1_000m, 28m);

        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);

        var valuation = (await ops.GetFromJsonAsync<InventoryValuationRunDto>("/inventory/valuations/latest"))!;
        Assert.Empty(valuation.Items);
    }

    private static object Contract(string commodityCode) => new
    {
        facility = "Demo Storage North",
        commodityCode,
        capacity = 100_000m,
        maxInjectionPerDay = 2_000m,
        maxWithdrawalPerDay = 3_000m,
        validFrom = "2027-04-01",
        validTo = "2028-03-31"
    };

    private static async Task<Guid> RegisterAsync(HttpClient ops)
    {
        var response = await ops.PostAsJsonAsync("/storage-contracts/", Contract("GAS"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StorageDto>())!.Id;
    }

    private static async Task<StorageDto> InjectAsync(HttpClient ops, Guid contractId, DateOnly gasDay, decimal quantity, decimal unitCost)
    {
        var response = await ops.PostAsJsonAsync($"/storage-contracts/{contractId}/injections", new { gasDay, quantity, unitCost });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StorageDto>())!;
    }

    private static async Task<StorageDto> WithdrawAsync(HttpClient ops, Guid contractId, DateOnly gasDay, decimal quantity)
    {
        var response = await ops.PostAsJsonAsync($"/storage-contracts/{contractId}/withdrawals", new { gasDay, quantity });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StorageDto>())!;
    }

    private static async Task PublishCurrentMonthGasAsync(HttpClient risk, decimal price) =>
        (await risk.PostAsJsonAsync("/curve-points/", new { commodityCode = "GAS", deliveryPeriod = $"{DateTimeOffset.UtcNow:yyyy-MM}", price }))
            .EnsureSuccessStatusCode();

    private sealed record StorageDto(Guid Id, decimal Quantity, decimal Wacog, decimal FillPct, decimal StockCost, int Version);

    private sealed record MovementDto(string Kind, decimal Quantity, decimal UnitCost, decimal QuantityAfter, decimal WacogAfter);

    private sealed record InventoryValuationRunDto(Guid? RunId, List<InventoryValuationDto> Items, decimal TotalUnrealized);

    private sealed record InventoryValuationDto(decimal Quantity, decimal Wacog, decimal CurvePrice, decimal UnrealizedAmount);
}
