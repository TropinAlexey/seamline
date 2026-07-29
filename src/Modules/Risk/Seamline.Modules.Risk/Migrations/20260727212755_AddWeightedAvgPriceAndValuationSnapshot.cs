using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Risk.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddWeightedAvgPriceAndValuationSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "weighted_avg_price",
                schema: "risk",
                table: "position",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "valuation_snapshot",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    commodity_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    net_volume = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    weighted_avg_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    curve_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    curve_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mtm_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    valued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_valuation_snapshot", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_valuation_snapshot_tenant_id_commodity_code_delivery_period~",
                schema: "risk",
                table: "valuation_snapshot",
                columns: new[] { "tenant_id", "commodity_code", "delivery_period", "valued_at" });

            // Append-only, same as trade_history (ADR-0006): seamline_app —
            // the role both Seamline.Api and Valuation.Worker connect as at
            // runtime — gets SELECT/INSERT only, never UPDATE/DELETE.
            // risk schema USAGE was already granted in Risk's InitialCreate.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON risk.valuation_snapshot TO seamline_app;");

            // ADR-0005 layer 2 (RLS).
            migrationBuilder.Sql("ALTER TABLE risk.valuation_snapshot ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON risk.valuation_snapshot
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY tenant_isolation ON risk.valuation_snapshot;");
            migrationBuilder.Sql("ALTER TABLE risk.valuation_snapshot DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "valuation_snapshot",
                schema: "risk");

            migrationBuilder.DropColumn(
                name: "weighted_avg_price",
                schema: "risk",
                table: "position");
        }
    }
}
