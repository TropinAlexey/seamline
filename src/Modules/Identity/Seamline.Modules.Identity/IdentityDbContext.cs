using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.Identity.Internal;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(builder =>
        {
            builder.ToTable("user");
            builder.HasKey(u => u.Id);
            builder.Property(u => u.TenantId)
                .HasConversion(t => t.Value, v => new TenantId(v))
                .HasColumnName("tenant_id");
            builder.Property(u => u.Login).HasColumnName("login");
            builder.Property(u => u.PasswordHash).HasColumnName("password_hash");
            builder.Property(u => u.Role).HasColumnName("role");
            builder.Property(u => u.CreatedAt).HasColumnName("created_at");

            builder.HasIndex(u => new { u.TenantId, u.Login }).IsUnique();
            builder.HasQueryFilter(u => u.TenantId == tenantContext.TenantId);
        });
    }
}
