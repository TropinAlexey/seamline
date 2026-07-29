using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Trading.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddRemitReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "remit_report",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ack_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remit_report", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_remit_report_trade_id_version",
                schema: "trading",
                table: "remit_report",
                columns: new[] { "trade_id", "version" },
                unique: true);

            // Append-only, same as trade_history (ADR-0006): seamline_app —
            // the role Seamline.Api and Seamline.Reporting.Worker both
            // connect as at runtime — gets SELECT/INSERT only, never
            // UPDATE/DELETE. trading schema USAGE was already granted in
            // Trading's InitialCreate.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON trading.remit_report TO seamline_app;");

            // ADR-0005 layer 2 (RLS).
            migrationBuilder.Sql("ALTER TABLE trading.remit_report ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON trading.remit_report
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY tenant_isolation ON trading.remit_report;");
            migrationBuilder.Sql("ALTER TABLE trading.remit_report DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "remit_report",
                schema: "trading");
        }
    }
}
