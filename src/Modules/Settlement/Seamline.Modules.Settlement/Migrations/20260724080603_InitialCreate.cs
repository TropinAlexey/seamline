using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Settlement.Internal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "settlement");

            migrationBuilder.CreateTable(
                name: "invoice",
                schema: "settlement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    trade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_trade_id",
                schema: "settlement",
                table: "invoice",
                column: "trade_id",
                unique: true);

            // seamline_app is the restricted runtime role (see
            // docker/postgres-init/01-create-app-role.sql). No UPDATE
            // grant: there is no code path that ever modifies an invoice
            // once created (see Invoice — no lifecycle yet).
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA settlement TO seamline_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON settlement.invoice TO seamline_app;");

            // ADR-0005 layer 2 (RLS) — baked into InitialCreate directly,
            // same as MarketData's price_curve_point.
            migrationBuilder.Sql("ALTER TABLE settlement.invoice ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON settlement.invoice
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY tenant_isolation ON settlement.invoice;");
            migrationBuilder.Sql("ALTER TABLE settlement.invoice DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "invoice",
                schema: "settlement");
        }
    }
}
