using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.MarketData.Internal;

internal sealed class MarketDataDbContext(DbContextOptions<MarketDataDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "marketdata";

    public DbSet<PriceCurvePoint> PriceCurvePoints => Set<PriceCurvePoint>();
    public DbSet<PriceIndex> PriceIndices => Set<PriceIndex>();
    public DbSet<IndexFixing> IndexFixings => Set<IndexFixing>();

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

        modelBuilder.Entity<PriceIndex>(builder =>
        {
            builder.ToTable("price_index");
            builder.HasKey(i => i.Id);
            builder.Property(i => i.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(i => i.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
            builder.Property(i => i.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(i => i.Name).HasColumnName("name").HasMaxLength(200).IsRequired();

            builder.HasIndex(i => new { i.TenantId, i.Code }).IsUnique();
            builder.HasQueryFilter(i => i.TenantId == tenantContext.TenantId);
        });

        // Append-only (ADR-0035): one value per index and day, never
        // overwritten — the unique index turns a second publish into a 409.
        modelBuilder.Entity<IndexFixing>(builder =>
        {
            builder.ToTable("index_fixing");
            builder.HasKey(f => f.Id);
            builder.Property(f => f.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(f => f.IndexCode).HasColumnName("index_code").HasMaxLength(20).IsRequired();
            builder.Property(f => f.FixingDate).HasColumnName("fixing_date");
            builder.Property(f => f.Value).HasColumnName("value").HasPrecision(18, 4);
            builder.Property(f => f.PublishedAt).HasColumnName("published_at");
            builder.Property(f => f.PublishedBy).HasColumnName("published_by").HasMaxLength(100).IsRequired();

            builder.HasIndex(f => new { f.TenantId, f.IndexCode, f.FixingDate }).IsUnique();
            builder.HasQueryFilter(f => f.TenantId == tenantContext.TenantId);
        });
    }
}
