using System.Reflection;
using Xunit;

namespace Seamline.ArchTests;

// CLAUDE.md: "Always decimal. Never double or float, anywhere in the domain
// model... including intermediate calculation values." Stated as a hard
// rule but never actually checked — this closes that gap. Reflection can see
// internal members without InternalsVisibleTo; C# accessibility isn't a CLR
// enforcement boundary for reflection.
public class DecimalUsageTests
{
    private static readonly string[] ModuleNames =
    [
        "Reference", "Trading", "MarketData", "Risk", "Settlement", "Identity", "Audit"
    ];

    public static IEnumerable<object[]> Modules =>
        ModuleNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_implementation_never_uses_double_or_float(string moduleName)
    {
        var assembly = Assembly.Load($"Seamline.Modules.{moduleName}");
        var internalNamespace = $"Seamline.Modules.{moduleName}.Internal";

        var violations = new List<string>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (var type in assembly.GetTypes())
        {
            if (type.Namespace is null || !type.Namespace.StartsWith(internalNamespace, StringComparison.Ordinal))
                continue;
            if (type.Namespace.Contains(".Migrations", StringComparison.Ordinal))
                continue;

            foreach (var property in type.GetProperties(flags))
            {
                if (IsFloatingPoint(property.PropertyType))
                    violations.Add($"{type.FullName}.{property.Name} ({property.PropertyType.Name})");
            }

            foreach (var field in type.GetFields(flags))
            {
                if (IsFloatingPoint(field.FieldType))
                    violations.Add($"{type.FullName}.{field.Name} ({field.FieldType.Name})");
            }
        }

        Assert.True(violations.Count == 0,
            $"{moduleName} uses double/float where decimal is required: {string.Join(", ", violations)}");
    }

    private static bool IsFloatingPoint(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(double) || underlying == typeof(float);
    }
}
