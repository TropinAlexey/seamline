namespace Seamline.Modules.MarketData.Contracts;

// Synchronous, in-process read query — same pattern as Reference's
// ICounterpartyDirectory. MarketData owns curve data; nothing else may
// write it, but Risk reads it directly rather than duplicating it.
public interface ICurvePointDirectory
{
    Task<CurvePointRef?> FindAsync(string commodityCode, string deliveryPeriod, CancellationToken cancellationToken = default);
}
