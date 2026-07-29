using Seamline.Modules.Trading.Internal;

namespace Seamline.Reporting.Worker;

// The one thing Hangfire schedules — a thin instance-method wrapper around
// TradingModuleExtensions.RunReportingBatchAsync so Hangfire's recurring
// job API (which resolves job classes through DI, not arbitrary static/
// extension-method expressions) has something concrete to construct. Same
// shape as Valuation.Worker's ValuationJob.
public sealed class ReportingJob(IServiceProvider services)
{
    public Task RunAsync(CancellationToken cancellationToken) => services.RunReportingBatchAsync(cancellationToken);
}
