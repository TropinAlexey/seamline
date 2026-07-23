using System.Reflection;
using Xunit;

namespace Seamline.ArchTests;

// CLAUDE.md: "A Contracts project may only reference SharedKernel." The
// existing ModuleBoundaryTests only checks the negative direction (Contracts
// must not depend on any implementation) — nothing stopped a Contracts
// assembly from quietly depending on another module's Contracts. Checked by
// assembly reference, not namespace, so there's no risk of the prefix
// ambiguity documented in ModuleBoundaryTests.
public class ContractsBoundaryTests
{
    private static readonly string[] ModuleNames =
    [
        "Reference", "Trading", "MarketData", "Risk", "Settlement", "Identity"
    ];

    public static IEnumerable<object[]> Modules =>
        ModuleNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Modules))]
    public void Contracts_assembly_depends_only_on_SharedKernel(string moduleName)
    {
        var assembly = Assembly.Load($"Seamline.Modules.{moduleName}.Contracts");

        var disallowed = assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null
                && name != "Seamline.SharedKernel"
                && !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft", StringComparison.Ordinal)
                && !name.StartsWith("netstandard", StringComparison.Ordinal)
                && !name.StartsWith("mscorlib", StringComparison.Ordinal))
            .ToArray();

        Assert.True(disallowed.Length == 0,
            $"{moduleName}.Contracts references assemblies beyond SharedKernel: {string.Join(", ", disallowed)}");
    }
}
