using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Audit.Internal.Migrations
{
    /// <inheritdoc />
    public partial class EnableRowLevelSecurity : Migration
    {
        // See the identical migration in Reference for the full rationale
        // (ADR-0005, second enforcement layer).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE audit.audit_event ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON audit.audit_event
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY tenant_isolation ON audit.audit_event;");
            migrationBuilder.Sql("ALTER TABLE audit.audit_event DISABLE ROW LEVEL SECURITY;");
        }
    }
}
