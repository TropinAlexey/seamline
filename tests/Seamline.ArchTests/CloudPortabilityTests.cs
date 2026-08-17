using System.Reflection;
using Xunit;

namespace Seamline.ArchTests;

// ADR-0021: domain and module assemblies must not depend on any cloud SDK.
// Positive selection by ModuleNames — hosts and composition roots are not
// loaded, not listed, and not excluded. A future host (e.g. Valuation.Function)
// does not require updating this test.
public class CloudPortabilityTests
{
    private static readonly string[] ModuleNames =
    [
        "Reference", "Trading", "MarketData", "Risk", "Settlement", "Identity", "Audit"
    ];

    private static readonly string[] CloudSdkPrefixes = ["AWSSDK", "Azure."];

    public static IEnumerable<object[]> AllAssemblies()
    {
        yield return ["Seamline.SharedKernel"];

        foreach (var module in ModuleNames)
        {
            yield return [$"Seamline.Modules.{module}"];
            yield return [$"Seamline.Modules.{module}.Contracts"];
        }
    }

    [Theory]
    [MemberData(nameof(AllAssemblies))]
    public void Assembly_must_not_reference_cloud_SDKs(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var cloudDeps = assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null
                && CloudSdkPrefixes.Any(prefix =>
                    name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(cloudDeps.Length == 0,
            $"{assemblyName} references cloud SDK assemblies: {string.Join(", ", cloudDeps)}. " +
            "Cloud dependencies belong in composition roots (Api, Workers, Functions), " +
            "not in domain or module code (ADR-0021).");
    }
}
