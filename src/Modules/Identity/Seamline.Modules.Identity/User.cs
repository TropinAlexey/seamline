using Seamline.SharedKernel;

namespace Seamline.Modules.Identity.Internal;

internal sealed class User : TenantOwnedEntity<Guid>
{
    public string Login { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private User() { }

    public static User Create(TenantId tenantId, string login, string password, string role)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Login = login,
            PasswordHash = PasswordHasher.Hash(password),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public bool VerifyPassword(string password) => PasswordHasher.Verify(password, PasswordHash);
}
