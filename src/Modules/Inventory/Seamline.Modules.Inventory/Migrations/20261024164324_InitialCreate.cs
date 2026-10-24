using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Inventory.Internal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "inventory_movement",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gas_day = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    trade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_after = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    wacog_after = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_movement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "storage_contract",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    commodity_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    capacity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    max_injection_per_day = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    max_withdrawal_per_day = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    wacog = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_contract", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movement_storage_contract_id_gas_day",
                schema: "inventory",
                table: "inventory_movement",
                columns: new[] { "storage_contract_id", "gas_day" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movement_storage_contract_id_version",
                schema: "inventory",
                table: "inventory_movement",
                columns: new[] { "storage_contract_id", "version" },
                unique: true);

            migrationBuilder.Sql("GRANT USAGE ON SCHEMA inventory TO seamline_app;");
            // The contract row carries the current balance, so the app
            // updates it; the movement ledger is append-only (ADR-0006).
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON inventory.storage_contract TO seamline_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON inventory.inventory_movement TO seamline_app;");

            // ADR-0005 layer 2 (RLS), from creation.
            foreach (var table in new[] { "storage_contract", "inventory_movement" })
            {
                migrationBuilder.Sql($"ALTER TABLE inventory.{table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"""
                    CREATE POLICY tenant_isolation ON inventory.{table}
                    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_movement",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "storage_contract",
                schema: "inventory");
        }
    }
}
