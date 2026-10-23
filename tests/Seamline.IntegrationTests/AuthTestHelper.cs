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
        var (client, _) = await CreateAuthenticatedClientWithLoginAsync(factory, tenantId, role);
        return client;
    }

    public static async Task<(HttpClient Client, string Login)> CreateAuthenticatedClientWithLoginAsync(SeamlineApiFactory factory, Guid tenantId, string role)
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

            // Each test runs in its own random tenant, so it gets the same
            // flow commodities the AddCommodity migration seeds for the demo
            // tenants (ADR-0033) — otherwise every POST /trades would be 400.
            await using var commodities = connection.CreateCommand();
            commodities.CommandText = """
                INSERT INTO reference.commodity ("Id", tenant_id, code, name, unit, delivery_model)
                VALUES (gen_random_uuid(), @tenantId, 'POWER', 'Power', 1, 1),
                       (gen_random_uuid(), @tenantId, 'GAS', 'Natural gas', 1, 1)
                ON CONFLICT (tenant_id, code) DO NOTHING;
                """;
            commodities.Parameters.AddWithValue("tenantId", tenantId);
            await commodities.ExecuteNonQueryAsync();
        }

        var loginResponse = await client.PostAsJsonAsync("/auth/login", new { tenantId, login, password = Password });
        loginResponse.EnsureSuccessStatusCode();
        var result = await loginResponse.Content.ReadFromJsonAsync<LoginResultDto>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.Token);
        return (client, login);
    }

    private sealed record LoginResultDto(string Token);
}
