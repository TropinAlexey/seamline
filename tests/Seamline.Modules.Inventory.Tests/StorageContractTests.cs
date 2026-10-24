using Seamline.Modules.Inventory.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Inventory.Tests;

// Spec 02: 100,000 MWh capacity, 2,000 MWh/day in, 3,000 MWh/day out.
public class StorageContractTests
{
    private static readonly DateOnly Day1 = new(2027, 4, 1);

    private static StorageContract Contract() =>
        StorageContract.Register(TenantId.New(), "Demo Storage North", "GAS", 100_000m, 2_000m, 3_000m,
            new DateOnly(2027, 4, 1), new DateOnly(2028, 3, 31));

    private static void InjectOverDays(StorageContract contract, DateOnly from, int days, decimal perDay, decimal cost)
    {
        for (var d = 0; d < days; d++)
            contract.Inject(from.AddDays(d), perDay, cost, injectedThatDay: 0m, null, "ops", "Injected");
    }

    [Fact]
    public void The_worked_example_blends_on_injection_and_keeps_the_average_on_withdrawal()
    {
        var contract = Contract();

        InjectOverDays(contract, Day1, 20, 2_000m, 28m);
        Assert.Equal((40_000m, 28m), (contract.Quantity, contract.WacogShown));

        // Ten daily blends: kept at 12 dp, the average lands on 29.0000.
        // Rounded to 4 dp at each movement it drifted to 29.0002 (ADR-0040).
        InjectOverDays(contract, Day1.AddDays(20), 10, 2_000m, 31m);
        Assert.Equal((60_000m, 29m), (contract.Quantity, contract.WacogShown));

        InventoryMovement? last = null;
        for (var d = 0; d < 5; d++)
            last = contract.Withdraw(Day1.AddDays(30 + d), 3_000m, withdrawnThatDay: 0m, null, "ops", "Withdrawn");

        Assert.Equal((45_000m, 29m), (contract.Quantity, contract.WacogShown));
        // A withdrawal leaves at the WACOG of the moment: 15,000 × 29 = 435,000.00.
        Assert.Equal((29m, 45_000m, 29m), (last!.UnitCost, last.QuantityAfter, last.WacogAfter));
    }

    [Fact]
    public void Emptying_resets_the_average_and_a_refill_starts_afresh()
    {
        var contract = Contract();
        contract.Inject(Day1, 1_000m, 28m, 0m, null, "ops", "In");
        contract.Withdraw(Day1.AddDays(1), 1_000m, 0m, null, "ops", "Out");

        Assert.Equal((0m, 0m), (contract.Quantity, contract.Wacog));

        contract.Inject(Day1.AddDays(2), 500m, 35m, 0m, null, "ops", "In");
        Assert.Equal(35m, contract.Wacog);
    }

    [Fact]
    public void The_average_is_shown_to_four_decimals()
    {
        var contract = Contract();
        contract.Inject(Day1, 1_000m, 28m, 0m, null, "ops", "In");
        contract.Inject(Day1.AddDays(1), 2_000m, 31m, 0m, null, "ops", "In");

        // (1,000 × 28 + 2,000 × 31) / 3,000 = 30
        Assert.Equal(30m, contract.WacogShown);

        contract.Inject(Day1.AddDays(2), 1_000m, 30.1m, 0m, null, "ops", "In");
        // (3,000 × 30 + 1,000 × 30.1) / 4,000 = 30.025
        Assert.Equal(30.025m, contract.WacogShown);

        contract.Inject(Day1.AddDays(3), 2_000m, 29.33333m, 0m, null, "ops", "In");
        // (4,000 × 30.025 + 2,000 × 29.33333) / 6,000 = 29.794443… → 29.7944,
        // kept to 12 dp underneath.
        Assert.Equal(29.7944m, contract.WacogShown);
        Assert.Equal(29.794443333333m, contract.Wacog);
    }

    [Fact]
    public void The_daily_injection_rate_is_inclusive_at_its_edge()
    {
        var contract = Contract();

        contract.Inject(Day1, 1_500m, 28m, injectedThatDay: 500m, null, "ops", "In");   // exactly 2,000
        Assert.Throws<DomainRuleViolationException>(() =>
            contract.Inject(Day1, 0.001m, 28m, injectedThatDay: 2_000m, null, "ops", "In"));
        Assert.Throws<DomainRuleViolationException>(() =>
            Contract().Inject(Day1, 2_500m, 28m, 0m, null, "ops", "In"));
    }

    [Fact]
    public void The_daily_withdrawal_rate_is_inclusive_at_its_edge()
    {
        var contract = Contract();
        InjectOverDays(contract, Day1, 5, 2_000m, 28m);

        contract.Withdraw(Day1.AddDays(10), 3_000m, withdrawnThatDay: 0m, null, "ops", "Out");
        Assert.Throws<DomainRuleViolationException>(() =>
            contract.Withdraw(Day1.AddDays(10), 0.001m, withdrawnThatDay: 3_000m, null, "ops", "Out"));
    }

    [Fact]
    public void Stock_never_exceeds_capacity_nor_goes_negative()
    {
        var small = StorageContract.Register(TenantId.New(), "Small", "GAS", 2_500m, 2_000m, 3_000m,
            new DateOnly(2027, 4, 1), new DateOnly(2028, 3, 31));
        small.Inject(Day1, 2_000m, 28m, 0m, null, "ops", "In");
        small.Inject(Day1.AddDays(1), 500m, 28m, 0m, null, "ops", "In");   // exactly full

        Assert.Throws<DomainRuleViolationException>(() => small.Inject(Day1.AddDays(2), 0.001m, 28m, 0m, null, "ops", "In"));
        Assert.Throws<DomainRuleViolationException>(() => small.Withdraw(Day1.AddDays(3), 2_500.001m, 0m, null, "ops", "Out"));
        small.Withdraw(Day1.AddDays(3), 2_500m, 0m, null, "ops", "Out");   // exactly empty
        Assert.Equal(0m, small.Quantity);
    }

    [Theory]
    [InlineData("2027-03-31")]
    [InlineData("2028-04-01")]
    public void A_gas_day_outside_the_contract_is_rejected(string day)
    {
        Assert.Throws<ArgumentException>(() =>
            Contract().Inject(DateOnly.Parse(day), 100m, 28m, 0m, null, "ops", "In"));
    }

    [Fact]
    public void Quantities_and_costs_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Contract().Inject(Day1, 0m, 28m, 0m, null, "ops", "In"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Contract().Inject(Day1, 100m, 0m, 0m, null, "ops", "In"));
        Assert.Throws<ArgumentException>(() =>
            StorageContract.Register(TenantId.New(), "X", "GAS", 0m, 1m, 1m, Day1, Day1));
    }
}
