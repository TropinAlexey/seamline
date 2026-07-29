using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.MarketData.Internal;

internal sealed class MarketDataDbContext(DbContextOptions<MarketDataDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "marketdata";

    public DbSet<PriceCurvePoint> PriceCurvePoints => Set<PriceCurvePoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<PriceCurvePoint>(builder =>
        {
            builder.ToTable("price_curve_point");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(p => p.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(p => p.DeliveryPeriod).HasColumnName("delivery_period").HasMaxLength(7).IsRequired();
            builder.Property(p => p.Price).HasColumnName("price").HasPrecision(18, 4);
            builder.Property(p => p.PublishedAt).HasColumnName("published_at");

            builder.HasIndex(p => new { p.TenantId, p.CommodityCode, p.DeliveryPeriod }).IsUnique();
            builder.HasQueryFilter(p => p.TenantId == tenantContext.TenantId);
        });
    }
}
