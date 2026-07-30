using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Seamline.Modules.Trading.Internal;

namespace Seamline.Modules.Trading.Tests;

// In-process saga tests via MassTransit's test harness (ADR-0017) — no HTTP,
// no Postgres, no broker. TradeToPositionTests already covers the
// credit-approval saga end to end through Seamline.Api; these exist to pin
// down the state machine's transitions in isolation, at unit-test speed.
public sealed class TradeApprovalStateMachineTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ITestHarness _harness;

    public TradeApprovalStateMachineTests()
    {
        _provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddSagaStateMachine<TradeApprovalStateMachine, TradeApprovalState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
    }

    private async Task StartHarnessAsync() => await _harness.Start();

    // ServiceProvider.DisposeAsync() alone doesn't run IHostedService.StopAsync
    // the way an IHost would — stop the bus explicitly first so shutdown
    // ordering (drain in-flight messages, then tear down DI) is guaranteed.
    public async ValueTask DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task Approval_publishes_TradeApprovalCompleted_as_approved_and_finalizes_the_saga()
    {
        await StartHarnessAsync();

        var tradeId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await _harness.Bus.Publish(new TradeApprovalRequested(tradeId, tenantId, Guid.NewGuid(), 1_000_000m));

        var sagaHarness = _harness.GetSagaStateMachineHarness<TradeApprovalStateMachine, TradeApprovalState>();
        Assert.True(await sagaHarness.Exists(tradeId, x => x.CreditPending) is not null);

        await _harness.Bus.Publish(new TradeApprovalGranted(tradeId));

        Assert.True(await _harness.Published.Any<TradeApprovalCompleted>(
            x => x.Context.Message.TradeId == tradeId && x.Context.Message.Approved));
        Assert.True(await sagaHarness.NotExists(tradeId) is null);
    }

    [Fact]
    public async Task Denial_publishes_TradeApprovalCompleted_as_not_approved_and_finalizes_the_saga()
    {
        await StartHarnessAsync();

        var tradeId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await _harness.Bus.Publish(new TradeApprovalRequested(tradeId, tenantId, Guid.NewGuid(), 1_000_000m));
        await _harness.Bus.Publish(new TradeApprovalDenied(tradeId));

        Assert.True(await _harness.Published.Any<TradeApprovalCompleted>(
            x => x.Context.Message.TradeId == tradeId && !x.Context.Message.Approved));

        var sagaHarness = _harness.GetSagaStateMachineHarness<TradeApprovalStateMachine, TradeApprovalState>();
        Assert.True(await sagaHarness.NotExists(tradeId) is null);
    }

    [Fact]
    public async Task Cancelling_a_pending_trade_publishes_TradeApprovalCancelled_and_finalizes_the_saga()
    {
        await StartHarnessAsync();

        var tradeId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await _harness.Bus.Publish(new TradeApprovalRequested(tradeId, tenantId, Guid.NewGuid(), 1_000_000m));
        await _harness.Bus.Publish(new TradeCancelRequested(tradeId));

        Assert.True(await _harness.Published.Any<TradeApprovalCancelled>(x => x.Context.Message.TradeId == tradeId));

        var sagaHarness = _harness.GetSagaStateMachineHarness<TradeApprovalStateMachine, TradeApprovalState>();
        Assert.True(await sagaHarness.NotExists(tradeId) is null);
    }

    [Fact]
    public async Task A_granted_approval_with_no_matching_saga_instance_faults_instead_of_being_dropped()
    {
        await StartHarnessAsync();

        // No TradeApprovalRequested published first — covers the outbox-lag
        // race documented on TradeApprovalStateMachine's event correlations.
        await _harness.Bus.Publish(new TradeApprovalGranted(Guid.NewGuid()));

        Assert.True(await _harness.Published.Any<Fault<TradeApprovalGranted>>());
    }
}
