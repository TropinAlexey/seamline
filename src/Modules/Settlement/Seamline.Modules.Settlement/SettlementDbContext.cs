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
            builder.Property(i => i.Amount).HasColumnName("amount").HasPrecision(18, 2);
            builder.Property(i => i.IssuedAt).HasColumnName("issued_at");

            builder.HasIndex(i => i.TradeId).IsUnique();
            builder.HasQueryFilter(i => i.TenantId == tenantContext.TenantId);
        });
    }
}
