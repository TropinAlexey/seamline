namespace Seamline.Modules.Risk.Contracts;

// A live net position with its mark-to-market. MarkPrice comes from
// MarketData.Contracts.ICurvePointDirectory (a synchronous, in-process read,
// same pattern as Reference's ICounterpartyDirectory) — null when no curve
// point has been published yet for this commodity/period, and then MtmAmount
// is null too. MtmAmount is computed and rounded server-side with the same
// MtmCalculator the EOD valuation uses (ADR-0007, ADR-0031), so the live
// figure and the EOD snapshot can't disagree on the formula.
public sealed record PositionRef(
    string CommodityCode,
    string DeliveryPeriod,
    decimal NetVolume,
    decimal WeightedAvgPrice,
    decimal? MarkPrice,
    decimal? MtmAmount);
