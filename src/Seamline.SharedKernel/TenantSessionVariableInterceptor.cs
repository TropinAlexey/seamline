using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Seamline.SharedKernel;

// Second layer of ADR-0005's multi-tenancy (the first is each DbContext's
// HasQueryFilter). Sets a Postgres session variable on every connection open
// so the RLS policies each module's migrations declare (see e.g.
// Reference's EnableRowLevelSecurity migration) have something to check
// against. Each module wires this into its own AddDbContext call via
// AddInterceptors — see ReferenceModuleExtensions and its counterparts.
//
// ConnectionOpened fires on every logical Open(), even when Npgsql reuses a
// pooled physical connection for a different tenant's previous request —
// that's exactly why SET (not SET LOCAL, which only lives inside a
// transaction) here is safe: it's reapplied every time, so a stale value
// from a previous logical use of the same physical connection never
// survives past the next Open().
public sealed class TenantSessionVariableInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SetSessionTenantSql();
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SetSessionTenantSql();
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // SET doesn't support bound parameters, so this is string interpolation
    // rather than a parameterized command — safe only because Guid's "D"
    // format is exactly 32 hex digits and 4 hyphens, never quotes or other
    // characters SQL would treat specially. That invariant lives on Guid's
    // formatter, not on anything this class controls, so it'd need
    // revisiting if TenantId's underlying type ever changed.
    private string SetSessionTenantSql() => $"SET app.tenant_id = '{tenantContext.TenantId.Value:D}';";
}
