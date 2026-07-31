using Seamline.Modules.MarketData.Internal;

namespace Seamline.Valuation.Worker;

// The one thing Hangfire schedules — a thin instance-method wrapper around
// MarketDataModuleExtensions.RunCurveImportAsync, same shape as
// ValuationJob. Scheduled to run before ValuationJob each day (see
// Program.cs's Cron.Daily hours) so the same day's EOD valuation already
// sees a refreshed price (ADR-0018).
public sealed class CurveImportJob(IServiceProvider services)
{
    public Task RunAsync(CancellationToken cancellationToken) => services.RunCurveImportAsync(cancellationToken);
}
