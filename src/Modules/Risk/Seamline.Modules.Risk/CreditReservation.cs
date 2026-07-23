using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

internal enum CreditReservationStatus
{
    Provisional = 1,
    Reserved = 2,
    Released = 3
}

// The one thing Risk writes that nobody else entered: its own credit
// decisions. Positions and valuations are derived from events; this is not.
internal sealed class CreditReservation : TenantOwnedEntity<Guid>
{
    public Guid CounterpartyId { get; private set; }
    public Guid TradeId { get; private set; }
    public decimal Amount { get; private set; }
    public CreditReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CreditReservation() { }

    public static CreditReservation Create(TenantId tenantId, Guid counterpartyId, Guid tradeId, decimal amount, CreditReservationStatus status)
    {
        return new CreditReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CounterpartyId = counterpartyId,
            TradeId = tradeId,
            Amount = amount,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Finalize() => Status = CreditReservationStatus.Reserved;

    public void Release() => Status = CreditReservationStatus.Released;
}
