using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.Reference.Internal;

internal sealed class ReferenceDbContext(DbContextOptions<ReferenceDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "reference";

    public DbSet<Counterparty> Counterparties => Set<Counterparty>();
    public DbSet<Commodity> Commodities => Set<Commodity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Counterparty>(builder =>
        {
            builder.ToTable("counterparty");
            builder.HasKey(c => c.Id);
            builder.Property(c => c.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            builder.Property(c => c.CreditLimit).HasColumnName("credit_limit").HasPrecision(18, 2);

            builder.HasQueryFilter(c => c.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Commodity>(builder =>
        {
            builder.ToTable("commodity");
            builder.HasKey(c => c.Id);
            builder.Property(c => c.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(c => c.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
            builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            builder.Property(c => c.Unit).HasColumnName("unit");
            builder.Property(c => c.DeliveryModel).HasColumnName("delivery_model");
            builder.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();

            builder.OwnsOne(c => c.QualitySpec, spec =>
            {
                spec.Property(q => q.BaseNarKcalKg).HasColumnName("base_nar_kcal_kg").HasPrecision(7, 1);
                spec.Property(q => q.RejectBelowNarKcalKg).HasColumnName("reject_below_nar_kcal_kg").HasPrecision(7, 1);
                spec.Property(q => q.MaxSulphurPct).HasColumnName("max_sulphur_pct").HasPrecision(5, 2);
                spec.Property(q => q.MaxAshPct).HasColumnName("max_ash_pct").HasPrecision(5, 2);
            });

            builder.HasQueryFilter(c => c.TenantId == tenantContext.TenantId);
        });
    }
}
