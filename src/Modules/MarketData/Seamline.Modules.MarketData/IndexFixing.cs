using Seamline.SharedKernel;

namespace Seamline.Modules.MarketData.Internal;

// One published value of an index for one pricing day. Unlike a curve point
// (refreshed in place, ADR-0012/0018) a fixing is a settlement input: an
// invoice is computed from it, so it is append-only and never overwritten
// (ADR-0035). seamline_app holds SELECT/INSERT only on the table.
internal sealed class IndexFixing : TenantOwnedEntity<Guid>
{
    public string IndexCode { get; private set; } = string.Empty;
    public DateOnly FixingDate { get; private set; }
    public decimal Value { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }
    public string PublishedBy { get; private set; } = string.Empty;

    private IndexFixing() { }

    public static IndexFixing Publish(TenantId tenantId, string indexCode, DateOnly fixingDate, decimal value, string publishedBy)
    {
        if (!PricingCalendar.IsPricingDay(fixingDate))
            throw new ArgumentException($"{fixingDate:yyyy-MM-dd} is a weekend: indices fix on weekdays only.", nameof(fixingDate));
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Fixing value must be positive.");

        return new IndexFixing
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            IndexCode = indexCode,
            FixingDate = fixingDate,
            Value = value,
            PublishedAt = DateTimeOffset.UtcNow,
            PublishedBy = publishedBy
        };
    }
}
