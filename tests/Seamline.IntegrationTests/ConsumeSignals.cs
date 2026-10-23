using MassTransit;

namespace Seamline.IntegrationTests;

// Event-driven waiting for the asynchronous side of the system. Consumers
// update read models off the transactional outbox, so a test must wait for
// them — and it waits for the consumer it depends on to finish the message
// it caused, not for a wall-clock interval to pass. Connected to the
// production bus configuration as a consume observer, so the saga keeps its
// real EF Core repository (MassTransit's test harness would swap it for an
// in-memory one).
//
// The only timeout left is a guard against a hang: reaching it means the
// message was never consumed, not that the machine was slow.
public sealed class ConsumeSignals : IConsumeObserver
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(60);

    private readonly Lock _gate = new();
    private readonly List<(string Endpoint, object Message)> _consumed = [];
    private readonly List<Waiter> _waiters = [];

    // Completes once TConsumer's endpoint has consumed a TMessage matching
    // the predicate. The endpoint name comes from the same formatter
    // ConfigureEndpoints uses, so it follows a renamed consumer. Consumers
    // with the same name in different modules (Risk's and Settlement's
    // TradeDeliveredConsumer) share one endpoint, and PostConsume fires once
    // per message after all of them have run — checked when this was
    // written. A message consumed before the call counts too.
    public Task Consumed<TConsumer, TMessage>(Func<TMessage, bool> match)
        where TConsumer : class, IConsumer<TMessage>
        where TMessage : class
    {
        var endpoint = DefaultEndpointNameFormatter.Instance.Consumer<TConsumer>();
        bool Matches(string e, object m) => e == endpoint && m is TMessage message && match(message);

        Waiter waiter;
        lock (_gate)
        {
            if (_consumed.Any(c => Matches(c.Endpoint, c.Message)))
                return Task.CompletedTask;
            waiter = new Waiter(Matches);
            _waiters.Add(waiter);
        }

        return waiter.Completion.Task.WaitAsync(HangGuard).ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception!.InnerException is TimeoutException)
                throw new TimeoutException(
                    $"{typeof(TConsumer).Name} never consumed the expected {typeof(TMessage).Name} " +
                    $"within {HangGuard.TotalSeconds:0} s: it was not published, or its consumer keeps failing.");
            t.GetAwaiter().GetResult();
        }, TaskScheduler.Default);
    }

    public Task PreConsume<T>(ConsumeContext<T> context) where T : class => Task.CompletedTask;

    // Only a successful consume counts. A failed attempt (some consumers
    // throw on purpose to be retried until their input has arrived) is not
    // completion; the retry that succeeds is.
    public Task PostConsume<T>(ConsumeContext<T> context) where T : class
    {
        var endpoint = context.ReceiveContext.InputAddress.AbsolutePath.Trim('/');
        lock (_gate)
        {
            _consumed.Add((endpoint, context.Message));
            foreach (var waiter in _waiters.Where(w => w.Matches(endpoint, context.Message)).ToList())
            {
                _waiters.Remove(waiter);
                waiter.Completion.TrySetResult();
            }
        }
        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class => Task.CompletedTask;

    private sealed record Waiter(Func<string, object, bool> Matches)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
