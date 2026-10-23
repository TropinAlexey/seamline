using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

// Contract terms of a cargo trade (ADR-0034, spec 01). FOB is implied —
// it is the only incoterm offered. The quality spec is a snapshot of the
// commodity's at booking: a later change to master data never reprices a
// contract already signed.
internal sealed record CargoTerms
{
    public const decimal MaxTolerancePct = 20m;

    public decimal TolerancePct { get; }
    public DateOnly LaycanFrom { get; }
    public DateOnly LaycanTo { get; }
    public decimal BaseNarKcalKg { get; }
    public decimal RejectBelowNarKcalKg { get; }
    public decimal MaxSulphurPct { get; }
    public decimal MaxAshPct { get; }

    public CargoTerms(
        decimal tolerancePct,
        DateOnly laycanFrom,
        DateOnly laycanTo,
        decimal baseNarKcalKg,
        decimal rejectBelowNarKcalKg,
        decimal maxSulphurPct,
        decimal maxAshPct)
    {
        if (tolerancePct is < 0 or > MaxTolerancePct)
            throw new ArgumentOutOfRangeException(nameof(tolerancePct), $"Tolerance must be between 0 and {MaxTolerancePct}%.");
        if (laycanFrom > laycanTo)
            throw new ArgumentException("Laycan start must not be after its end.", nameof(laycanFrom));

        TolerancePct = tolerancePct;
        LaycanFrom = laycanFrom;
        LaycanTo = laycanTo;
        BaseNarKcalKg = baseNarKcalKg;
        RejectBelowNarKcalKg = rejectBelowNarKcalKg;
        MaxSulphurPct = maxSulphurPct;
        MaxAshPct = maxAshPct;
    }

    // The laycan lies inside the shipment month, so positions, curves and
    // stress keep working per month (ADR-0032).
    public void RequireLaycanWithin(string deliveryPeriod)
    {
        DeliveryPeriodFormat.Validate(deliveryPeriod);
        var period = $"{LaycanFrom:yyyy-MM}";
        if (period != deliveryPeriod || $"{LaycanTo:yyyy-MM}" != deliveryPeriod)
            throw new ArgumentException($"The laycan must lie within the delivery period {deliveryPeriod}.");
    }

    // Seller's option, both ends inclusive. Quantities are numeric(18,3), so
    // the band is rounded to that scale explicitly (ADR-0007).
    public (decimal Min, decimal Max) ToleranceBand(decimal contractVolume)
    {
        var delta = contractVolume * TolerancePct / 100m;
        return (Math.Round(contractVolume - delta, 3, MidpointRounding.ToEven),
                Math.Round(contractVolume + delta, 3, MidpointRounding.ToEven));
    }

    public bool WithinQualityLimits(decimal measuredNar, decimal sulphurPct, decimal ashPct) =>
        measuredNar >= RejectBelowNarKcalKg && sulphurPct <= MaxSulphurPct && ashPct <= MaxAshPct;

    public CargoTermsRef ToRef() =>
        new(TolerancePct, LaycanFrom, LaycanTo, BaseNarKcalKg, RejectBelowNarKcalKg, MaxSulphurPct, MaxAshPct);
}
