using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.Audit.Internal;

internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AuditEvent>(builder =>
        {
            builder.ToTable("audit_event");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.TenantId).HasColumnName("tenant_id");
            builder.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            builder.Property(e => e.Actor).HasColumnName("actor").HasMaxLength(100).IsRequired();
            builder.Property(e => e.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
            builder.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
            builder.Property(e => e.EntityId).HasColumnName("entity_id");
            builder.Property(e => e.Context).HasColumnName("context").HasMaxLength(500);

            builder.HasQueryFilter(e => e.TenantId == tenantContext.TenantId.Value);
        });
    }
}
