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
    public string CommodityCode { get; private set; } = string.Empty;
    public string DeliveryPeriod { get; private set; } = string.Empty;
    public decimal SignedVolume { get; private set; }
    public decimal TradePrice { get; private set; }
    public decimal Amount { get; private set; }
    public CreditReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CreditReservation() { }

    public static CreditReservation Create(
        TenantId tenantId, Guid counterpartyId, Guid tradeId,
        string commodityCode, string deliveryPeriod,
        decimal signedVolume, decimal tradePrice,
        decimal exposureAmount, CreditReservationStatus status)
    {
        return new CreditReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CounterpartyId = counterpartyId,
            TradeId = tradeId,
            CommodityCode = commodityCode,
            DeliveryPeriod = deliveryPeriod,
            SignedVolume = signedVolume,
            TradePrice = tradePrice,
            Amount = exposureAmount,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkReserved() => Status = CreditReservationStatus.Reserved;

    public void Release() => Status = CreditReservationStatus.Released;

    // An amended trade keeps its reservation but at the new volume/price —
    // otherwise exposure would keep being measured on terms that no longer
    // exist. Amend deliberately doesn't re-run the credit check (Trade.Amend),
    // so this can't be refused; it only keeps the numbers truthful.
    public void Amend(decimal signedVolume, decimal tradePrice, decimal exposureAmount)
    {
        SignedVolume = signedVolume;
        TradePrice = tradePrice;
        Amount = exposureAmount;
    }
}

internal static class CreditReservationQueries
{
    public static IQueryable<CreditReservation> HoldingCredit(this IQueryable<CreditReservation> reservations, Guid counterpartyId) =>
        reservations.Where(r => r.CounterpartyId == counterpartyId &&
                                (r.Status == CreditReservationStatus.Reserved || r.Status == CreditReservationStatus.Provisional));
}
