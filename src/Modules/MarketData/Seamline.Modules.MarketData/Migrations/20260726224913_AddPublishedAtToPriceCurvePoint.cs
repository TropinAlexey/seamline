using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.MarketData.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishedAtToPriceCurvePoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Any pre-existing row (there shouldn't be any in a demo
            // deployment, but the migration has to be valid regardless)
            // gets "now" rather than a sentinel epoch — an honest "we don't
            // know when this was actually published" is closer to now than
            // to year 1.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                schema: "marketdata",
                table: "price_curve_point",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "marketdata",
                table: "price_curve_point");
        }
    }
}
