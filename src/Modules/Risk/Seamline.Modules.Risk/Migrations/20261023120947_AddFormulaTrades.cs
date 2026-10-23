using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Risk.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddFormulaTrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "trade_id",
                schema: "risk",
                table: "valuation_snapshot",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trade_id",
                schema: "risk",
                table: "stress_scenario_result",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "trade_price",
                schema: "risk",
                table: "credit_reservation",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<string>(
                name: "pricing_index_code",
                schema: "risk",
                table: "credit_reservation",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pricing_premium",
                schema: "risk",
                table: "credit_reservation",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "pricing_window_from",
                schema: "risk",
                table: "credit_reservation",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "pricing_window_to",
                schema: "risk",
                table: "credit_reservation",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "formula_trade",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    trade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commodity_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    signed_volume = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    pricing_index_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pricing_window_from = table.Column<DateOnly>(type: "date", nullable: false),
                    pricing_window_to = table.Column<DateOnly>(type: "date", nullable: false),
                    pricing_premium = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    final_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    is_delivered = table.Column<bool>(type: "boolean", nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_formula_trade", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_formula_trade_trade_id",
                schema: "risk",
                table: "formula_trade",
                column: "trade_id",
                unique: true);

            // Derived from Trading's events like position: the consumers
            // update it (volume, final price, delivery), nothing deletes it.
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON risk.formula_trade TO seamline_app;");

            // ADR-0005 layer 2 (RLS), from creation.
            migrationBuilder.Sql("ALTER TABLE risk.formula_trade ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON risk.formula_trade
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "formula_trade",
                schema: "risk");

            migrationBuilder.DropColumn(
                name: "trade_id",
                schema: "risk",
                table: "valuation_snapshot");

            migrationBuilder.DropColumn(
                name: "trade_id",
                schema: "risk",
                table: "stress_scenario_result");

            migrationBuilder.DropColumn(
                name: "pricing_index_code",
                schema: "risk",
                table: "credit_reservation");

            migrationBuilder.DropColumn(
                name: "pricing_premium",
                schema: "risk",
                table: "credit_reservation");

            migrationBuilder.DropColumn(
                name: "pricing_window_from",
                schema: "risk",
                table: "credit_reservation");

            migrationBuilder.DropColumn(
                name: "pricing_window_to",
                schema: "risk",
                table: "credit_reservation");

            migrationBuilder.AlterColumn<decimal>(
                name: "trade_price",
                schema: "risk",
                table: "credit_reservation",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4,
                oldNullable: true);
        }
    }
}
