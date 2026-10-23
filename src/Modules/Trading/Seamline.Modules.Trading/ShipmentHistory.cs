using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

// Append-only, one row per shipment version, full snapshot — same shape and the same
// SELECT/INSERT-only grant as trade_history (ADR-0006).
internal sealed class ShipmentHistory
{
    public Guid Id { get; private init; }
    public Guid ShipmentId { get; private init; }
    public Guid TradeId { get; private init; }
    public TenantId TenantId { get; private init; }
    public int Version { get; private init; }
    public DateTimeOffset ValidFrom { get; private init; }
    public string ChangedBy { get; private init; } = string.Empty;
    public string ChangeReason { get; private init; } = string.Empty;
    public ShipmentState State { get; private init; }
    public string VesselName { get; private init; } = string.Empty;
    public string Imo { get; private init; } = string.Empty;
    public DateOnly Eta { get; private init; }
    public decimal? BlQuantity { get; private init; }
    public DateOnly? BlDate { get; private init; }
    public decimal? MeasuredNarKcalKg { get; private init; }
    public decimal? SulphurPct { get; private init; }
    public decimal? AshPct { get; private init; }

    private ShipmentHistory() { }

    public static ShipmentHistory CreateSnapshot(Shipment shipment, string changedBy, string changeReason) => new()
    {
        Id = Guid.NewGuid(),
        ShipmentId = shipment.Id,
        TradeId = shipment.TradeId,
        TenantId = shipment.TenantId,
        Version = shipment.Version,
        ValidFrom = shipment.LastChangedAt,
        ChangedBy = changedBy,
        ChangeReason = changeReason,
        State = shipment.State,
        VesselName = shipment.VesselName,
        Imo = shipment.Imo,
        Eta = shipment.Eta,
        BlQuantity = shipment.BlQuantity,
        BlDate = shipment.BlDate,
        MeasuredNarKcalKg = shipment.MeasuredNarKcalKg,
        SulphurPct = shipment.SulphurPct,
        AshPct = shipment.AshPct
    };
}
