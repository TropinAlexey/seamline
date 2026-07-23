namespace Seamline.Modules.Trading.Contracts;

// Published when a trade fails the credit check outright, or when the
// credit-limit saga (ADR-0008) ends in rejection or an approval timeout.
public sealed record TradeRejected(Guid TradeId, Guid TenantId);
