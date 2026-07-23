namespace Seamline.Modules.Risk.Contracts;

// Synchronous, in-process command contract — the one write path Risk
// exposes. Risk owns no data anyone else enters; the only thing it ever
// writes is its own credit decisions. See ADR-0008.
public interface ICreditReservationService
{
    Task<CreditReservationResult> TryReserveAsync(
        Guid tenantId, Guid counterpartyId, Guid tradeId, decimal notionalAmount, CancellationToken cancellationToken = default);

    Task FinalizeAsync(Guid tradeId, CancellationToken cancellationToken = default);

    Task ReleaseAsync(Guid tradeId, CancellationToken cancellationToken = default);
}
