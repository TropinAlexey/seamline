using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using Seamline.Modules.Identity.Internal;

namespace Seamline.IntegrationTests;

// Seeds a user directly via SQL on the owner connection (bypasses RLS — same
// pattern RowLevelSecurityTests uses) rather than adding a self-service
// registration endpoint nothing else in the app needs (see docs/adr/0013),
// then logs in through the real /auth/login endpoint so tests exercise the
// actual JWT issuance path, not a shortcut around it.
internal static class AuthTestHelper
{
    private const string Password = "Test-Password-123!";

    public static async Task<HttpClient> CreateAuthenticatedClientAsync(SeamlineApiFactory factory, Guid tenantId, string role)
    {
        var client = factory.CreateClient();
        var login = $"user-{Guid.NewGuid():N}";

        var passwordHash = PasswordHasher.Hash(Password);

        await using (var connection = new NpgsqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO identity."user" ("Id", tenant_id, login, password_hash, role, created_at)
                VALUES (gen_random_uuid(), @tenantId, @login, @passwordHash, @role, now());
                """;
            insert.Parameters.AddWithValue("tenantId", tenantId);
            insert.Parameters.AddWithValue("login", login);
            insert.Parameters.AddWithValue("passwordHash", passwordHash);
            insert.Parameters.AddWithValue("role", role);
            await insert.ExecuteNonQueryAsync();
        }

        var loginResponse = await client.PostAsJsonAsync("/auth/login", new { tenantId, login, password = Password });
        loginResponse.EnsureSuccessStatusCode();
        var result = await loginResponse.Content.ReadFromJsonAsync<LoginResultDto>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.Token);
        return client;
    }

    private sealed record LoginResultDto(string Token);
}
