namespace Seamline.Modules.Trading.Contracts;

// Published when an already-active trade's volume/price is corrected.
// CommodityCode/DeliveryPeriod/Direction/CounterpartyId never change on an
// amendment — only Volume and Price do — so Risk can key its position
// lookup on them unchanged and adjust by (NewVolume - OldVolume) instead of
// recomputing the position from scratch. OldPrice rides along (ADR-0014,
// Valuation.Worker) so Position's weighted-average cost basis can back out
// the trade's old contribution before applying the new one; a delta alone
// isn't enough for a weighted average the way it is for NetVolume.
public sealed record TradeAmended(
    Guid TradeId,
    Guid TenantId,
    string CommodityCode,
    string DeliveryPeriod,
    TradeDirection Direction,
    decimal OldVolume,
    decimal NewVolume,
    decimal OldPrice,
    decimal NewPrice,
    Guid CounterpartyId,
    string Actor,
    string Reason);
