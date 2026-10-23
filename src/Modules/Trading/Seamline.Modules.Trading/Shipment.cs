using System.Text.RegularExpressions;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

// Nominated -> Loaded -> QualityCertified | QualityRejected.
// "Awaiting nomination" is not a row: it is an Active cargo trade with no
// shipment yet. One shipment per trade (no part-cargoes, spec 01).
internal enum ShipmentState
{
    Nominated = 1,
    Loaded = 2,
    QualityCertified = 3,
    QualityRejected = 4
}

// The physical side of a cargo trade (ADR-0034), kept in Trading rather than
// a module of its own (ADR-0037): loading *is* the trade's delivery, and the
// two must commit — and publish through the one bus outbox — together.
internal sealed partial class Shipment : TenantOwnedEntity<Guid>
{
    public Guid TradeId { get; private set; }
    public ShipmentState State { get; private set; }
    public string VesselName { get; private set; } = string.Empty;
    public string Imo { get; private set; } = string.Empty;
    public DateOnly Eta { get; private set; }
    public decimal? BlQuantity { get; private set; }
    public DateOnly? BlDate { get; private set; }
    public decimal? MeasuredNarKcalKg { get; private set; }
    public decimal? SulphurPct { get; private set; }
    public decimal? AshPct { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset LastChangedAt { get; private set; }

    private Shipment() { }

    public static (Shipment Shipment, ShipmentHistory History, CargoNominated Event) Nominate(
        Trade trade, string vesselName, string imo, DateOnly eta, string changedBy)
    {
        if (trade.State != TradeState.Active)
            throw new DomainRuleViolationException($"Trade {trade.Id}: a vessel can be nominated only for an Active trade, was {trade.State}.");
        var terms = trade.CargoTerms
            ?? throw new DomainRuleViolationException($"Trade {trade.Id} is not a cargo trade.");
        if (string.IsNullOrWhiteSpace(vesselName))
            throw new ArgumentException("Vessel name is required.", nameof(vesselName));
        if (!ImoNumber().IsMatch(imo))
            throw new ArgumentException("IMO number must be 7 digits.", nameof(imo));
        if (eta < terms.LaycanFrom || eta > terms.LaycanTo)
            throw new ArgumentException($"ETA {eta:yyyy-MM-dd} is outside the laycan {terms.LaycanFrom:yyyy-MM-dd}–{terms.LaycanTo:yyyy-MM-dd}.");

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            TenantId = trade.TenantId,
            TradeId = trade.Id,
            State = ShipmentState.Nominated,
            VesselName = vesselName.Trim(),
            Imo = imo,
            Eta = eta,
            Version = 1,
            LastChangedAt = DateTimeOffset.UtcNow
        };
        var reason = $"Nominated {shipment.VesselName} (IMO {imo}), ETA {eta:yyyy-MM-dd}";
        return (shipment, ShipmentHistory.CreateSnapshot(shipment, changedBy, reason),
            new CargoNominated(trade.Id, trade.TenantId.Value, shipment.VesselName, imo, eta, changedBy, reason));
    }

    // Records the B/L. The tolerance check belongs to the contract, so the
    // trade's DeliverLoaded makes it; the caller does both in one transaction.
    public ShipmentHistory RecordLoading(decimal blQuantity, DateOnly blDate, string changedBy)
    {
        RequireState(nameof(RecordLoading), ShipmentState.Nominated);
        if (blQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(blQuantity), "B/L quantity must be positive.");

        BlQuantity = blQuantity;
        BlDate = blDate;
        State = ShipmentState.Loaded;
        Touch();
        return ShipmentHistory.CreateSnapshot(this, changedBy, $"Loaded: B/L {blDate:yyyy-MM-dd}, {blQuantity:0.000}");
    }

    // A parameter outside the contract's rejection limits ends the automated
    // flow: no final invoice, the claim is handled outside the system.
    //
    // tradePrice is the price the final invoice adjusts from; null while an
    // index-priced trade's window is still open, and the final invoice then
    // waits for TradePriceFixed (ADR-0035).
    public (ShipmentHistory History, object Event) RecordCertificate(
        CargoTerms terms, decimal measuredNar, decimal sulphurPct, decimal ashPct, string changedBy, decimal? tradePrice)
    {
        RequireState(nameof(RecordCertificate), ShipmentState.Loaded);
        if (measuredNar <= 0 || sulphurPct < 0 || ashPct < 0)
            throw new ArgumentException("Calorific value must be positive; sulphur and ash cannot be negative.");

        MeasuredNarKcalKg = measuredNar;
        SulphurPct = sulphurPct;
        AshPct = ashPct;
        var accepted = terms.WithinQualityLimits(measuredNar, sulphurPct, ashPct);
        State = accepted ? ShipmentState.QualityCertified : ShipmentState.QualityRejected;
        Touch();

        var reason = accepted
            ? $"Certified: NAR {measuredNar:0.0}, S {sulphurPct:0.00}%, ash {ashPct:0.00}%"
            : $"Rejected on quality: NAR {measuredNar:0.0}, S {sulphurPct:0.00}%, ash {ashPct:0.00}%";
        var history = ShipmentHistory.CreateSnapshot(this, changedBy, reason);
        object @event = accepted
            ? new CargoQualityCertified(TradeId, TenantId.Value, measuredNar, sulphurPct, ashPct, terms.BaseNarKcalKg, changedBy, reason,
                FinalPrice: tradePrice, PricePending: tradePrice is null)
            : new CargoQualityRejected(TradeId, TenantId.Value, measuredNar, sulphurPct, ashPct, changedBy, reason);
        return (history, @event);
    }

    // What the final invoice needs from this cargo, once it is certified.
    public CargoCertificate? CertifiedQuality(CargoTerms terms) =>
        State == ShipmentState.QualityCertified && MeasuredNarKcalKg is { } measured
            ? new CargoCertificate(measured, terms.BaseNarKcalKg)
            : null;

    private void Touch()
    {
        Version++;
        LastChangedAt = DateTimeOffset.UtcNow;
    }

    private void RequireState(string methodName, ShipmentState allowed)
    {
        if (State != allowed)
            throw new DomainRuleViolationException($"Shipment for trade {TradeId}: {methodName} requires state {allowed}, was {State}.");
    }

    [GeneratedRegex("^[0-9]{7}$")]
    private static partial Regex ImoNumber();
}
