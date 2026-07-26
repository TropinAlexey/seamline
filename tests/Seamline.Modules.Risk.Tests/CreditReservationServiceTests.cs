using Microsoft.EntityFrameworkCore;
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

        var result = await service.TryReserveAsync(Tenant.Value, CounterpartyId, Guid.NewGuid(), 4_550m);

        Assert.Equal(CreditReservationOutcome.Reserved, result.Outcome);
        Assert.Equal(0m, result.ExistingExposure);
        Assert.Equal(1_000_000m, result.CreditLimit);

        var reservation = Assert.Single(db.CreditReservations);
        Assert.Equal(CreditReservationStatus.Reserved, reservation.Status);
    }

    [Fact]
    public async Task TryReserveAsync_breaches_when_notional_exceeds_credit_limit()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 1_000m);

        var result = await service.TryReserveAsync(Tenant.Value, CounterpartyId, Guid.NewGuid(), 50_000m);

        Assert.Equal(CreditReservationOutcome.Breached, result.Outcome);

        var reservation = Assert.Single(db.CreditReservations);
        Assert.Equal(CreditReservationStatus.Provisional, reservation.Status);
    }

    [Fact]
    public async Task TryReserveAsync_sums_existing_reserved_and_provisional_exposure_for_the_same_counterparty()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 10_000m);

        // First reservation eats 6,000 of the 10,000 limit.
        await service.TryReserveAsync(Tenant.Value, CounterpartyId, Guid.NewGuid(), 6_000m);

        // A second trade for the same counterparty only has 4,000 of
        // headroom left — 5,000 breaches.
        var result = await service.TryReserveAsync(Tenant.Value, CounterpartyId, Guid.NewGuid(), 5_000m);

        Assert.Equal(CreditReservationOutcome.Breached, result.Outcome);
        Assert.Equal(6_000m, result.ExistingExposure);
    }

    [Fact]
    public async Task TryReserveAsync_ignores_released_reservations_when_summing_exposure()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 10_000m);

        var firstTradeId = Guid.NewGuid();
        await service.TryReserveAsync(Tenant.Value, CounterpartyId, firstTradeId, 6_000m);
        await service.ReleaseAsync(firstTradeId);

        // The released reservation no longer counts — full 10,000 is free again.
        var result = await service.TryReserveAsync(Tenant.Value, CounterpartyId, Guid.NewGuid(), 8_000m);

        Assert.Equal(CreditReservationOutcome.Reserved, result.Outcome);
        Assert.Equal(0m, result.ExistingExposure);
    }

    [Fact]
    public async Task TryReserveAsync_throws_when_the_counterparty_does_not_exist()
    {
        var db = CreateDbContext();
        var service = new CreditReservationService(db, new FakeCounterpartyDirectory(counterparty: null));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TryReserveAsync(Tenant.Value, CounterpartyId, Guid.NewGuid(), 1_000m));
    }

    [Fact]
    public async Task FinalizeAsync_marks_the_reservation_for_the_trade_as_reserved()
    {
        var db = CreateDbContext();
        var service = CreateService(db, creditLimit: 1_000m);
        var tradeId = Guid.NewGuid();
        await service.TryReserveAsync(Tenant.Value, CounterpartyId, tradeId, 50_000m); // breaches -> Provisional

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
        await service.TryReserveAsync(Tenant.Value, CounterpartyId, tradeId, 4_550m);

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
        new(db, new FakeCounterpartyDirectory(new CounterpartyRef(CounterpartyId, "Acme Energy", creditLimit)));

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
}
