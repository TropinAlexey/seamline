using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Seamline.ArchTests;

// Each module's implementation lives in the "<Module>.Internal" namespace, not
// "<Module>" directly, so that this namespace prefix never collides with
// "<Module>.Contracts" — NetArchTest's dependency match is prefix-based, and
// "Trading" is itself a string-prefix of "Trading.Contracts".
public class ModuleBoundaryTests
{
    private static readonly string[] ModuleNames =
    [
        "Reference", "Trading", "MarketData", "Risk", "Settlement", "Identity"
    ];

    public static IEnumerable<object[]> Modules =>
        ModuleNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_implementation_must_not_depend_on_another_modules_implementation(string moduleName)
    {
        var assembly = Assembly.Load($"Seamline.Modules.{moduleName}");

        var otherModuleImplNamespaces = ModuleNames
            .Where(name => name != moduleName)
            .Select(name => $"Seamline.Modules.{name}.Internal")
            .ToArray();

        var result = Types.InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(otherModuleImplNamespaces)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"{moduleName} depends directly on another module's implementation: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Contracts_assembly_must_not_depend_on_any_implementation(string moduleName)
    {
        var assembly = Assembly.Load($"Seamline.Modules.{moduleName}.Contracts");

        var allImplNamespaces = ModuleNames
            .Select(name => $"Seamline.Modules.{name}.Internal")
            .ToArray();

        var result = Types.InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(allImplNamespaces)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"{moduleName}.Contracts depends on an implementation assembly: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}
