using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Seamline.ArchTests;

// CLAUDE.md: "No foreign keys across module schemas in PostgreSQL.
// Referential integrity between modules is eventual, via events, not
// enforced at the database level." That's a statement about generated SQL,
// not about C# dependencies — ModuleBoundaryTests can't see it. This runs
// each migration's Up() against a real MigrationBuilder and inspects the
// resulting operations directly, rather than text-scanning the generated
// C# (which would break the moment someone reformats a migration file).
public class SchemaIsolationTests
{
    private static readonly string[] ModuleNames =
    [
        "Reference", "Trading", "MarketData", "Risk", "Settlement", "Identity"
    ];

    public static IEnumerable<object[]> Modules =>
        ModuleNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Modules))]
    public void Migrations_never_add_a_foreign_key_across_schemas(string moduleName)
    {
        var assembly = Assembly.Load($"Seamline.Modules.{moduleName}");

        var migrationTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.BaseType?.Name == "Migration");

        var violations = new List<string>();

        foreach (var migrationType in migrationTypes)
        {
            var migration = (Migration)Activator.CreateInstance(migrationType)!;
            var builder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");

            var upMethod = migrationType.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            upMethod.Invoke(migration, [builder]);

            // FKs show up two ways: a standalone AddForeignKeyOperation, or
            // nested inside a CreateTableOperation when declared inline via
            // `constraints: table => table.ForeignKey(...)` — which is how
            // every migration in this repo declares them. Missing the
            // second form would make this test pass vacuously.
            var standalone = builder.Operations.OfType<AddForeignKeyOperation>();
            var nested = builder.Operations.OfType<CreateTableOperation>().SelectMany(op => op.ForeignKeys);

            foreach (var operation in standalone.Concat(nested))
            {
                var tableSchema = operation.Schema ?? "public";
                var principalSchema = operation.PrincipalSchema ?? "public";

                if (tableSchema != principalSchema)
                {
                    violations.Add(
                        $"{migrationType.FullName}: FK \"{operation.Name}\" on {tableSchema}.{operation.Table} " +
                        $"references {principalSchema}.{operation.PrincipalTable}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"{moduleName} has a migration that adds a cross-schema foreign key: {string.Join(", ", violations)}");
    }
}
