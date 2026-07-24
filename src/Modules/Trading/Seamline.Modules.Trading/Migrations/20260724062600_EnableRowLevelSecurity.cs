using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Trading.Internal.Migrations
{
    /// <inheritdoc />
    public partial class EnableRowLevelSecurity : Migration
    {
        // See the identical migration in Reference for the full rationale
        // (ADR-0005, second enforcement layer). trade_history is append-only
        // (seamline_app has no UPDATE/DELETE grant on it at all — see
        // InitialCreate), so the policy here only ever gates SELECT/INSERT
        // in practice, but it's declared the same way as every other table
        // for consistency; there's nothing dangerous about also covering
        // commands that can never reach the table.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE trading.trade ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON trading.trade
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);

            migrationBuilder.Sql("ALTER TABLE trading.trade_history ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON trading.trade_history
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY tenant_isolation ON trading.trade_history;");
            migrationBuilder.Sql("ALTER TABLE trading.trade_history DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("DROP POLICY tenant_isolation ON trading.trade;");
            migrationBuilder.Sql("ALTER TABLE trading.trade DISABLE ROW LEVEL SECURITY;");
        }
    }
}
