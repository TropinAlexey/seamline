namespace Seamline.Modules.Trading.Contracts;

// Contract terms of a cargo trade (ADR-0034): FOB, a quantity tolerance at
// the seller's option, a loading window, and the commodity's quality spec
// as it stood when the trade was booked — a later change to the commodity's
// spec never reprices a contract already signed.
public sealed record CargoTermsRef(
    decimal TolerancePct,
    DateOnly LaycanFrom,
    DateOnly LaycanTo,
    decimal BaseNarKcalKg,
    decimal RejectBelowNarKcalKg,
    decimal MaxSulphurPct,
    decimal MaxAshPct);
