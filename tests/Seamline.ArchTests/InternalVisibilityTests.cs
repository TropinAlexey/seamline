using System.Reflection;
using Xunit;

namespace Seamline.ArchTests;

// CLAUDE.md's "internal by default" claim currently rests on the RootNamespace
// trick plus developer discipline alone — nothing stops a domain type from
// accidentally being declared `public`. This checks the actual exported
// surface of each module's implementation assembly against an explicit,
// narrow whitelist: DI/endpoint composition-root entry points, and EF Core
// migrations (which the tooling requires to be public).
public class InternalVisibilityTests
{
    private static readonly string[] ModuleNames =
    [
        "Reference", "Trading", "MarketData", "Risk", "Settlement", "Identity", "Audit"
    ];

    public static IEnumerable<object[]> Modules =>
        ModuleNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_implementation_exposes_only_the_composition_root_surface(string moduleName)
    {
        var assembly = Assembly.Load($"Seamline.Modules.{moduleName}");
        var internalNamespace = $"Seamline.Modules.{moduleName}.Internal";

        var violations = assembly.GetExportedTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith(internalNamespace, StringComparison.Ordinal))
            .Where(t => !IsAllowedPublicType(t))
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(violations.Length == 0,
            $"{moduleName} exposes public types beyond the composition-root surface: {string.Join(", ", violations)}");
    }

    private static bool IsAllowedPublicType(Type type)
    {
        // DI/endpoint registration — the module's intended entry points.
        if (type.Name.EndsWith("ModuleExtensions", StringComparison.Ordinal))
            return true;
        if (type.Name.EndsWith("Endpoints", StringComparison.Ordinal))
            return true;

        // EF Core migrations and model snapshots — generated, and the
        // tooling expects them public.
        if (type.BaseType?.Name == "Migration")
            return true;
        if (type.Name.EndsWith("ModelSnapshot", StringComparison.Ordinal))
            return true;

        return false;
    }
}
