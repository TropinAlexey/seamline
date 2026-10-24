using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Risk.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddValuationRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "valuation_run",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_valuation_run", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_valuation_run_tenant_id_run_id",
                schema: "risk",
                table: "valuation_run",
                columns: new[] { "tenant_id", "run_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_valuation_run_tenant_id_valued_at",
                schema: "risk",
                table: "valuation_run",
                columns: new[] { "tenant_id", "valued_at" });

            // Runs before this table existed are known only by their rows.
            // A run that wrote no row for a tenant left no trace, so it
            // cannot be backfilled; the next run marks every tenant.
            migrationBuilder.Sql("""
                INSERT INTO risk.valuation_run ("Id", tenant_id, run_id, valued_at)
                SELECT gen_random_uuid(), tenant_id, run_id, max(valued_at)
                FROM (
                    SELECT tenant_id, run_id, valued_at FROM risk.valuation_snapshot
                    UNION ALL SELECT tenant_id, run_id, valued_at FROM risk.stress_scenario_result
                    UNION ALL SELECT tenant_id, run_id, valued_at FROM risk.inventory_valuation
                ) AS rows
                GROUP BY tenant_id, run_id;
                """);

            // Append-only, like the rows it marks (ADR-0006).
            migrationBuilder.Sql("GRANT SELECT, INSERT ON risk.valuation_run TO seamline_app;");

            // ADR-0005 layer 2 (RLS), from creation.
            migrationBuilder.Sql("ALTER TABLE risk.valuation_run ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON risk.valuation_run
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "valuation_run",
                schema: "risk");
        }
    }
}
