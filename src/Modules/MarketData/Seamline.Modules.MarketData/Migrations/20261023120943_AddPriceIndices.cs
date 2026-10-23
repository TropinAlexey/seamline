using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.MarketData.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceIndices : Migration
    {
        // Both demo tenants get the synthetic coal index formula trades
        // average (spec 03); a real benchmark is proprietary (clean room).
        private static readonly string[] DemoTenantIds =
        [
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "index_fixing",
                schema: "marketdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    index_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fixing_date = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_index_fixing", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "price_index",
                schema: "marketdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    commodity_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_index", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_index_fixing_tenant_id_index_code_fixing_date",
                schema: "marketdata",
                table: "index_fixing",
                columns: new[] { "tenant_id", "index_code", "fixing_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_price_index_tenant_id_code",
                schema: "marketdata",
                table: "price_index",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            // Indices are seeded master data; fixings are append-only
            // settlement inputs (ADR-0035) — no UPDATE or DELETE for the app.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON marketdata.price_index TO seamline_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON marketdata.index_fixing TO seamline_app;");

            // ADR-0005 layer 2 (RLS), from creation.
            foreach (var table in new[] { "price_index", "index_fixing" })
            {
                migrationBuilder.Sql($"ALTER TABLE marketdata.{table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"""
                    CREATE POLICY tenant_isolation ON marketdata.{table}
                    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                    """);
            }

            // Runs as the owner role, which bypasses RLS.
            foreach (var tenantId in DemoTenantIds)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO marketdata.price_index ("Id", tenant_id, code, commodity_code, name)
                    VALUES (gen_random_uuid(), '{tenantId}', 'COAL-IDX', 'COAL', 'Synthetic coal index (demo)');
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "index_fixing",
                schema: "marketdata");

            migrationBuilder.DropTable(
                name: "price_index",
                schema: "marketdata");
        }
    }
}
