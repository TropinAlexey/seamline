using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Risk.Internal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "risk");

            migrationBuilder.CreateTable(
                name: "credit_reservation",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_reservation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "position",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    commodity_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    net_volume = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_position", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credit_reservation_trade_id",
                schema: "risk",
                table: "credit_reservation",
                column: "trade_id");

            migrationBuilder.CreateIndex(
                name: "IX_position_tenant_id_commodity_code_delivery_period",
                schema: "risk",
                table: "position",
                columns: new[] { "tenant_id", "commodity_code", "delivery_period" },
                unique: true);

            // seamline_app is the restricted runtime role. credit_reservation
            // is mutable state (Provisional -> Reserved/Released), not an
            // audit log, so UPDATE is granted here — unlike trading.trade_history.
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA risk TO seamline_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON risk.credit_reservation TO seamline_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON risk.position TO seamline_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_reservation",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "position",
                schema: "risk");
        }
    }
}
