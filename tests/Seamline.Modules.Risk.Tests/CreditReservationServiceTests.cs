using Microsoft.EntityFrameworkCore;
using Seamline.Modules.MarketData.Contracts;
using Seamline.Modules.Reference.Contracts;
using Seamline.Modules.Risk.Contracts;
using Seamline.Modules.Risk.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Tests;

public class CreditReservationServiceTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Guid CounterpartyId = Guid.NewGuid();

    [Fact]
    public async Task TryReserveAsync_reserves_when_within_credit_limit()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 1_000_000m);

        var result = await service.TryReserveAsync(
            Tenant.Value, CounterpartyId, Guid.NewGuid(), "POWER", "2026-08", 100m, 45.50m);

        Assert.Equal(CreditReservationOutcome.Reserved, result.Outcome);
        Assert.Equal(0m, result.ExistingExposure);
        Assert.Equal(1_000_000m, result.CreditLimit);

        var reservation = Assert.Single(db.CreditReservations);
        Assert.Equal(CreditReservationStatus.Reserved, reservation.Status);
    }

    [Fact]
    public async Task TryReserveAsync_breaches_when_exposure_exceeds_credit_limit()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 1_000m);

        var result = await service.TryReserveAsync(
            Tenant.Value, CounterpartyId, Guid.NewGuid(), "POWER", "2026-08", 100m, 500m);

        Assert.Equal(CreditReservationOutcome.Breached, result.Outcome);

        var reservation = Assert.Single(db.CreditReservations);
        Assert.Equal(CreditReservationStatus.Provisional, reservation.Status);
    }

    [Fact]
    public async Task TryReserveAsync_sums_existing_exposure_for_the_same_counterparty()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 10_000m);

        await service.TryReserveAsync(
            Tenant.Value, CounterpartyId, Guid.NewGuid(), "POWER", "2026-08", 100m, 60m);

        var result = await service.TryReserveAsync(
            Tenant.Value, CounterpartyId, Guid.NewGuid(), "POWER", "2026-09", 100m, 50m);

        Assert.Equal(CreditReservationOutcome.Breached, result.Outcome);
    }

    [Fact]
    public async Task TryReserveAsync_ignores_released_reservations_when_summing_exposure()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 10_000m);

        var firstTradeId = Guid.NewGuid();
        await service.TryReserveAsync(
            Tenant.Value, CounterpartyId, firstTradeId, "POWER", "2026-08", 100m, 60m);
        await service.ReleaseAsync(firstTradeId);

        var result = await service.TryReserveAsync(
            Tenant.Value, CounterpartyId, Guid.NewGuid(), "POWER", "2026-09", 100m, 80m);

        Assert.Equal(CreditReservationOutcome.Reserved, result.Outcome);
        Assert.Equal(0m, result.ExistingExposure);
    }

    [Fact]
    public async Task TryReserveAsync_uses_mtm_when_curve_exists()
    {
        var db = CreateDbContext();
        var curveDirectory = new FakeCurvePointDirectory(50m);
        var service = new CreditReservationService(
            db,
            new FakeCounterpartyDirectory(new CounterpartyRef(CounterpartyId, "Acme Energy", 1_000m)),
            curveDirectory);

        // Buy 100 @ 45, curve at 50 → MtM = (50-45)*100 = 500 (positive, exposed)
        var result = await service.TryReserveAsync(
            Tenant.Value, CounterpartyId, Guid.NewGuid(), "POWER", "2026-08", 100m, 45m);

        Assert.Equal(CreditReservationOutcome.Reserved, result.Outcome);
        var reservation = Assert.Single(db.CreditReservations);
        Assert.Equal(500m, reservation.Amount);
    }

    [Fact]
    public async Task TryReserveAsync_clamps_negative_mtm_to_zero()
    {
        var db = CreateDbContext();
        var curveDirectory = new FakeCurvePointDirectory(40m);
        var service = new CreditReservationService(
            db,
            new FakeCounterpartyDirectory(new CounterpartyRef(CounterpartyId, "Acme Energy", 1_000m)),
            curveDirectory);

        // Buy 100 @ 45, curve at 40 → MtM = (40-45)*100 = -500 → clamped to 0
        var result = await service.TryReserveAsync(
            Tenant.Value, CounterpartyId, Guid.NewGuid(), "POWER", "2026-08", 100m, 45m);

        Assert.Equal(CreditReservationOutcome.Reserved, result.Outcome);
        var reservation = Assert.Single(db.CreditReservations);
        Assert.Equal(0m, reservation.Amount);
    }

    [Fact]
    public async Task TryReserveAsync_falls_back_to_notional_when_no_curve()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 1_000_000m);

        var result = await service.TryReserveAsync(
            Tenant.Value, CounterpartyId, Guid.NewGuid(), "POWER", "2026-08", 100m, 45m);

        Assert.Equal(CreditReservationOutcome.Reserved, result.Outcome);
        var reservation = Assert.Single(db.CreditReservations);
        Assert.Equal(4_500m, reservation.Amount);
    }

    [Fact]
    public async Task TryReserveAsync_throws_when_the_counterparty_does_not_exist()
    {
        var db = CreateDbContext();
        var service = new CreditReservationService(
            db, new FakeCounterpartyDirectory(counterparty: null), new FakeCurvePointDirectory(null));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TryReserveAsync(
                Tenant.Value, CounterpartyId, Guid.NewGuid(), "POWER", "2026-08", 100m, 45m));
    }

    [Fact]
    public async Task FinalizeAsync_marks_the_reservation_for_the_trade_as_reserved()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 1_000m);
        var tradeId = Guid.NewGuid();
        await service.TryReserveAsync(
            Tenant.Value, CounterpartyId, tradeId, "POWER", "2026-08", 100m, 500m);

        await service.FinalizeAsync(tradeId);

        var reservation = Assert.Single(db.CreditReservations);
        Assert.Equal(CreditReservationStatus.Reserved, reservation.Status);
    }

    [Fact]
    public async Task ReleaseAsync_marks_the_reservation_for_the_trade_as_released()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 1_000_000m);
        var tradeId = Guid.NewGuid();
        await service.TryReserveAsync(
            Tenant.Value, CounterpartyId, tradeId, "POWER", "2026-08", 100m, 45m);

        await service.ReleaseAsync(tradeId);

        var reservation = Assert.Single(db.CreditReservations);
        Assert.Equal(CreditReservationStatus.Released, reservation.Status);
    }

    [Fact]
    public async Task FinalizeAsync_throws_when_no_reservation_exists_for_the_trade()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 1_000m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinalizeAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ReleaseAsync_throws_when_no_reservation_exists_for_the_trade()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 1_000m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReleaseAsync(Guid.NewGuid()));
    }

    private static CreditReservationService CreateService(RiskDbContext db, decimal creditLimit) =>
        new(db,
            new FakeCounterpartyDirectory(new CounterpartyRef(CounterpartyId, "Acme Energy", creditLimit)),
            new FakeCurvePointDirectory(null));

    private static RiskDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RiskDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Tenant);
        return new RiskDbContext(options, tenantContext);
    }

    private sealed class FakeCounterpartyDirectory(CounterpartyRef? counterparty) : ICounterpartyDirectory
    {
        public Task<CounterpartyRef?> FindAsync(Guid counterpartyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(counterparty);
    }

    private sealed class FakeCurvePointDirectory(decimal? price) : ICurvePointDirectory
    {
        public Task<CurvePointRef?> FindAsync(string commodityCode, string deliveryPeriod, CancellationToken cancellationToken = default) =>
            Task.FromResult(price.HasValue
                ? new CurvePointRef(commodityCode, deliveryPeriod, price.Value, DateTimeOffset.UtcNow)
                : null);
    }
}
