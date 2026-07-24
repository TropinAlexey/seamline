using Seamline.Modules.Trading.Contracts;
using Seamline.Modules.Trading.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Tests;

public class TradeTests
{
    private static Trade CreateDraftTrade(decimal volume = 100m, decimal price = 45.5m) =>
        Trade.CreateDraft(TenantId.New(), "POWER", "2027-03", TradeDirection.Buy, volume, price, Guid.NewGuid());

    [Fact]
    public void CreateDraft_starts_in_Draft_state_at_version_1()
    {
        var trade = CreateDraftTrade();

        Assert.Equal(TradeState.Draft, trade.State);
        Assert.Equal(1, trade.Version);
    }

    [Theory]
    [InlineData("", "2027-03")]
    [InlineData(" ", "2027-03")]
    [InlineData("POWER", "")]
    [InlineData("POWER", " ")]
    public void CreateDraft_rejects_missing_commodity_or_delivery_period(string commodityCode, string deliveryPeriod)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Trade.CreateDraft(TenantId.New(), commodityCode, deliveryPeriod, TradeDirection.Buy, 100m, 45.5m, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateDraft_rejects_non_positive_volume(decimal volume)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Trade.CreateDraft(TenantId.New(), "POWER", "2027-03", TradeDirection.Buy, volume, 45.5m, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateDraft_rejects_non_positive_price(decimal price)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Trade.CreateDraft(TenantId.New(), "POWER", "2027-03", TradeDirection.Buy, 100m, price, Guid.NewGuid()));
    }

    [Fact]
    public void Notional_is_volume_times_price()
    {
        var trade = CreateDraftTrade(volume: 100m, price: 45.5m);

        Assert.Equal(4_550m, trade.Notional);
    }

    [Fact]
    public void Submit_moves_Draft_to_Submitted_and_increments_version()
    {
        var trade = CreateDraftTrade();

        var history = trade.Submit("trader", "Submitted for credit check");

        Assert.Equal(TradeState.Submitted, trade.State);
        Assert.Equal(2, trade.Version);
        Assert.Equal(2, history.Version);
        Assert.Equal(TradeState.Submitted, history.State);
        Assert.Equal("trader", history.ChangedBy);
        Assert.Equal("Submitted for credit check", history.ChangeReason);
        Assert.Equal(trade.Id, history.TradeId);
    }

    // TradeState is internal, so a public [Theory] method can't take it as a
    // parameter (CS0051) — pass the int value and cast inside instead.
    [Theory]
    [InlineData((int)TradeState.Submitted)]
    [InlineData((int)TradeState.CreditPending)]
    [InlineData((int)TradeState.Active)]
    [InlineData((int)TradeState.Rejected)]
    public void Submit_throws_when_trade_is_not_in_Draft_state(int stateValue)
    {
        var trade = MoveTo((TradeState)stateValue);

        Assert.Throws<InvalidOperationException>(() => trade.Submit("trader", "retry"));
    }

    [Fact]
    public void Activate_from_Submitted_moves_to_Active_and_publishes_TradeActivated_with_actor_and_reason()
    {
        var trade = MoveTo(TradeState.Submitted);

        var (history, activated) = trade.Activate("system", "Within credit limit — approved automatically");

        Assert.Equal(TradeState.Active, trade.State);
        Assert.Equal(TradeState.Active, history.State);
        Assert.Equal(trade.Id, activated.TradeId);
        Assert.Equal(trade.TenantId.Value, activated.TenantId);
        Assert.Equal(trade.CommodityCode, activated.CommodityCode);
        Assert.Equal(trade.DeliveryPeriod, activated.DeliveryPeriod);
        Assert.Equal(trade.Direction, activated.Direction);
        Assert.Equal(trade.Volume, activated.Volume);
        Assert.Equal(trade.Price, activated.Price);
        Assert.Equal(trade.CounterpartyId, activated.CounterpartyId);
        Assert.Equal("system", activated.Actor);
        Assert.Equal("Within credit limit — approved automatically", activated.Reason);
    }

