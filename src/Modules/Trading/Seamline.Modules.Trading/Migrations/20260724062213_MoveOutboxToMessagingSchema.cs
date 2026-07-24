using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Trading.Internal.Migrations
{
    /// <inheritdoc />
    public partial class MoveOutboxToMessagingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.RenameTable(
                name: "OutboxState",
                schema: "trading",
                newName: "OutboxState",
                newSchema: "messaging");

            migrationBuilder.RenameTable(
                name: "OutboxMessage",
                schema: "trading",
                newName: "OutboxMessage",
                newSchema: "messaging");

            migrationBuilder.RenameTable(
                name: "InboxState",
                schema: "trading",
                newName: "InboxState",
                newSchema: "messaging");

            // The existing GRANT SELECT/INSERT/UPDATE/DELETE on these three
            // tables (from InitialCreate) survives the rename — PostgreSQL
            // privileges attach to the table object, not its schema path.
            // USAGE on the schema itself is a separate permission, though,
            // and seamline_app never had it for "messaging" since it didn't
            // exist until EnsureSchema above.
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA messaging TO seamline_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "OutboxState",
                schema: "messaging",
                newName: "OutboxState",
                newSchema: "trading");

            migrationBuilder.RenameTable(
                name: "OutboxMessage",
                schema: "messaging",
                newName: "OutboxMessage",
                newSchema: "trading");

            migrationBuilder.RenameTable(
                name: "InboxState",
                schema: "messaging",
                newName: "InboxState",
                newSchema: "trading");
        }
    }
}
