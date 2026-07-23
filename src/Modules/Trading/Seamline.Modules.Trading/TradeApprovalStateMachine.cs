using MassTransit;

namespace Seamline.Modules.Trading.Internal;

// Orchestration, not choreography — the process has a timeout, a human
// decision point, and a compensation path, so it needs to be inspectable as
// one thing. Only engages when a trade breaches the counterparty's credit
// limit; the within-limit path never touches this. See ADR-0008.
internal sealed class TradeApprovalStateMachine : MassTransitStateMachine<TradeApprovalState>
{
    public State CreditPending { get; private set; } = null!;

    public Event<TradeApprovalRequested> TradeApprovalRequestedEvent { get; private set; } = null!;
    public Event<TradeApprovalGranted> TradeApprovalGrantedEvent { get; private set; } = null!;
    public Event<TradeApprovalDenied> TradeApprovalDeniedEvent { get; private set; } = null!;

    public Schedule<TradeApprovalState, ApprovalTimeoutExpired> ApprovalTimeout { get; private set; } = null!;

    public TradeApprovalStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => TradeApprovalRequestedEvent, x => x.CorrelateById(context => context.Message.TradeId));

        // TradeApprovalRequested reaches this saga through the transactional
        // outbox (polled, not instant); an approve/reject call made right
        // after submit can outrun it and arrive before the saga instance
        // exists. Faulting on a missing instance (instead of the MassTransit
        // default of silently discarding the event) turns that race into a
        // retried delivery instead of a silently dropped approval — see
        // the global retry policy in Program.cs.
        Event(() => TradeApprovalGrantedEvent, x =>
        {
            x.CorrelateById(context => context.Message.TradeId);
            x.OnMissingInstance(m => m.Fault());
        });
        Event(() => TradeApprovalDeniedEvent, x =>
        {
            x.CorrelateById(context => context.Message.TradeId);
            x.OnMissingInstance(m => m.Fault());
        });

        Schedule(() => ApprovalTimeout, x => x.ApprovalTimeoutTokenId, s =>
        {
            // Demo-scale timeout — a real deployment would use hours or
            // days, matching how long a risk manager actually takes.
            s.Delay = TimeSpan.FromMinutes(5);
            s.Received = r => r.CorrelateById(context => context.Message.TradeId);
        });

        Initially(
            When(TradeApprovalRequestedEvent)
                .Then(context =>
                {
                    context.Saga.TenantId = context.Message.TenantId;
                    context.Saga.CounterpartyId = context.Message.CounterpartyId;
                    context.Saga.Notional = context.Message.Notional;
                })
                .Schedule(ApprovalTimeout, context => new ApprovalTimeoutExpired(context.Message.TradeId))
                .TransitionTo(CreditPending));

        During(CreditPending,
            When(TradeApprovalGrantedEvent)
                .Unschedule(ApprovalTimeout)
                .Publish(context => new TradeApprovalCompleted(context.Saga.CorrelationId, context.Saga.TenantId, Approved: true))
                .Finalize(),
            When(TradeApprovalDeniedEvent)
                .Unschedule(ApprovalTimeout)
                .Publish(context => new TradeApprovalCompleted(context.Saga.CorrelationId, context.Saga.TenantId, Approved: false))
                .Finalize(),
            When(ApprovalTimeout.Received)
                .Publish(context => new TradeApprovalCompleted(context.Saga.CorrelationId, context.Saga.TenantId, Approved: false))
                .Finalize());

        SetCompletedWhenFinalized();
    }
}
