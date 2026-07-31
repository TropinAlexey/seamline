using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Risk.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddMtmFieldsToCreditReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "commodity_code",
                schema: "risk",
                table: "credit_reservation",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "delivery_period",
                schema: "risk",
                table: "credit_reservation",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "signed_volume",
                schema: "risk",
                table: "credit_reservation",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "trade_price",
                schema: "risk",
                table: "credit_reservation",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "commodity_code",
                schema: "risk",
                table: "credit_reservation");

            migrationBuilder.DropColumn(
                name: "delivery_period",
                schema: "risk",
                table: "credit_reservation");

            migrationBuilder.DropColumn(
                name: "signed_volume",
                schema: "risk",
                table: "credit_reservation");

            migrationBuilder.DropColumn(
                name: "trade_price",
                schema: "risk",
                table: "credit_reservation");
        }
    }
}
