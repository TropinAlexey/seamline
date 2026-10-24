using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.Inventory.Internal;

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "inventory";

    public DbSet<StorageContract> StorageContracts => Set<StorageContract>();
    public DbSet<InventoryMovement> Movements => Set<InventoryMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<StorageContract>(builder =>
        {
            builder.ToTable("storage_contract");
            builder.HasKey(c => c.Id);
            builder.Property(c => c.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(c => c.Facility).HasColumnName("facility").HasMaxLength(200).IsRequired();
            builder.Property(c => c.CommodityCode).HasColumnName("commodity_code").HasMaxLength(20).IsRequired();
            builder.Property(c => c.Capacity).HasColumnName("capacity").HasPrecision(18, 3);
            builder.Property(c => c.MaxInjectionPerDay).HasColumnName("max_injection_per_day").HasPrecision(18, 3);
            builder.Property(c => c.MaxWithdrawalPerDay).HasColumnName("max_withdrawal_per_day").HasPrecision(18, 3);
            builder.Property(c => c.ValidFrom).HasColumnName("valid_from");
            builder.Property(c => c.ValidTo).HasColumnName("valid_to");
            builder.Property(c => c.Quantity).HasColumnName("quantity").HasPrecision(18, 3);
            builder.Property(c => c.Wacog).HasColumnName("wacog").HasPrecision(28, StorageContract.WacogScale);
            builder.Property(c => c.Version).HasColumnName("version");

            builder.HasQueryFilter(c => c.TenantId == tenantContext.TenantId);
        });

        // Append-only (ADR-0006): SELECT/INSERT only for seamline_app.
        modelBuilder.Entity<InventoryMovement>(builder =>
        {
            builder.ToTable("inventory_movement");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(m => m.StorageContractId).HasColumnName("storage_contract_id");
            builder.Property(m => m.GasDay).HasColumnName("gas_day");
            builder.Property(m => m.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
            builder.Property(m => m.Quantity).HasColumnName("quantity").HasPrecision(18, 3);
            builder.Property(m => m.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 4);
            builder.Property(m => m.TradeId).HasColumnName("trade_id");
            builder.Property(m => m.QuantityAfter).HasColumnName("quantity_after").HasPrecision(18, 3);
            builder.Property(m => m.WacogAfter).HasColumnName("wacog_after").HasPrecision(18, 4);
            builder.Property(m => m.Version).HasColumnName("version");
            builder.Property(m => m.Actor).HasColumnName("actor").HasMaxLength(100).IsRequired();
            builder.Property(m => m.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
            builder.Property(m => m.RecordedAt).HasColumnName("recorded_at");

            builder.HasIndex(m => new { m.StorageContractId, m.Version }).IsUnique();
            builder.HasIndex(m => new { m.StorageContractId, m.GasDay });
            builder.HasQueryFilter(m => m.TenantId == tenantContext.TenantId);
        });
    }
}
