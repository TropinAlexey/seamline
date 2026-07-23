namespace Seamline.SharedKernel;

// Mutable, scoped implementation of ITenantContext. HTTP middleware sets it
// from a request header; a message consumer sets it from the tenant carried
// on the integration event — there is no HTTP request to read a header from
// when a message is being consumed.
public sealed class TenantContext : ITenantContext
{
    public TenantId TenantId { get; private set; }

    public void SetTenant(TenantId tenantId) => TenantId = tenantId;
}
