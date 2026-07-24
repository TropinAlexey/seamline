namespace Seamline.Modules.Trading.Contracts;

// Published when an already-active trade's volume/price is corrected.
// CommodityCode/DeliveryPeriod/Direction/CounterpartyId never change on an
// amendment — only Volume and Price do — so Risk can key its position
// lookup on them unchanged and adjust by (NewVolume - OldVolume) instead of
// recomputing the position from scratch.
public sealed record TradeAmended(
    Guid TradeId,
    Guid TenantId,
    string CommodityCode,
    string DeliveryPeriod,
    TradeDirection Direction,
    decimal OldVolume,
    decimal NewVolume,
    decimal Price,
    Guid CounterpartyId,
    string Actor,
    string Reason);
