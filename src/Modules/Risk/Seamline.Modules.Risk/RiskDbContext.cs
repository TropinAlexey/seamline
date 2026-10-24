using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
    public DbSet<FormulaTrade> FormulaTrades => Set<FormulaTrade>();
    public DbSet<InventoryValuation> InventoryValuations => Set<InventoryValuation>();
    public DbSet<ValuationRun> ValuationRuns => Set<ValuationRun>();

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
            builder.Property(r => r.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(r => r.DeliveryPeriod).HasColumnName("delivery_period").HasMaxLength(7).IsRequired();
            builder.Property(r => r.SignedVolume).HasColumnName("signed_volume").HasPrecision(18, 3);
            builder.Property(r => r.TradePrice).HasColumnName("trade_price").HasPrecision(18, 4);
            builder.OwnsOne(r => r.Pricing, MapPricingTerms);
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
            builder.Property(v => v.TradeId).HasColumnName("trade_id");
            builder.Property(v => v.RunId).HasColumnName("run_id");
            builder.Property(v => v.ValuedAt).HasColumnName("valued_at");

            builder.HasIndex(v => new { v.TenantId, v.CommodityCode, v.DeliveryPeriod, v.ValuedAt });
            builder.HasIndex(v => new { v.TenantId, v.RunId });
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
            builder.Property(s => s.TradeId).HasColumnName("trade_id");
            builder.Property(s => s.RunId).HasColumnName("run_id");
            builder.Property(s => s.ValuedAt).HasColumnName("valued_at");

            builder.HasIndex(s => new { s.TenantId, s.CommodityCode, s.DeliveryPeriod, s.ValuedAt });
            builder.HasIndex(s => new { s.TenantId, s.RunId });
            builder.HasQueryFilter(s => s.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<FormulaTrade>(builder =>
        {
            builder.ToTable("formula_trade");
            builder.HasKey(f => f.Id);
            builder.Property(f => f.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(f => f.TradeId).HasColumnName("trade_id");
            builder.Property(f => f.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(f => f.DeliveryPeriod).HasColumnName("delivery_period").HasMaxLength(7).IsRequired();
            builder.Property(f => f.SignedVolume).HasColumnName("signed_volume").HasPrecision(18, 3);
            builder.OwnsOne(f => f.Pricing, MapPricingTerms);
            builder.Navigation(f => f.Pricing).IsRequired();
            builder.Property(f => f.FinalPrice).HasColumnName("final_price").HasPrecision(18, 4);
            builder.Property(f => f.IsDelivered).HasColumnName("is_delivered");
            builder.Property(f => f.IsOpen).HasColumnName("is_open");

            builder.HasIndex(f => f.TradeId).IsUnique();
            builder.HasQueryFilter(f => f.TenantId == tenantContext.TenantId);
        });

        // Append-only (ADR-0006), one row per storage contract per EOD run.
        modelBuilder.Entity<InventoryValuation>(builder =>
        {
            builder.ToTable("inventory_valuation");
            builder.HasKey(v => v.Id);
            builder.Property(v => v.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(v => v.StorageContractId).HasColumnName("storage_contract_id");
            builder.Property(v => v.Facility).HasColumnName("facility").HasMaxLength(200).IsRequired();
            builder.Property(v => v.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(v => v.DeliveryPeriod).HasColumnName("delivery_period").HasMaxLength(7).IsRequired();
            builder.Property(v => v.Quantity).HasColumnName("quantity").HasPrecision(18, 3);
            builder.Property(v => v.Wacog).HasColumnName("wacog").HasPrecision(18, 4);
            builder.Property(v => v.CurvePrice).HasColumnName("curve_price").HasPrecision(18, 4);
            builder.Property(v => v.CurvePublishedAt).HasColumnName("curve_published_at");
            builder.Property(v => v.UnrealizedAmount).HasColumnName("unrealized_amount").HasPrecision(18, 2);
            builder.Property(v => v.RunId).HasColumnName("run_id");
            builder.Property(v => v.ValuedAt).HasColumnName("valued_at");

            builder.HasIndex(v => new { v.TenantId, v.RunId });
            builder.HasQueryFilter(v => v.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ValuationRun>(builder =>
        {
            builder.ToTable("valuation_run");
            builder.HasKey(r => r.Id);
            builder.Property(r => r.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(r => r.RunId).HasColumnName("run_id");
            builder.Property(r => r.ValuedAt).HasColumnName("valued_at");

            builder.HasIndex(r => new { r.TenantId, r.RunId }).IsUnique();
            builder.HasIndex(r => new { r.TenantId, r.ValuedAt });
            builder.HasQueryFilter(r => r.TenantId == tenantContext.TenantId);
        });
    }

    // The same four columns wherever pricing terms are stored in Risk.
    private static void MapPricingTerms<TOwner>(OwnedNavigationBuilder<TOwner, IndexPricingTerms> terms)
        where TOwner : class
    {
        terms.Property(p => p.IndexCode).HasColumnName("pricing_index_code").HasMaxLength(20).IsRequired();
        terms.Property(p => p.WindowFrom).HasColumnName("pricing_window_from");
        terms.Property(p => p.WindowTo).HasColumnName("pricing_window_to");
        terms.Property(p => p.Premium).HasColumnName("pricing_premium").HasPrecision(18, 4);
    }
}
