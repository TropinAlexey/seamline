namespace Seamline.Modules.Trading.Contracts;

// An index-priced trade's window has fully fixed and its final price is
// confirmed (ADR-0035, ADR-0038). Risk marks against FinalPrice from now on.
// When the cargo's quality certificate is already in, MeasuredNarKcalKg and
// BaseNarKcalKg ride along so Settlement can issue the final invoice from
// this event alone; otherwise the certificate event will carry FinalPrice.
// Trading decides which of the two triggers the final, in one transaction,
// so Settlement never has to remember half of it.
public sealed record TradePriceFixed(
    Guid TradeId,
    Guid TenantId,
    decimal FinalPrice,
    string Actor,
    string Reason,
    decimal? MeasuredNarKcalKg = null,
    decimal? BaseNarKcalKg = null);
