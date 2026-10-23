namespace Seamline.Modules.MarketData.Contracts;

public sealed record PriceIndexRef(string Code, string CommodityCode, string Name);

public sealed record IndexFixingRef(DateOnly FixingDate, decimal Value);

// Synchronous, in-process read query, the same pattern as
// ICurvePointDirectory. Fixings are settlement inputs (ADR-0035): Risk prices
// formula trades from them and Trading validates index codes against them;
// neither keeps a copy.
public interface IIndexFixingDirectory
{
    Task<PriceIndexRef?> FindIndexAsync(string indexCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexFixingRef>> ListFixingsAsync(
        string indexCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
