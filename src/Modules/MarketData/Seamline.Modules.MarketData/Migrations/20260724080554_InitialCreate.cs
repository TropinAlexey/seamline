using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.MarketData.Internal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "marketdata");

            migrationBuilder.CreateTable(
                name: "price_curve_point",
                schema: "marketdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    commodity_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_curve_point", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_price_curve_point_tenant_id_commodity_code_delivery_period",
                schema: "marketdata",
                table: "price_curve_point",
                columns: new[] { "tenant_id", "commodity_code", "delivery_period" },
                unique: true);

            // seamline_app is the restricted runtime role (see
            // docker/postgres-init/01-create-app-role.sql). Curve points get
            // refreshed in place (see PriceCurvePoint.UpdatePrice), so this
            // isn't append-only like trade_history — UPDATE is granted.
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA marketdata TO seamline_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON marketdata.price_curve_point TO seamline_app;");

            // ADR-0005 layer 2 (RLS) — baked into InitialCreate directly
            // since this table is new, unlike the four tables that got RLS
            // retrofitted in their own EnableRowLevelSecurity migrations.
            migrationBuilder.Sql("ALTER TABLE marketdata.price_curve_point ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON marketdata.price_curve_point
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY tenant_isolation ON marketdata.price_curve_point;");
            migrationBuilder.Sql("ALTER TABLE marketdata.price_curve_point DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "price_curve_point",
                schema: "marketdata");
        }
    }
}
