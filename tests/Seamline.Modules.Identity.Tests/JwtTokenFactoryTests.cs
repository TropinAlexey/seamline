using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Identity.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Identity.Tests;

public class JwtTokenFactoryTests
{
    private const string SigningKey = "test-only-signing-key-at-least-32-bytes-long";
    private const string Issuer = "seamline-test";

    [Fact]
    public void CreateToken_carries_the_users_identity_role_and_tenant_as_claims()
    {
        var tenantId = TenantId.New();
        var user = User.Create(tenantId, "trader", "password", IdentityRoles.FrontOffice);

        var token = JwtTokenFactory.CreateToken(CreateConfiguration(), user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(user.Id.ToString(), jwt.Claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("trader", jwt.Claims.Single(c => c.Type == ClaimTypes.Name).Value);
        Assert.Equal(IdentityRoles.FrontOffice, jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal(tenantId.Value.ToString(), jwt.Claims.Single(c => c.Type == IdentityClaimTypes.TenantId).Value);
    }

    [Fact]
    public void CreateToken_sets_the_issuer_from_configuration()
    {
        var user = User.Create(TenantId.New(), "trader", "password", IdentityRoles.FrontOffice);

        var token = JwtTokenFactory.CreateToken(CreateConfiguration(), user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(Issuer, jwt.Issuer);
    }

    [Fact]
    public void CreateToken_expires_in_the_future_but_not_absurdly_far_out()
    {
        var user = User.Create(TenantId.New(), "trader", "password", IdentityRoles.FrontOffice);

        var token = JwtTokenFactory.CreateToken(CreateConfiguration(), user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
        Assert.True(jwt.ValidTo <= DateTime.UtcNow.AddHours(9)); // 8h expiry + slack
    }

    [Fact]
    public void CreateToken_validates_successfully_against_the_same_signing_key()
    {
        var user = User.Create(TenantId.New(), "trader", "password", IdentityRoles.FrontOffice);
        var token = JwtTokenFactory.CreateToken(CreateConfiguration(), user);

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, ValidationParameters(SigningKey), out _);

        Assert.Equal(user.Id.ToString(), principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    }

    [Fact]
    public void CreateToken_fails_validation_against_a_different_signing_key()
    {
        var user = User.Create(TenantId.New(), "trader", "password", IdentityRoles.FrontOffice);
        var token = JwtTokenFactory.CreateToken(CreateConfiguration(), user);

        Assert.ThrowsAny<SecurityTokenException>(
            () => new JwtSecurityTokenHandler().ValidateToken(token, ValidationParameters("a-completely-different-signing-key-32-bytes"), out _));
    }

    private static TokenValidationParameters ValidationParameters(string signingKey) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = false,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidateLifetime = true
    };

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:SigningKey"] = SigningKey
            })
            .Build();
}
