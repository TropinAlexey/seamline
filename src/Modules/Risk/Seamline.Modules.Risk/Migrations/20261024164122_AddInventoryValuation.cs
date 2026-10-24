using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Risk.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryValuation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_valuation",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    commodity_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    wacog = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    curve_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    curve_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    unrealized_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_valuation", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_valuation_tenant_id_run_id",
                schema: "risk",
                table: "inventory_valuation",
                columns: new[] { "tenant_id", "run_id" });

            // Append-only, like valuation_snapshot (ADR-0006).
            migrationBuilder.Sql("GRANT SELECT, INSERT ON risk.inventory_valuation TO seamline_app;");

            // ADR-0005 layer 2 (RLS), from creation.
            migrationBuilder.Sql("ALTER TABLE risk.inventory_valuation ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON risk.inventory_valuation
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_valuation",
                schema: "risk");
        }
    }
}
