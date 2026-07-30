using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Risk.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddStressScenarioResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stress_scenario_result",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    commodity_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    net_volume = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    weighted_avg_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    scenario_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    shock_percentage = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    shocked_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    mtm_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    valued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stress_scenario_result", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stress_scenario_result_tenant_id_commodity_code_delivery_pe~",
                schema: "risk",
                table: "stress_scenario_result",
                columns: new[] { "tenant_id", "commodity_code", "delivery_period", "valued_at" });

            // Append-only, same as valuation_snapshot: seamline_app gets
            // SELECT/INSERT only. risk schema USAGE already granted in
            // Risk's InitialCreate.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON risk.stress_scenario_result TO seamline_app;");

            // ADR-0005 layer 2 (RLS).
            migrationBuilder.Sql("ALTER TABLE risk.stress_scenario_result ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON risk.stress_scenario_result
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY tenant_isolation ON risk.stress_scenario_result;");
            migrationBuilder.Sql("ALTER TABLE risk.stress_scenario_result DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "stress_scenario_result",
                schema: "risk");
        }
    }
}
