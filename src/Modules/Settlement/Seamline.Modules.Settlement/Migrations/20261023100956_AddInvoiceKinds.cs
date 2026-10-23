using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Settlement.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoice_trade_id",
                schema: "settlement",
                table: "invoice");

            migrationBuilder.AddColumn<decimal>(
                name: "balance",
                schema: "settlement",
                table: "invoice",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "settlement",
                table: "invoice",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");

            // Every invoice issued so far was a flow-commodity invoice. The
            // default exists only to backfill them; the application always
            // sets the kind, so it is dropped again — no silent DB default
            // behind the EF model.
            migrationBuilder.Sql("ALTER TABLE settlement.invoice ALTER COLUMN kind DROP DEFAULT;");

            migrationBuilder.AddColumn<decimal>(
                name: "quantity",
                schema: "settlement",
                table: "invoice",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_price",
                schema: "settlement",
                table: "invoice",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_trade_id_kind",
                schema: "settlement",
                table: "invoice",
                columns: new[] { "trade_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoice_trade_id_kind",
                schema: "settlement",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "balance",
                schema: "settlement",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "settlement",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "quantity",
                schema: "settlement",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "unit_price",
                schema: "settlement",
                table: "invoice");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_trade_id",
                schema: "settlement",
                table: "invoice",
                column: "trade_id",
                unique: true);
        }
    }
}
