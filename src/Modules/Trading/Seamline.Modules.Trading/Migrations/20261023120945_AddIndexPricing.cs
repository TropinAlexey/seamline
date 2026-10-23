using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Trading.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                schema: "trading",
                table: "trade_history",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                schema: "trading",
                table: "trade",
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
                schema: "trading",
                table: "trade",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pricing_premium",
                schema: "trading",
                table: "trade",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "pricing_window_from",
                schema: "trading",
                table: "trade",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "pricing_window_to",
                schema: "trading",
                table: "trade",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pricing_index_code",
                schema: "trading",
                table: "trade");

            migrationBuilder.DropColumn(
                name: "pricing_premium",
                schema: "trading",
                table: "trade");

            migrationBuilder.DropColumn(
                name: "pricing_window_from",
                schema: "trading",
                table: "trade");

            migrationBuilder.DropColumn(
                name: "pricing_window_to",
                schema: "trading",
                table: "trade");

            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                schema: "trading",
                table: "trade_history",
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

            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                schema: "trading",
                table: "trade",
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
