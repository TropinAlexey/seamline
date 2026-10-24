using Seamline.SharedKernel;

namespace Seamline.Modules.Inventory.Internal;

// Booked storage capacity with the gas currently in it (ADR-0036, spec 02).
// The balance (Quantity, Wacog) is current state on the contract row,
// changed only together with an append-only InventoryMovement — the same
// split as trade and trade_history (ADR-0006).
internal sealed class StorageContract : TenantOwnedEntity<Guid>
{
    public string Facility { get; private set; } = string.Empty;
    public string CommodityCode { get; private set; } = string.Empty;
    public decimal Capacity { get; private set; }
    public decimal MaxInjectionPerDay { get; private set; }
    public decimal MaxWithdrawalPerDay { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly ValidTo { get; private set; }

    public decimal Quantity { get; private set; }

    // Weighted average cost of the gas in storage, EUR/MWh, 0 when empty.
    // Kept to 12 dp, not the 4 dp it is shown at: rounding to the price
    // scale on every movement drifts — 30 daily injections ended 0.0002 off
    // spec 02's worked example (ADR-0040). It is rounded to 4 dp wherever it
    // leaves the aggregate: the ledger, the API and the valuation snapshot.
    public decimal Wacog { get; private set; }

    public const int WacogScale = 12;

    public decimal WacogShown => PriceScale.Round(Wacog);
    public int Version { get; private set; }

    private StorageContract() { }

    public static StorageContract Register(
        TenantId tenantId, string facility, string commodityCode, decimal capacity,
        decimal maxInjectionPerDay, decimal maxWithdrawalPerDay, DateOnly validFrom, DateOnly validTo)
    {
        if (string.IsNullOrWhiteSpace(facility))
            throw new ArgumentException("Facility is required.", nameof(facility));
        if (capacity <= 0 || maxInjectionPerDay <= 0 || maxWithdrawalPerDay <= 0)
            throw new ArgumentException("Capacity and daily rates must be positive.");
        if (validFrom > validTo)
            throw new ArgumentException("The contract cannot end before it starts.", nameof(validFrom));

        return new StorageContract
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Facility = facility.Trim(),
            CommodityCode = commodityCode,
            Capacity = capacity,
            MaxInjectionPerDay = maxInjectionPerDay,
            MaxWithdrawalPerDay = maxWithdrawalPerDay,
            ValidFrom = validFrom,
            ValidTo = validTo,
            Version = 1
        };
    }

    // injectedThatDay: what the gas day already holds in injections, which
    // the caller reads under the contract's row lock.
    public InventoryMovement Inject(
        DateOnly gasDay, decimal quantity, decimal unitCost, decimal injectedThatDay, Guid? tradeId, string actor, string reason)
    {
        RequireGasDay(gasDay);
        RequirePositive(quantity);
        if (unitCost <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitCost), "Unit cost must be positive.");
        if (injectedThatDay + quantity > MaxInjectionPerDay)
            throw new DomainRuleViolationException(
                $"Injecting {quantity:0.000} MWh on {gasDay:yyyy-MM-dd} would take the day to {injectedThatDay + quantity:0.000}, " +
                $"above the {MaxInjectionPerDay:0.000} MWh/day injection rate.");
        if (Quantity + quantity > Capacity)
            throw new DomainRuleViolationException(
                $"Injecting {quantity:0.000} MWh would fill the storage to {Quantity + quantity:0.000}, above its {Capacity:0.000} MWh capacity.");

        return Apply(InventoryMovementKind.Injection, gasDay, quantity, unitCost, tradeId, actor, reason);
    }

    public InventoryMovement Withdraw(
        DateOnly gasDay, decimal quantity, decimal withdrawnThatDay, Guid? tradeId, string actor, string reason)
    {
        RequireGasDay(gasDay);
        RequirePositive(quantity);
        if (withdrawnThatDay + quantity > MaxWithdrawalPerDay)
            throw new DomainRuleViolationException(
                $"Withdrawing {quantity:0.000} MWh on {gasDay:yyyy-MM-dd} would take the day to {withdrawnThatDay + quantity:0.000}, " +
                $"above the {MaxWithdrawalPerDay:0.000} MWh/day withdrawal rate.");
        if (quantity > Quantity)
            throw new DomainRuleViolationException(
                $"Withdrawing {quantity:0.000} MWh is more than the {Quantity:0.000} MWh in storage; inventory never goes negative.");

        // A withdrawal leaves at the current WACOG, recorded by the system
        // at the price scale, as an invoice unit price would be.
        return Apply(InventoryMovementKind.Withdrawal, gasDay, quantity, WacogShown, tradeId, actor, reason);
    }

    private InventoryMovement Apply(
        InventoryMovementKind kind, DateOnly gasDay, decimal quantity, decimal unitCost, Guid? tradeId, string actor, string reason)
    {
        var signed = kind == InventoryMovementKind.Injection ? quantity : -quantity;
        Wacog = Math.Round(WeightedAverageCost.After(Quantity, Wacog, signed, unitCost), WacogScale, MidpointRounding.ToEven);
        Quantity += signed;
        Version++;
        return InventoryMovement.Record(this, kind, gasDay, quantity, unitCost, tradeId, actor, reason);
    }

    private void RequireGasDay(DateOnly gasDay)
    {
        if (gasDay < ValidFrom || gasDay > ValidTo)
            throw new ArgumentException(
                $"Gas day {gasDay:yyyy-MM-dd} is outside the contract's validity {ValidFrom:yyyy-MM-dd}–{ValidTo:yyyy-MM-dd}.");
    }

    private static void RequirePositive(decimal quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
    }
}
