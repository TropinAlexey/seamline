using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Seamline.SharedKernel;

namespace Seamline.Modules.Identity.Internal;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // No authenticated context exists yet to read a tenant from — the
        // caller states which tenant it's logging into right here, then
        // everything downstream carries it as a signed claim instead.
        // See docs/adr/0013.
        app.MapPost("/auth/login", async (
                LoginRequest request, IdentityDbContext db, TenantContext tenantContext, IConfiguration configuration, CancellationToken ct) =>
            {
                tenantContext.SetTenant(new TenantId(request.TenantId));

                var user = await db.Users.FirstOrDefaultAsync(u => u.Login == request.Login, ct);
                if (user is null || !user.VerifyPassword(request.Password))
                    return Results.Unauthorized();

                var token = JwtTokenFactory.CreateToken(configuration, user);
                return Results.Ok(new LoginResponse(token));
            })
            .AllowAnonymous()
            .WithTags("Identity");

        return app;
    }
}

internal sealed record LoginRequest(Guid TenantId, string Login, string Password);

internal sealed record LoginResponse(string Token);
