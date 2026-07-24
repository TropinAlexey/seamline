using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Risk.Internal.Migrations
{
    /// <inheritdoc />
    public partial class EnableRowLevelSecurity : Migration
    {
        // See the identical migration in Reference for the full rationale
        // (ADR-0005, second enforcement layer).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE risk.position ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON risk.position
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);

            migrationBuilder.Sql("ALTER TABLE risk.credit_reservation ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON risk.credit_reservation
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY tenant_isolation ON risk.credit_reservation;");
            migrationBuilder.Sql("ALTER TABLE risk.credit_reservation DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("DROP POLICY tenant_isolation ON risk.position;");
            migrationBuilder.Sql("ALTER TABLE risk.position DISABLE ROW LEVEL SECURITY;");
        }
    }
}
