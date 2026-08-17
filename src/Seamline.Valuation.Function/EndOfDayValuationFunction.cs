using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seamline.Modules.Risk.Internal;

namespace Seamline.Valuation.Function;

public sealed class EndOfDayValuationFunction(IServiceProvider services, ILogger<EndOfDayValuationFunction> logger)
{
    [Function("eod-valuation")]
    public async Task RunAsync([TimerTrigger("0 0 6 * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        logger.LogInformation("Timer trigger fired at {UtcNow}, past due: {IsPastDue}.", DateTime.UtcNow, timer.IsPastDue);
        await services.RunEndOfDayValuationAsync(cancellationToken);
    }
}
