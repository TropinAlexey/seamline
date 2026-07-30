using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

internal sealed class RiskDbContext(DbContextOptions<RiskDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "risk";

    public DbSet<Position> Positions => Set<Position>();
    public DbSet<CreditReservation> CreditReservations => Set<CreditReservation>();
    public DbSet<ValuationSnapshot> ValuationSnapshots => Set<ValuationSnapshot>();
    public DbSet<StressScenarioResult> StressScenarioResults => Set<StressScenarioResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Position>(builder =>
        {
            builder.ToTable("position");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(p => p.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(p => p.DeliveryPeriod).HasColumnName("delivery_period").HasMaxLength(7).IsRequired();
            builder.Property(p => p.NetVolume).HasColumnName("net_volume").HasPrecision(18, 3);
            builder.Property(p => p.WeightedAvgPrice).HasColumnName("weighted_avg_price").HasPrecision(18, 4);

            builder.HasIndex(p => new { p.TenantId, p.CommodityCode, p.DeliveryPeriod }).IsUnique();
            builder.HasQueryFilter(p => p.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<CreditReservation>(builder =>
        {
            builder.ToTable("credit_reservation");
            builder.HasKey(r => r.Id);
            builder.Property(r => r.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(r => r.CounterpartyId).HasColumnName("counterparty_id");
            builder.Property(r => r.TradeId).HasColumnName("trade_id");
            builder.Property(r => r.Amount).HasColumnName("amount").HasPrecision(18, 4);
            builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            builder.Property(r => r.CreatedAt).HasColumnName("created_at");

            builder.HasIndex(r => r.TradeId);
            builder.HasQueryFilter(r => r.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ValuationSnapshot>(builder =>
        {
            builder.ToTable("valuation_snapshot");
            builder.HasKey(v => v.Id);
            builder.Property(v => v.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(v => v.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(v => v.DeliveryPeriod).HasColumnName("delivery_period").HasMaxLength(7).IsRequired();
            builder.Property(v => v.NetVolume).HasColumnName("net_volume").HasPrecision(18, 3);
            builder.Property(v => v.WeightedAvgPrice).HasColumnName("weighted_avg_price").HasPrecision(18, 4);
            builder.Property(v => v.CurvePrice).HasColumnName("curve_price").HasPrecision(18, 4);
            builder.Property(v => v.CurvePublishedAt).HasColumnName("curve_published_at");
            builder.Property(v => v.MtmAmount).HasColumnName("mtm_amount").HasPrecision(18, 2);
            builder.Property(v => v.ValuedAt).HasColumnName("valued_at");

            builder.HasIndex(v => new { v.TenantId, v.CommodityCode, v.DeliveryPeriod, v.ValuedAt });
            builder.HasQueryFilter(v => v.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<StressScenarioResult>(builder =>
        {
            builder.ToTable("stress_scenario_result");
            builder.HasKey(s => s.Id);
            builder.Property(s => s.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(s => s.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(s => s.DeliveryPeriod).HasColumnName("delivery_period").HasMaxLength(7).IsRequired();
            builder.Property(s => s.NetVolume).HasColumnName("net_volume").HasPrecision(18, 3);
            builder.Property(s => s.WeightedAvgPrice).HasColumnName("weighted_avg_price").HasPrecision(18, 4);
            builder.Property(s => s.ScenarioType).HasColumnName("scenario_type").HasConversion<string>().HasMaxLength(30);
            builder.Property(s => s.ShockPercentage).HasColumnName("shock_percentage").HasPrecision(6, 2);
            builder.Property(s => s.ShockedPrice).HasColumnName("shocked_price").HasPrecision(18, 4);
            builder.Property(s => s.MtmAmount).HasColumnName("mtm_amount").HasPrecision(18, 2);
            builder.Property(s => s.ValuedAt).HasColumnName("valued_at");

            builder.HasIndex(s => new { s.TenantId, s.CommodityCode, s.DeliveryPeriod, s.ValuedAt });
            builder.HasQueryFilter(s => s.TenantId == tenantContext.TenantId);
        });
    }
}
