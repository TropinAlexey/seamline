using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// An index-priced trade, valued on its own (ADR-0035). It stays out of
// Position: a weighted average cost basis that ADR-0014 keeps fixed on a
// partial reduction cannot absorb a price that moves with every fixing.
// Derived from Trading's events, like Position — no write API.
internal sealed class FormulaTrade : TenantOwnedEntity<Guid>
{
    public Guid TradeId { get; private set; }
    public string CommodityCode { get; private set; } = string.Empty;
    public string DeliveryPeriod { get; private set; } = string.Empty;
    public decimal SignedVolume { get; private set; }
    public IndexPricingTerms Pricing { get; private set; } = null!;

    // Set by TradePriceFixed once every pricing day has fixed; valuation then
    // marks against it instead of recomputing the expected price.
    public decimal? FinalPrice { get; private set; }

    // A fixed-price trade leaves the position at delivery. An index-priced
    // one delivered mid-window still carries price risk on its unfixed days,
    // so it stays open until it is both delivered and fixed.
    public bool IsDelivered { get; private set; }
    public bool IsOpen { get; private set; }

    private FormulaTrade() { }

    public static FormulaTrade Open(
        TenantId tenantId, Guid tradeId, string commodityCode, string deliveryPeriod, decimal signedVolume, IndexPricingTerms pricing) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TradeId = tradeId,
            CommodityCode = commodityCode,
            DeliveryPeriod = deliveryPeriod,
            SignedVolume = signedVolume,
            Pricing = pricing,
            IsOpen = true
        };

    // The price the trade is marked at: its confirmed final price once fixed,
    // otherwise the formula's expected price — at today's forward, or at a
    // shocked one for a stress scenario. Null when nothing can price it yet.
    public decimal? MarkPrice(FormulaPrice formula, decimal? forward = null) =>
        FinalPrice ?? formula.Expected(forward);

    public void Amend(decimal signedVolume) => SignedVolume = signedVolume;

    public void Fix(decimal finalPrice)
    {
        FinalPrice = finalPrice;
        IsOpen = !IsDelivered;
    }

    public void MarkDelivered()
    {
        IsDelivered = true;
        IsOpen = FinalPrice is null;
    }
}
