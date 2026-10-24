using System.Security.Claims;

namespace Seamline.SharedKernel;

public static class ClaimsPrincipalExtensions
{
    // The name recorded as the actor on history rows, ledger rows and
    // events: the login name from the JWT.
    public static string ActorName(this ClaimsPrincipal user) => user.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
}
