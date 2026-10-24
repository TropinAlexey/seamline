using System.Net;
using System.Net.Http.Json;
using Npgsql;
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

        // Cost of the gas withdrawn: 15,000 × 29.0000 = 435,000.00, each
        // day's 3,000 × 29.0000 = 87,000.00 rounded to cents in the ledger.
        var withdrawals = ledger.Where(m => m.Kind == "Withdrawal").ToList();
        Assert.All(withdrawals, m => Assert.Equal(87_000.00m, m.Cost));
        Assert.Equal(435_000.00m, withdrawals.Sum(m => m.Cost));

        // 46,000 MWh is above the stock, but the daily rate is checked
        // first: the 409 names the rate (spec 02).
        var aboveStockAndRate = await ops.PostAsJsonAsync($"/storage-contracts/{contractId}/withdrawals",
            new { gasDay = Day1.AddDays(35), quantity = 46_000m });
        Assert.Equal(HttpStatusCode.Conflict, aboveStockAndRate.StatusCode);
        Assert.Contains("withdrawal rate", await aboveStockAndRate.Content.ReadAsStringAsync());

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
        Assert.Contains("injection rate", await aboveRate.Content.ReadAsStringAsync());
        // Below the 3,000 MWh/day rate, so the stock is what refuses it.
        Assert.Equal(HttpStatusCode.Conflict, aboveStock.StatusCode);
        Assert.Contains("in storage", await aboveStock.Content.ReadAsStringAsync());
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

    // A run that values nothing for a contract must not leave the previous
    // run's P&L on screen (ADR-0041): the latest valuation is the tenant's
    // latest run, and an emptied storage has no row in it.
    [Fact]
    public async Task After_the_storage_empties_the_latest_valuation_shows_no_stale_pnl()
    {
        var tenantId = Guid.NewGuid();
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var risk = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);
        var contractId = await RegisterAsync(ops);
        await InjectAsync(ops, contractId, Day1, 1_000m, 28m);
        await PublishCurrentMonthGasAsync(risk, 33m);

        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);
        var first = (await risk.GetFromJsonAsync<InventoryValuationRunDto>("/inventory/valuations/latest"))!;
        Assert.Equal(5_000.00m, Assert.Single(first.Items).UnrealizedAmount);

        await WithdrawAsync(ops, contractId, Day1.AddDays(1), 1_000m);
        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);

        var latest = (await risk.GetFromJsonAsync<InventoryValuationRunDto>("/inventory/valuations/latest"))!;
        Assert.NotEqual(first.RunId, latest.RunId);
        Assert.Empty(latest.Items);
        Assert.Equal(0m, latest.TotalUnrealized);
    }

    // The balance on the contract row and the ledger row are written in one
    // transaction (spec 02). A trigger, keyed on one reason so no other test
    // is affected, makes the ledger insert fail; the balance must not move.
    [Fact]
    public async Task A_movement_whose_ledger_row_fails_leaves_the_balance_unchanged()
    {
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.BackOffice);
        var contractId = await RegisterAsync(ops);
        await InjectAsync(ops, contractId, Day1, 1_000m, 28m);
        var reason = $"fail-{Guid.NewGuid():N}";

        await ExecuteAsOwnerAsync($"""
            CREATE FUNCTION inventory.fail_{reason[5..]}() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN RAISE EXCEPTION 'ledger insert refused by the test'; END $$;
            CREATE TRIGGER fail_{reason[5..]} BEFORE INSERT ON inventory.inventory_movement
            FOR EACH ROW WHEN (NEW.reason = '{reason}') EXECUTE FUNCTION inventory.fail_{reason[5..]}();
            """);
        try
        {
            // The failure is a bug-class 500. The test server rethrows it to
            // the client rather than writing a response.
            HttpResponseMessage? response = null;
            try
            {
                response = await ops.PostAsJsonAsync($"/storage-contracts/{contractId}/injections",
                    new { gasDay = Day1, quantity = 500m, unitCost = 40m, reason });
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException) { }
            Assert.True(response is null || !response.IsSuccessStatusCode);
        }
        finally
        {
            await ExecuteAsOwnerAsync($"""
                DROP TRIGGER fail_{reason[5..]} ON inventory.inventory_movement;
                DROP FUNCTION inventory.fail_{reason[5..]}();
                """);
        }

        var contract = (await ops.GetFromJsonAsync<List<StorageDto>>("/storage-contracts"))!.Single(c => c.Id == contractId);
        Assert.Equal((1_000m, 28m, 2), (contract.Quantity, contract.Wacog, contract.Version));
        var ledger = (await ops.GetFromJsonAsync<List<MovementDto>>($"/storage-contracts/{contractId}/movements"))!;
        Assert.Equal(1_000m, Assert.Single(ledger).QuantityAfter);
    }

    // ADR-0005 layer 2 for every table storage adds: a raw query as the app
    // role, past EF Core's query filter, sees only the session's tenant.
    [Theory]
    [InlineData("inventory.storage_contract")]
    [InlineData("inventory.inventory_movement")]
    [InlineData("risk.inventory_valuation")]
    [InlineData("risk.valuation_run")]
    public async Task A_raw_query_as_the_app_role_sees_only_its_own_tenants_storage(string table)
    {
        var tenantId = Guid.NewGuid();
        var ops = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);
        var risk = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);
        var contractId = await RegisterAsync(ops);
        await InjectAsync(ops, contractId, Day1, 1_000m, 28m);
        await PublishCurrentMonthGasAsync(risk, 33m);
        await factory.Services.RunEndOfDayValuationAsync(CancellationToken.None);

        await using var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();
        Assert.Equal(0, await CountAsync(connection, table, Guid.NewGuid()));
        Assert.True(await CountAsync(connection, table, tenantId) > 0);
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string table, Guid sessionTenant)
    {
        // Each call sets its own session variable: a pooled connection may
        // carry one from an earlier use (see RowLevelSecurityTests).
        await using var command = connection.CreateCommand();
        command.CommandText = $"SET app.tenant_id = '{sessionTenant:D}'; SELECT count(*) FROM {table};";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsOwnerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
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

    private sealed record MovementDto(string Kind, decimal Quantity, decimal UnitCost, decimal Cost, decimal QuantityAfter, decimal WacogAfter);

    private sealed record InventoryValuationRunDto(Guid? RunId, List<InventoryValuationDto> Items, decimal TotalUnrealized);

    private sealed record InventoryValuationDto(decimal Quantity, decimal Wacog, decimal CurvePrice, decimal UnrealizedAmount);
}
