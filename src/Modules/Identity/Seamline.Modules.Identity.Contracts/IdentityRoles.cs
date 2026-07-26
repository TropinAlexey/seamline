namespace Seamline.Modules.Identity.Contracts;

// The one thing other modules legitimately need from Identity: compile-time
// safe role names for RequireAuthorization(policy => policy.RequireRole(...)).
// See docs/adr/0013 — no query interface exists into Identity because
// authorization reads the JWT's claims, not a synchronous call back in.
public static class IdentityRoles
{
    public const string FrontOffice = "FO";
    public const string MiddleOffice = "MO";
    public const string BackOffice = "BO";
}

// The claim name the JWT carries the tenant under — shared between token
// issuance (Identity's JwtTokenFactory) and the composition root
// (Seamline.Api's tenant-claim middleware), which is why it lives here
// rather than as an internal constant on either side.
public static class IdentityClaimTypes
{
    public const string TenantId = "tenant_id";
}
