using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Audit.Internal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_event",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    context = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_event", x => x.Id);
                });

            // Append-only, same guarantee as trading.trade_history (ADR-0006):
            // seamline_app gets SELECT + INSERT only — no UPDATE, no DELETE.
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA audit TO seamline_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON audit.audit_event TO seamline_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_event",
                schema: "audit");
        }
    }
}
