using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

internal sealed class RiskDbContext(DbContextOptions<RiskDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "risk";

    public DbSet<Position> Positions => Set<Position>();
    public DbSet<CreditReservation> CreditReservations => Set<CreditReservation>();

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
    }
}
