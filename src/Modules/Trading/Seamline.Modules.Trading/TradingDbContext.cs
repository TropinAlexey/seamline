using MassTransit;
using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

internal sealed class TradingDbContext(DbContextOptions<TradingDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "trading";

    public DbSet<Trade> Trades => Set<Trade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Trade>(builder =>
        {
            builder.ToTable("trade");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(t => t.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(t => t.DeliveryPeriod).HasColumnName("delivery_period").HasMaxLength(7).IsRequired();
            builder.Property(t => t.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(10);
            builder.Property(t => t.Volume).HasColumnName("volume").HasPrecision(18, 3);
            builder.Property(t => t.Price).HasColumnName("price").HasPrecision(18, 4);
            builder.Property(t => t.CounterpartyId).HasColumnName("counterparty_id");
            builder.Property(t => t.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20);

            builder.HasQueryFilter(t => t.TenantId == tenantContext.TenantId);
        });

        // Transactional outbox: TradeConfirmed is written to this schema in the
        // same transaction as the trade's state change, then dispatched by
        // MassTransit's bus outbox delivery service. See ADR-0004 (outbox).
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
