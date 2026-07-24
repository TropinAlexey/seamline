namespace Seamline.Modules.MarketData.Contracts;

public sealed record CurvePointRef(string CommodityCode, string DeliveryPeriod, decimal Price);
