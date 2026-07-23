using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.Reference.Internal;

internal sealed class ReferenceDbContext(DbContextOptions<ReferenceDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "reference";

    public DbSet<Counterparty> Counterparties => Set<Counterparty>();

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
    }
}
