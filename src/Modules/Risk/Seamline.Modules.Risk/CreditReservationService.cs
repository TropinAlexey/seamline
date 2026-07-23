using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Reference.Contracts;
using Seamline.Modules.Risk.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

internal sealed class CreditReservationService(RiskDbContext db, ICounterpartyDirectory counterpartyDirectory) : ICreditReservationService
{
    public async Task<CreditReservationResult> TryReserveAsync(
        Guid tenantId, Guid counterpartyId, Guid tradeId, decimal notionalAmount, CancellationToken cancellationToken = default)
    {
        var counterparty = await counterpartyDirectory.FindAsync(counterpartyId, cancellationToken)
            ?? throw new InvalidOperationException($"Counterparty {counterpartyId} not found.");

        // Notional (Volume x Price), not mark-to-market: there's no
        // Valuation.Worker yet to produce (forward_price - trade_price) x
        // volume. Real MtM-based exposure replaces this in Phase 2 — see
        // CTRM_Domain notes on the actual formula.
        var existingExposure = await db.CreditReservations
            .Where(r => r.CounterpartyId == counterpartyId &&
                        (r.Status == CreditReservationStatus.Reserved || r.Status == CreditReservationStatus.Provisional))
            .SumAsync(r => r.Amount, cancellationToken);

        var projectedExposure = existingExposure + notionalAmount;
        var tenant = new TenantId(tenantId);

        var outcome = projectedExposure <= counterparty.CreditLimit
            ? CreditReservationOutcome.Reserved
            : CreditReservationOutcome.Breached;

        var status = outcome == CreditReservationOutcome.Reserved
            ? CreditReservationStatus.Reserved
            : CreditReservationStatus.Provisional;

        db.CreditReservations.Add(CreditReservation.Create(tenant, counterpartyId, tradeId, notionalAmount, status));
        await db.SaveChangesAsync(cancellationToken);

        return new CreditReservationResult(outcome, existingExposure, counterparty.CreditLimit);
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

    private async Task<CreditReservation> RequireReservationAsync(Guid tradeId, CancellationToken cancellationToken)
    {
        return await db.CreditReservations.FirstOrDefaultAsync(r => r.TradeId == tradeId, cancellationToken)
            ?? throw new InvalidOperationException($"No credit reservation found for trade {tradeId}.");
    }
}
