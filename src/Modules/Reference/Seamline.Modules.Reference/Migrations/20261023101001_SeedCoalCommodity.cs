using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seamline.Modules.Reference.Internal.Migrations
{
    /// <inheritdoc />
    public partial class SeedCoalCommodity : Migration
    {
        // Seeded with the cargo step (ADR-0034), not with master data itself
        // (ADR-0033): before shipments existed, a bookable COAL would have
        // been a trade the system could not deliver. Quality values are
        // Seamline's own demo numbers (spec 00).
        private static readonly string[] DemoTenantIds =
        [
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var tenantId in DemoTenantIds)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO reference.commodity
                        ("Id", tenant_id, code, name, unit, delivery_model,
                         base_nar_kcal_kg, reject_below_nar_kcal_kg, max_sulphur_pct, max_ash_pct)
                    VALUES (gen_random_uuid(), '{tenantId}', 'COAL', 'Steam coal', 2, 2, 6000.0, 5700.0, 1.00, 16.00)
                    ON CONFLICT (tenant_id, code) DO NOTHING;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM reference.commodity WHERE code = 'COAL';");
        }
    }
}
