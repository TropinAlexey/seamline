using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Reference.Contracts;
using Seamline.Modules.Risk.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

internal sealed class CreditReservationService(
    RiskDbContext db,
    ICounterpartyDirectory counterpartyDirectory,
    CreditExposureCalculator exposureCalculator) : ICreditReservationService
{
    public async Task<CreditReservationResult> TryReserveAsync(
        Guid tenantId, Guid counterpartyId, Guid tradeId,
        string commodityCode, string deliveryPeriod,
        decimal signedVolume, decimal tradePrice,
        CancellationToken cancellationToken = default)
    {
        var counterparty = await counterpartyDirectory.FindAsync(counterpartyId, cancellationToken)
            ?? throw new InvalidOperationException($"Counterparty {counterpartyId} not found.");

        var useTransaction = db.Database.IsNpgsql();
        var transaction = useTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            // ponytail: advisory lock serializes credit checks per counterparty —
            // prevents TOCTOU race where two concurrent submits both pass the limit.
            // Upgrade to SELECT FOR UPDATE on a ledger row if lock contention shows up in traces.
            if (useTransaction)
                await AcquireCounterpartyLockAsync(tenantId, counterpartyId, cancellationToken);

            var activeReservations = await db.CreditReservations
                .HoldingCredit(counterpartyId)
                .ToListAsync(cancellationToken);

            var existingExposure = await exposureCalculator.ForReservationsAsync(activeReservations, cancellationToken);
            var newTradeExposure = await exposureCalculator.ForTradeAsync(commodityCode, deliveryPeriod, signedVolume, tradePrice, cancellationToken);
            var projectedExposure = existingExposure + newTradeExposure;
            var tenant = new TenantId(tenantId);

            var outcome = projectedExposure <= counterparty.CreditLimit
                ? CreditReservationOutcome.Reserved
                : CreditReservationOutcome.Breached;

            var status = outcome == CreditReservationOutcome.Reserved
                ? CreditReservationStatus.Reserved
                : CreditReservationStatus.Provisional;

            db.CreditReservations.Add(CreditReservation.Create(
                tenant, counterpartyId, tradeId,
                commodityCode, deliveryPeriod, signedVolume, tradePrice,
                newTradeExposure, status));
            await db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return new CreditReservationResult(outcome, existingExposure, counterparty.CreditLimit);
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    public async Task FinalizeAsync(Guid tradeId, CancellationToken cancellationToken = default)
    {
        var reservation = await RequireReservationAsync(tradeId, cancellationToken);
        reservation.MarkReserved();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(Guid tradeId, CancellationToken cancellationToken = default)
    {
        var reservation = await RequireReservationAsync(tradeId, cancellationToken);
        reservation.Release();
        await db.SaveChangesAsync(cancellationToken);
    }

    // ponytail: Guid.GetHashCode() is stable within a .NET version but not across major versions.
    // Fine for transient advisory locks; if stability matters, use first 4 bytes of the GUID instead.
    private async Task AcquireCounterpartyLockAsync(Guid tenantId, Guid counterpartyId, CancellationToken cancellationToken)
    {
        var lockKey = ((long)tenantId.GetHashCode() << 32) | (uint)counterpartyId.GetHashCode();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})", new object[] { lockKey }, cancellationToken);
    }

    private async Task<CreditReservation> RequireReservationAsync(Guid tradeId, CancellationToken cancellationToken)
    {
        return await db.CreditReservations.FirstOrDefaultAsync(r => r.TradeId == tradeId, cancellationToken)
            ?? throw new InvalidOperationException($"No credit reservation found for trade {tradeId}.");
    }
}
