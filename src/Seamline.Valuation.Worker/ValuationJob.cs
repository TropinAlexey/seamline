using Seamline.Modules.Risk.Internal;

namespace Seamline.Valuation.Worker;

// The one thing Hangfire schedules — a thin instance-method wrapper around
// RiskModuleExtensions.RunEndOfDayValuationAsync so Hangfire's recurring
// job API (which resolves job classes through DI, not arbitrary static/
// extension-method expressions) has something concrete to construct.
public sealed class ValuationJob(IServiceProvider services)
{
    public Task RunAsync(CancellationToken cancellationToken) => services.RunEndOfDayValuationAsync(cancellationToken);
}
