using Seamline.SharedKernel;

namespace Seamline.Modules.MarketData.Internal;

// An index a formula trade can average (ADR-0035). Tenant-owned and seeded
// by migration; the values behind it are synthetic, because real benchmark
// coal indices are proprietary (spec 03, clean room).
internal sealed class PriceIndex : TenantOwnedEntity<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public string CommodityCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    private PriceIndex() { }
}
