namespace Seamline.Modules.Risk.Internal;

// One EOD valuation pass (ADR-0031). Every ValuationSnapshot and
// StressScenarioResult written by the pass carries the same RunId and
// ValuedAt, so "the latest run" is a single key, and a stress result can
// be compared with the base valuation from the same run. Before this each
// row stamped its own UtcNow, and the rows of one run had no common key.
internal sealed record EodRun(Guid RunId, DateTimeOffset ValuedAt)
{
    public static EodRun Start() => new(Guid.NewGuid(), DateTimeOffset.UtcNow);
}
