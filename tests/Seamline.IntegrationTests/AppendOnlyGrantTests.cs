using Npgsql;
using Xunit;

namespace Seamline.IntegrationTests;

// ADR-0006: append-only tables (audit_event, trade_history, remit_report,
// invoice) are protected by GRANT SELECT, INSERT — no UPDATE, no DELETE.
// These tests prove the runtime role (seamline_app) cannot mutate or remove
// existing rows, regardless of what EF Core or application code does.
public sealed class AppendOnlyGrantTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Theory]
    [InlineData("audit.audit_event")]
    [InlineData("trading.trade_history")]
    [InlineData("trading.remit_report")]
    [InlineData("settlement.invoice")]
    public async Task Runtime_role_cannot_update_append_only_table(string table)
    {
        EnsureMigrationsRun();

        await using var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();
        await SetSessionTenantAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET tenant_id = gen_random_uuid() WHERE false;";

        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState); // insufficient_privilege
    }

    [Theory]
    [InlineData("audit.audit_event")]
    [InlineData("trading.trade_history")]
    [InlineData("trading.remit_report")]
    [InlineData("settlement.invoice")]
    public async Task Runtime_role_cannot_delete_from_append_only_table(string table)
    {
        EnsureMigrationsRun();

        await using var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();
        await SetSessionTenantAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE false;";

        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState);
    }

    private void EnsureMigrationsRun() => factory.CreateClient();

    private static async Task SetSessionTenantAsync(NpgsqlConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SET app.tenant_id = '{Guid.NewGuid():D}';";
        await cmd.ExecuteNonQueryAsync();
    }
}
