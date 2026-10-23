namespace Seamline.Modules.Reference.Contracts;

// Commodity master data (ADR-0033). Every volume on a trade is in its
// commodity's Unit; DeliveryModel says whether it is delivered over the
// period (Flow) or shipped as a discrete cargo with an actual quantity and
// measured quality (Cargo, ADR-0034).
public sealed record CommodityRef(
    string Code,
    string Name,
    UnitOfMeasure Unit,
    DeliveryModel DeliveryModel,
    QualitySpecRef? QualitySpec);

public sealed record QualitySpecRef(
    decimal BaseNarKcalKg,
    decimal RejectBelowNarKcalKg,
    decimal MaxSulphurPct,
    decimal MaxAshPct);

public enum UnitOfMeasure
{
    MWh = 1,
    Tonne = 2
}

public enum DeliveryModel
{
    Flow = 1,
    Cargo = 2
}
