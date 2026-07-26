using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Seamline.Modules.Identity.Contracts;

#nullable disable

namespace Seamline.Modules.Identity.Internal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        // Fixed so README/manual-testing instructions can name a tenant and
        // three logins without first calling an endpoint to discover them.
        // Not a secret — anyone who wants access to this demo tenant already
        // has the password documented alongside it.
        private const string DemoTenantId = "11111111-1111-1111-1111-111111111111";
        private const string DemoPassword = "Demo-Password-123!";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "user",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    login = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_tenant_id_login",
                schema: "identity",
                table: "user",
                columns: new[] { "tenant_id", "login" },
                unique: true);

            // seamline_app only ever reads this table (login) — user rows
            // are created here, at migration time, never by the running app.
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA identity TO seamline_app;");
            migrationBuilder.Sql("GRANT SELECT ON identity.\"user\" TO seamline_app;");

            // ADR-0005 layer 2 (RLS) — baked into InitialCreate directly,
            // same as every other module's first migration. The owner role
            // running this migration bypasses RLS, which is exactly what
            // lets the seed INSERT below work without first setting
            // app.tenant_id.
            migrationBuilder.Sql("ALTER TABLE identity.\"user\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON identity."user"
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                """);

            // One demo user per role (ADR-0013) — seeded here rather than via
            // a registration endpoint nothing else in this project needs.
            // Hashed with the same PBKDF2 PasswordHasher the login endpoint
            // verifies against, computed at migration-apply time so no
            // precomputed hash literal has to be kept in sync by hand.
            InsertDemoUser(migrationBuilder, "trader", IdentityRoles.FrontOffice);
            InsertDemoUser(migrationBuilder, "risk", IdentityRoles.MiddleOffice);
            InsertDemoUser(migrationBuilder, "backoffice", IdentityRoles.BackOffice);
        }

        private static void InsertDemoUser(MigrationBuilder migrationBuilder, string login, string role)
        {
            var passwordHash = PasswordHasher.Hash(DemoPassword);
            migrationBuilder.Sql($"""
                INSERT INTO identity."user" ("Id", tenant_id, login, password_hash, role, created_at)
                VALUES (gen_random_uuid(), '{DemoTenantId}', '{login}', '{passwordHash}', '{role}', now());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY tenant_isolation ON identity.\"user\";");
            migrationBuilder.Sql("ALTER TABLE identity.\"user\" DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "user",
                schema: "identity");
        }
    }
}
