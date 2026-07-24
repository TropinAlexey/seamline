using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Reference.Internal.Migrations
{
    /// <inheritdoc />
    public partial class EnableRowLevelSecurity : Migration
    {
        // Second layer of ADR-0005's multi-tenancy: the EF Core query filter
        // (layer 1) is application code and can be bypassed by
        // IgnoreQueryFilters(), raw SQL, or a query path that simply forgets
        // it. RLS enforces the same boundary in the database itself, keyed
        // on a session variable (app.tenant_id) the connection interceptor
        // sets on every connection open — see TenantSessionVariableInterceptor
        // in Seamline.Api. seamline_app is not the table owner, so RLS
        // applies to it without needing FORCE ROW LEVEL SECURITY (which
        // would be needed only to also restrict the owner role itself).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE reference.counterparty ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON reference.counterparty
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY tenant_isolation ON reference.counterparty;");
            migrationBuilder.Sql("ALTER TABLE reference.counterparty DISABLE ROW LEVEL SECURITY;");
        }
    }
}
