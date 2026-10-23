using Seamline.Modules.MarketData.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// Spec 03, rules 1–5. Pure arithmetic over the fixings and the forward —
// FormulaPricingService fetches both. Intermediate values are unrounded;
// rounding happens once at each displayed result (ADR-0007).
internal sealed record FormulaPrice(
    IndexPricingTerms Terms,
    int PricingDays,
    int FixedDays,
    decimal SumFixings,
    decimal? Forward)
{
    public static FormulaPrice From(IndexPricingTerms terms, IEnumerable<IndexFixingRef> fixings, decimal? forward)
    {
        var days = terms.PricingDays().ToHashSet();
        var counted = fixings.Where(f => days.Contains(f.FixingDate)).ToList();
        return new FormulaPrice(terms, days.Count, counted.Count, counted.Sum(f => f.Value), forward);
    }

    public bool FullyFixed => FixedDays == PricingDays;

    public decimal? AverageFixing => FixedDays == 0 ? null : SumFixings / FixedDays;

    // expected = (Σ fixings + (N − k) × F) / N + premium. With every day
    // fixed the forward drops out; with no forward the unfixed days cannot
    // be priced, and the expected price falls back to the fixed average
    // (rule 6) — or to nothing before the first fixing.
    public decimal? Expected(decimal? forward = null)
    {
        var f = forward ?? Forward;
        if (FullyFixed)
            return SumFixings / PricingDays + Terms.Premium;
        if (f is { } price)
            return (SumFixings + (PricingDays - FixedDays) * price) / PricingDays + Terms.Premium;
        return AverageFixing + Terms.Premium;
    }

    public decimal? FinalPrice => FullyFixed ? ToPriceScale(SumFixings / PricingDays + Terms.Premium) : null;

    // The one rounding point for a displayed price or notional: 4 dp, the
    // scale of every price column (ADR-0007).
    public static decimal? ToPriceScale(decimal? value) =>
        value is { } v ? Math.Round(v, 4, MidpointRounding.ToEven) : null;

    public decimal FixedQuantity(decimal volume) =>
        Math.Round(volume * FixedDays / PricingDays, 3, MidpointRounding.ToEven);
}
