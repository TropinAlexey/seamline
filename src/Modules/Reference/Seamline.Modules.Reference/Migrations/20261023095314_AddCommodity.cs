using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Reference.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddCommodity : Migration
    {
        // Both demo tenants (Identity's InitialCreate and AddSecondDemoTenant)
        // get the two flow commodities every existing trade and curve point
        // already uses. COAL arrives with the cargo step (ADR-0034), when a
        // cargo trade can actually be delivered.
        private static readonly string[] DemoTenantIds =
        [
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commodity",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit = table.Column<int>(type: "integer", nullable: false),
                    delivery_model = table.Column<int>(type: "integer", nullable: false),
                    base_nar_kcal_kg = table.Column<decimal>(type: "numeric(7,1)", precision: 7, scale: 1, nullable: true),
                    reject_below_nar_kcal_kg = table.Column<decimal>(type: "numeric(7,1)", precision: 7, scale: 1, nullable: true),
                    max_sulphur_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    max_ash_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commodity", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_commodity_tenant_id_code",
                schema: "reference",
                table: "commodity",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            // UPDATE because ChangeQualitySpec exists; no DELETE — a commodity
            // that trades refer to never disappears (ADR-0033).
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON reference.commodity TO seamline_app;");

            // RLS from creation, like every table since ADR-0005's second
            // layer existed — no window without it.
            migrationBuilder.Sql("ALTER TABLE reference.commodity ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON reference.commodity
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);

            // Runs as the owner role, which bypasses RLS — no app.tenant_id
            // needed for the seed.
            foreach (var tenantId in DemoTenantIds)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO reference.commodity ("Id", tenant_id, code, name, unit, delivery_model)
                    VALUES (gen_random_uuid(), '{tenantId}', 'POWER', 'Power', 1, 1),
                           (gen_random_uuid(), '{tenantId}', 'GAS', 'Natural gas', 1, 1);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commodity",
                schema: "reference");
        }
    }
}
