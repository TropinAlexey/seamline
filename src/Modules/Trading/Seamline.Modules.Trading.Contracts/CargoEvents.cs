namespace Seamline.Modules.Trading.Contracts;

// Shipment lifecycle of a cargo trade (ADR-0034, ADR-0037). Each carries
// Actor/Reason for Audit (ADR-0010); Settlement reacts to the certificate.
// Loading itself is announced as TradeDelivered — loading is delivery under
// FOB — so there is no separate "loaded" event to keep in step with it.

public sealed record CargoNominated(
    Guid TradeId,
    Guid TenantId,
    string VesselName,
    string Imo,
    DateOnly Eta,
    string Actor,
    string Reason);

public sealed record CargoQualityCertified(
    Guid TradeId,
    Guid TenantId,
    decimal MeasuredNarKcalKg,
    decimal SulphurPct,
    decimal AshPct,
    decimal BaseNarKcalKg,
    string Actor,
    string Reason);

public sealed record CargoQualityRejected(
    Guid TradeId,
    Guid TenantId,
    decimal MeasuredNarKcalKg,
    decimal SulphurPct,
    decimal AshPct,
    string Actor,
    string Reason);
