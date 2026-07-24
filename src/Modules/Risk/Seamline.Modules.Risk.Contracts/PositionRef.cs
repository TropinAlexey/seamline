namespace Seamline.Modules.Risk.Contracts;

// MarkPrice comes from MarketData.Contracts.ICurvePointDirectory (a
// synchronous, in-process read, same pattern as Reference's
// ICounterpartyDirectory) — null when no curve point has been published yet
// for this commodity/period. This is a stepping stone toward real
// mark-to-market (ADR-0007: (forward_price - trade_price) * volume), not
// MtM itself — Position doesn't track a trade-weighted price, so computing
// unrealized P&L is Phase 2's Valuation.Worker, not this endpoint.
public sealed record PositionRef(string CommodityCode, string DeliveryPeriod, decimal NetVolume, decimal? MarkPrice);
