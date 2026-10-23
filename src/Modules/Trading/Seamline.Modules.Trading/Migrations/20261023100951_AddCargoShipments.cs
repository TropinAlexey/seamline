using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Trading.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddCargoShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "actual_volume",
                schema: "trading",
                table: "trade_history",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_volume",
                schema: "trading",
                table: "trade",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cargo_base_nar_kcal_kg",
                schema: "trading",
                table: "trade",
                type: "numeric(7,1)",
                precision: 7,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "cargo_laycan_from",
                schema: "trading",
                table: "trade",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "cargo_laycan_to",
                schema: "trading",
                table: "trade",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cargo_max_ash_pct",
                schema: "trading",
                table: "trade",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cargo_max_sulphur_pct",
                schema: "trading",
                table: "trade",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cargo_reject_below_nar_kcal_kg",
                schema: "trading",
                table: "trade",
                type: "numeric(7,1)",
                precision: 7,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cargo_tolerance_pct",
                schema: "trading",
                table: "trade",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shipment",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    trade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vessel_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    imo = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    eta = table.Column<DateOnly>(type: "date", nullable: false),
                    bl_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    bl_date = table.Column<DateOnly>(type: "date", nullable: true),
                    measured_nar_kcal_kg = table.Column<decimal>(type: "numeric(7,1)", precision: 7, scale: 1, nullable: true),
                    sulphur_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    ash_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    last_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "shipment_history",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    change_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vessel_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    imo = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    eta = table.Column<DateOnly>(type: "date", nullable: false),
                    bl_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    bl_date = table.Column<DateOnly>(type: "date", nullable: true),
                    measured_nar_kcal_kg = table.Column<decimal>(type: "numeric(7,1)", precision: 7, scale: 1, nullable: true),
                    sulphur_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    ash_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_history", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shipment_trade_id",
                schema: "trading",
                table: "shipment",
                column: "trade_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipment_history_shipment_id_version",
                schema: "trading",
                table: "shipment_history",
                columns: new[] { "shipment_id", "version" },
                unique: true);

            // Shipment is current state (UPDATE as it moves through loading
            // and certification); its history is append-only like
            // trade_history (ADR-0006). RLS from creation (ADR-0005).
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON trading.shipment TO seamline_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON trading.shipment_history TO seamline_app;");
            foreach (var table in new[] { "shipment", "shipment_history" })
            {
                migrationBuilder.Sql($"ALTER TABLE trading.{table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"""
                    CREATE POLICY tenant_isolation ON trading.{table}
                    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipment",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "shipment_history",
                schema: "trading");

            migrationBuilder.DropColumn(
                name: "actual_volume",
                schema: "trading",
                table: "trade_history");

            migrationBuilder.DropColumn(
                name: "actual_volume",
                schema: "trading",
                table: "trade");

            migrationBuilder.DropColumn(
                name: "cargo_base_nar_kcal_kg",
                schema: "trading",
                table: "trade");

            migrationBuilder.DropColumn(
                name: "cargo_laycan_from",
                schema: "trading",
                table: "trade");

            migrationBuilder.DropColumn(
                name: "cargo_laycan_to",
                schema: "trading",
                table: "trade");

            migrationBuilder.DropColumn(
                name: "cargo_max_ash_pct",
                schema: "trading",
                table: "trade");

            migrationBuilder.DropColumn(
                name: "cargo_max_sulphur_pct",
                schema: "trading",
                table: "trade");

            migrationBuilder.DropColumn(
                name: "cargo_reject_below_nar_kcal_kg",
                schema: "trading",
                table: "trade");

            migrationBuilder.DropColumn(
                name: "cargo_tolerance_pct",
                schema: "trading",
                table: "trade");
        }
    }
}
