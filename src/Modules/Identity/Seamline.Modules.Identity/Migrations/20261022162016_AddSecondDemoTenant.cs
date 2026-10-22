using Microsoft.EntityFrameworkCore.Migrations;
using Seamline.Modules.Identity.Contracts;

#nullable disable

namespace Seamline.Modules.Identity.Internal.Migrations
{
    /// <inheritdoc />
    public partial class AddSecondDemoTenant : Migration
    {
        // A second tenant with the same three logins, so the web UI can show
        // tenant isolation (ADR-0005, ADR-0030): log in as `trader` in each
        // tenant and see two disjoint blotters. Same password as the first
        // demo tenant — this is a demo, and the password is documented.
        private const string SecondDemoTenantId = "22222222-2222-2222-2222-222222222222";
        private const string DemoPassword = "Demo-Password-123!";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Runs as the owner role, which bypasses RLS — same as the seed
            // in InitialCreate, so no app.tenant_id has to be set first.
            InsertDemoUser(migrationBuilder, "trader", IdentityRoles.FrontOffice);
            InsertDemoUser(migrationBuilder, "risk", IdentityRoles.MiddleOffice);
            InsertDemoUser(migrationBuilder, "backoffice", IdentityRoles.BackOffice);
        }

        private static void InsertDemoUser(MigrationBuilder migrationBuilder, string login, string role)
        {
            var passwordHash = PasswordHasher.Hash(DemoPassword);
            migrationBuilder.Sql($"""
                INSERT INTO identity."user" ("Id", tenant_id, login, password_hash, role, created_at)
                VALUES (gen_random_uuid(), '{SecondDemoTenantId}', '{login}', '{passwordHash}', '{role}', now());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""DELETE FROM identity."user" WHERE tenant_id = '{SecondDemoTenantId}';""");
        }
    }
}
