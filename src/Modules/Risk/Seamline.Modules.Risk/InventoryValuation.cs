using Seamline.Modules.Inventory.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// Gas in storage marked against the curve in the EOD run (ADR-0036).
// Append-only like ValuationSnapshot, one row per storage contract per run,
// carrying the run id (ADR-0031) and the curve price it used so it stays
// reproducible after the curve moves. Shown next to positions, never summed
// with them: a position is a contract not yet delivered, stock is gas that
// has been.
internal sealed class InventoryValuation : TenantOwnedEntity<Guid>
{
    public Guid StorageContractId { get; private set; }
    public string Facility { get; private set; } = string.Empty;
    public string CommodityCode { get; private set; } = string.Empty;
    public string DeliveryPeriod { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal Wacog { get; private set; }
    public decimal CurvePrice { get; private set; }
    public DateTimeOffset CurvePublishedAt { get; private set; }
    public decimal UnrealizedAmount { get; private set; }
    public Guid RunId { get; private set; }
    public DateTimeOffset ValuedAt { get; private set; }

    private InventoryValuation() { }

    // unrealized = (curve − WACOG) × quantity, through the same calculator
    // as every MtM in the system (spec 02, rule 6). The WACOG arrives at its
    // full stored precision and is rounded once in the result; the 4 dp copy
    // is for display (ADR-0040).
    public static InventoryValuation Create(
        TenantId tenantId, StorageBalanceRef stock, string deliveryPeriod, decimal curvePrice, DateTimeOffset curvePublishedAt, EodRun run) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StorageContractId = stock.StorageContractId,
            Facility = stock.Facility,
            CommodityCode = stock.CommodityCode,
            DeliveryPeriod = deliveryPeriod,
            Quantity = stock.Quantity,
            Wacog = Math.Round(stock.Wacog, 4, MidpointRounding.ToEven),
            CurvePrice = curvePrice,
            CurvePublishedAt = curvePublishedAt,
            UnrealizedAmount = MtmCalculator.Calculate(curvePrice, stock.Wacog, stock.Quantity),
            RunId = run.RunId,
            ValuedAt = run.ValuedAt
        };
}
