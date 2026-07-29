using Seamline.Modules.Identity.Internal;

namespace Seamline.Modules.Identity.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verify_accepts_the_password_that_was_hashed()
    {
        var hash = PasswordHasher.Hash("correct-horse-battery-staple");

        Assert.True(PasswordHasher.Verify("correct-horse-battery-staple", hash));
    }

    [Fact]
    public void Verify_rejects_a_wrong_password()
    {
        var hash = PasswordHasher.Hash("correct-horse-battery-staple");

        Assert.False(PasswordHasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Verify_is_case_sensitive()
    {
        var hash = PasswordHasher.Hash("Correct-Horse-Battery-Staple");

        Assert.False(PasswordHasher.Verify("correct-horse-battery-staple", hash));
    }

    [Fact]
    public void Hash_salts_each_call_differently()
    {
        var first = PasswordHasher.Hash("same-password");
        var second = PasswordHasher.Hash("same-password");

        // Same input, different output — a fixed salt would make identical
        // passwords produce identical hashes, which is exactly what salting
        // exists to prevent (rainbow-table / equal-password correlation).
        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("same-password", first));
        Assert.True(PasswordHasher.Verify("same-password", second));
    }

    [Fact]
    public void Hash_format_is_iterations_dot_salt_dot_hash()
    {
        var hash = PasswordHasher.Hash("some-password");

        var parts = hash.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.True(int.TryParse(parts[0], out var iterations));
        Assert.True(iterations > 0);
        Assert.True(Convert.FromBase64String(parts[1]).Length > 0);
        Assert.True(Convert.FromBase64String(parts[2]).Length > 0);
    }

    [Fact]
    public void Verify_rejects_a_tampered_hash()
    {
        var hash = PasswordHasher.Hash("some-password");
        var parts = hash.Split('.');
        var tamperedHashBytes = Convert.FromBase64String(parts[2]);
        tamperedHashBytes[0] ^= 0xFF; // flip a bit
        var tampered = $"{parts[0]}.{parts[1]}.{Convert.ToBase64String(tamperedHashBytes)}";

        Assert.False(PasswordHasher.Verify("some-password", tampered));
    }
}