    [Fact]
    public void Activate_from_CreditPending_moves_to_Active()
    {
        var trade = MoveTo(TradeState.CreditPending);

        var (_, activated) = trade.Activate("risk", "Approved after credit limit breach review");

        Assert.Equal(TradeState.Active, trade.State);
        Assert.Equal("risk", activated.Actor);
    }

    [Theory]
    [InlineData((int)TradeState.Draft)]
    [InlineData((int)TradeState.Active)]
    [InlineData((int)TradeState.Rejected)]
    public void Activate_throws_when_trade_is_not_Submitted_or_CreditPending(int stateValue)
    {
        var trade = MoveTo((TradeState)stateValue);

        Assert.Throws<InvalidOperationException>(() => trade.Activate("system", "n/a"));
    }

    [Fact]
    public void EnterCreditPending_from_Submitted_moves_to_CreditPending()
    {
        var trade = MoveTo(TradeState.Submitted);

        var history = trade.EnterCreditPending("system", "Credit limit breached — awaiting risk approval");

        Assert.Equal(TradeState.CreditPending, trade.State);
        Assert.Equal(TradeState.CreditPending, history.State);
    }

    [Theory]
    [InlineData((int)TradeState.Draft)]
    [InlineData((int)TradeState.CreditPending)]
    [InlineData((int)TradeState.Active)]
    [InlineData((int)TradeState.Rejected)]
    public void EnterCreditPending_throws_when_trade_is_not_Submitted(int stateValue)
    {
        var trade = MoveTo((TradeState)stateValue);

        Assert.Throws<InvalidOperationException>(() => trade.EnterCreditPending("system", "n/a"));
    }

    [Fact]
    public void Reject_from_Submitted_moves_to_Rejected_and_publishes_TradeRejected_with_actor_and_reason()
    {
        var trade = MoveTo(TradeState.Submitted);

        var (history, rejected) = trade.Reject("system", "Rejected: credit limit breach not approved");

        Assert.Equal(TradeState.Rejected, trade.State);
        Assert.Equal(TradeState.Rejected, history.State);
        Assert.Equal(trade.Id, rejected.TradeId);
        Assert.Equal(trade.TenantId.Value, rejected.TenantId);
        Assert.Equal("system", rejected.Actor);
        Assert.Equal("Rejected: credit limit breach not approved", rejected.Reason);
    }

    [Fact]
    public void Reject_from_CreditPending_moves_to_Rejected()
    {
        var trade = MoveTo(TradeState.CreditPending);

        var (_, rejected) = trade.Reject("system", "Approval timed out");

        Assert.Equal(TradeState.Rejected, trade.State);
        Assert.Equal("Approval timed out", rejected.Reason);
    }

    [Theory]
    [InlineData((int)TradeState.Draft)]
    [InlineData((int)TradeState.Active)]
    [InlineData((int)TradeState.Rejected)]
    public void Reject_throws_when_trade_is_not_Submitted_or_CreditPending(int stateValue)
    {
        var trade = MoveTo((TradeState)stateValue);

        Assert.Throws<InvalidOperationException>(() => trade.Reject("system", "n/a"));
    }

    [Theory]
    [InlineData((int)TradeState.Draft)]
    [InlineData((int)TradeState.Submitted)]
    [InlineData((int)TradeState.CreditPending)]
    public void Cancel_moves_to_Cancelled_from_Draft_Submitted_or_CreditPending(int stateValue)
    {
        var trade = MoveTo((TradeState)stateValue);

        var history = trade.Cancel("trader", "Cancelled by trader");

        Assert.Equal(TradeState.Cancelled, trade.State);
        Assert.Equal(TradeState.Cancelled, history.State);
        Assert.Equal("trader", history.ChangedBy);
    }

    [Theory]
    [InlineData((int)TradeState.Active)]
    [InlineData((int)TradeState.Rejected)]
    public void Cancel_throws_when_trade_is_Active_or_Rejected(int stateValue)
    {
        var trade = MoveTo((TradeState)stateValue);

        Assert.Throws<InvalidOperationException>(() => trade.Cancel("trader", "n/a"));
    }

