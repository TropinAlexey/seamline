using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

// Append-only, one row per Trade version. No ValidTo column: it's derived at
// query time as the next version's ValidFrom (or "still open" if there is
// none) — see docs/adr/0006-audit-trail-instead-of-event-sourcing.md, the
// paragraph on why storing ValidTo would contradict "never mutated". That
// keeps this table genuinely insert-only, so the seamline_app role's REVOKE
// UPDATE/DELETE (see the migration that creates this table) is never worked
// around by application code, not even accidentally.
internal sealed class TradeHistory
{
    public Guid Id { get; private init; }
    public Guid TradeId { get; private init; }
    public TenantId TenantId { get; private init; }
    public int Version { get; private init; }
    public DateTimeOffset ValidFrom { get; private init; }
    public string ChangedBy { get; private init; } = string.Empty;
    public string ChangeReason { get; private init; } = string.Empty;

    public string CommodityCode { get; private init; } = string.Empty;
    public string DeliveryPeriod { get; private init; } = string.Empty;
    public TradeDirection Direction { get; private init; }
    public decimal Volume { get; private init; }
    public decimal Price { get; private init; }
    public Guid CounterpartyId { get; private init; }
    public TradeState State { get; private init; }

    private TradeHistory() { }

    public static TradeHistory CreateSnapshot(Trade trade, string changedBy, string changeReason)
    {
        return new TradeHistory
        {
            Id = Guid.NewGuid(),
            TradeId = trade.Id,
            TenantId = trade.TenantId,
            Version = trade.Version,
            ValidFrom = DateTimeOffset.UtcNow,
            ChangedBy = changedBy,
            ChangeReason = changeReason,
            CommodityCode = trade.CommodityCode,
            DeliveryPeriod = trade.DeliveryPeriod,
            Direction = trade.Direction,
            Volume = trade.Volume,
            Price = trade.Price,
            CounterpartyId = trade.CounterpartyId,
            State = trade.State
        };
    }
}
