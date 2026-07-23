namespace Seamline.Modules.Risk.Contracts;

public sealed record PositionRef(string CommodityCode, string DeliveryPeriod, decimal NetVolume);
