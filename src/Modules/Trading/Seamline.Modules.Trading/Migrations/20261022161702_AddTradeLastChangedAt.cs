using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Trading.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeLastChangedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_changed_at",
                schema: "trading",
                table: "trade",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Backfill from history (the owner role running migrations bypasses
            // RLS). Trades created before creation was recorded in history and
            // never transitioned have no row there — fall back to now().
            migrationBuilder.Sql("""
                UPDATE trading.trade t
                SET last_changed_at = COALESCE(
                    (SELECT max(h.valid_from) FROM trading.trade_history h WHERE h.trade_id = t."Id"),
                    now());
                """);

            migrationBuilder.CreateIndex(
                name: "IX_trade_tenant_id_last_changed_at",
                schema: "trading",
                table: "trade",
                columns: new[] { "tenant_id", "last_changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_trade_tenant_id_last_changed_at",
                schema: "trading",
                table: "trade");

            migrationBuilder.DropColumn(
                name: "last_changed_at",
                schema: "trading",
                table: "trade");
        }
    }
}
