namespace Seamline.Modules.Trading.Internal;

// The two numbers the final invoice's calorific adjustment needs from a
// certified cargo (spec 01): what was measured and what the contract based
// its price on.
internal sealed record CargoCertificate(decimal MeasuredNarKcalKg, decimal BaseNarKcalKg);