    [Fact]
    public void Amend_updates_volume_and_price_keeps_Active_and_publishes_delta()
    {
        var trade = MoveTo(TradeState.Active, volume: 100m, price: 45.5m);
        var versionBeforeAmend = trade.Version;

        var (history, amended) = trade.Amend("trader", "Volume correction", newVolume: 150m, newPrice: 46m);

        Assert.Equal(TradeState.Active, trade.State);
        Assert.Equal(150m, trade.Volume);
        Assert.Equal(46m, trade.Price);
        Assert.Equal(versionBeforeAmend + 1, trade.Version);
        Assert.Equal(TradeState.Active, history.State);

        Assert.Equal(trade.Id, amended.TradeId);
        Assert.Equal(100m, amended.OldVolume);
        Assert.Equal(150m, amended.NewVolume);
        Assert.Equal(46m, amended.Price);
        Assert.Equal(trade.CommodityCode, amended.CommodityCode);
        Assert.Equal(trade.DeliveryPeriod, amended.DeliveryPeriod);
        Assert.Equal(trade.Direction, amended.Direction);
        Assert.Equal("trader", amended.Actor);
        Assert.Equal("Volume correction", amended.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Amend_rejects_non_positive_volume(decimal newVolume)
    {
        var trade = MoveTo(TradeState.Active);

        Assert.Throws<ArgumentOutOfRangeException>(() => trade.Amend("trader", "n/a", newVolume, 46m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Amend_rejects_non_positive_price(decimal newPrice)
    {
        var trade = MoveTo(TradeState.Active);

        Assert.Throws<ArgumentOutOfRangeException>(() => trade.Amend("trader", "n/a", 150m, newPrice));
    }

    [Theory]
    [InlineData((int)TradeState.Draft)]
    [InlineData((int)TradeState.Submitted)]
    [InlineData((int)TradeState.CreditPending)]
    [InlineData((int)TradeState.Rejected)]
    public void Amend_throws_when_trade_is_not_Active(int stateValue)
    {
        var trade = MoveTo((TradeState)stateValue);

        Assert.Throws<InvalidOperationException>(() => trade.Amend("trader", "n/a", 150m, 46m));
    }

    [Fact]
    public void Deliver_moves_Active_to_Delivered_and_publishes_TradeDelivered()
    {
        var trade = MoveTo(TradeState.Active, volume: 100m, price: 45.5m);

        var (history, delivered) = trade.Deliver("trader", "Delivered");

        Assert.Equal(TradeState.Delivered, trade.State);
        Assert.Equal(TradeState.Delivered, history.State);
        Assert.Equal(trade.Id, delivered.TradeId);
        Assert.Equal(trade.TenantId.Value, delivered.TenantId);
        Assert.Equal(trade.CounterpartyId, delivered.CounterpartyId);
        Assert.Equal(100m, delivered.Volume);
        Assert.Equal(45.5m, delivered.Price);
        Assert.Equal("trader", delivered.Actor);
        Assert.Equal("Delivered", delivered.Reason);
    }

    [Theory]
    [InlineData((int)TradeState.Draft)]
    [InlineData((int)TradeState.Submitted)]
    [InlineData((int)TradeState.CreditPending)]
    [InlineData((int)TradeState.Rejected)]
    public void Deliver_throws_when_trade_is_not_Active(int stateValue)
    {
        var trade = MoveTo((TradeState)stateValue);

        Assert.Throws<InvalidOperationException>(() => trade.Deliver("trader", "n/a"));
    }

    // Drives a fresh trade through the real transitions to reach the
    // requested state, rather than constructing it directly — RequireState
    // guards are exercised the same way production code hits them.
    private static Trade MoveTo(TradeState state, decimal volume = 100m, decimal price = 45.5m)
    {
        var trade = CreateDraftTrade(volume, price);
        if (state == TradeState.Draft)
            return trade;

        trade.Submit("trader", "Submitted for credit check");
        if (state == TradeState.Submitted)
            return trade;

        switch (state)
        {
            case TradeState.CreditPending:
                trade.EnterCreditPending("system", "Credit limit breached — awaiting risk approval");
                return trade;
            case TradeState.Active:
                trade.Activate("system", "Within credit limit — approved automatically");
                return trade;
            case TradeState.Rejected:
                trade.Reject("system", "Rejected: credit limit breach not approved");
                return trade;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unreachable state for this helper.");
        }
    }
}
