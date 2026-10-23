using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.Settlement.Internal;

internal sealed class SettlementDbContext(DbContextOptions<SettlementDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "settlement";

    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Invoice>(builder =>
        {
            builder.ToTable("invoice");
            builder.HasKey(i => i.Id);
            builder.Property(i => i.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(i => i.TradeId).HasColumnName("trade_id");
            builder.Property(i => i.CounterpartyId).HasColumnName("counterparty_id");
            builder.Property(i => i.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
            builder.Property(i => i.Quantity).HasColumnName("quantity").HasPrecision(18, 3);
            builder.Property(i => i.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 4);
            builder.Property(i => i.Amount).HasColumnName("amount").HasPrecision(18, 2);
            builder.Property(i => i.Balance).HasColumnName("balance").HasPrecision(18, 2);
            builder.Property(i => i.IssuedAt).HasColumnName("issued_at");

            // One invoice per kind per trade: a redelivered event cannot
            // issue a second provisional or final (ADR-0034).
            builder.HasIndex(i => new { i.TradeId, i.Kind }).IsUnique();
            builder.HasQueryFilter(i => i.TenantId == tenantContext.TenantId);
        });
    }
}
