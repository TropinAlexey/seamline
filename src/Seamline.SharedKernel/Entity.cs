namespace Seamline.SharedKernel;

public abstract class Entity<TId> where TId : notnull
{
    public TId Id { get; protected set; } = default!;
}

// Base for entities owned by a tenant. See docs/adr/0005-multi-tenancy.md —
// isolation is enforced via an EF Core global query filter on TenantId.
public abstract class TenantOwnedEntity<TId> : Entity<TId> where TId : notnull
{
    public TenantId TenantId { get; protected set; }
}
