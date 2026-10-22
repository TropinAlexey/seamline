using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Risk.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddEodRunId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "run_id",
                schema: "risk",
                table: "valuation_snapshot",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "run_id",
                schema: "risk",
                table: "stress_scenario_result",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill (ADR-0031, best effort): rows written before runs had an
            // id stamped their own UtcNow, milliseconds apart within one pass.
            // Rows of the same tenant valued in the same minute are treated as
            // one run, across both tables, so a legacy stress result still
            // lines up with its base valuation. Runs as the owner role — the
            // app role's SELECT/INSERT-only grant on these tables is unchanged.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE legacy_eod_run AS
                SELECT tenant_id, bucket, gen_random_uuid() AS run_id
                FROM (
                    SELECT tenant_id, date_trunc('minute', valued_at) AS bucket FROM risk.valuation_snapshot
                    UNION
                    SELECT tenant_id, date_trunc('minute', valued_at) FROM risk.stress_scenario_result
                ) buckets;

                UPDATE risk.valuation_snapshot v SET run_id = r.run_id
                FROM legacy_eod_run r
                WHERE v.tenant_id = r.tenant_id AND date_trunc('minute', v.valued_at) = r.bucket;

                UPDATE risk.stress_scenario_result s SET run_id = r.run_id
                FROM legacy_eod_run r
                WHERE s.tenant_id = r.tenant_id AND date_trunc('minute', s.valued_at) = r.bucket;

                DROP TABLE legacy_eod_run;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_valuation_snapshot_tenant_id_run_id",
                schema: "risk",
                table: "valuation_snapshot",
                columns: new[] { "tenant_id", "run_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stress_scenario_result_tenant_id_run_id",
                schema: "risk",
                table: "stress_scenario_result",
                columns: new[] { "tenant_id", "run_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_valuation_snapshot_tenant_id_run_id",
                schema: "risk",
                table: "valuation_snapshot");

            migrationBuilder.DropIndex(
                name: "IX_stress_scenario_result_tenant_id_run_id",
                schema: "risk",
                table: "stress_scenario_result");

            migrationBuilder.DropColumn(
                name: "run_id",
                schema: "risk",
                table: "valuation_snapshot");

            migrationBuilder.DropColumn(
                name: "run_id",
                schema: "risk",
                table: "stress_scenario_result");
        }
    }
}
