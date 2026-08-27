using System.Reflection;
using Xunit;

namespace Seamline.ArchTests;

// ADR-0008: a saga belongs to the module that owns the aggregate whose
// lifecycle it drives. No dedicated Sagas project, no saga in the wrong module.
// Also: no standalone Sagas assembly in the solution.
public class SagaPlacementTests
{
    private static readonly string[] ModuleNames =
    [
        "Reference", "Trading", "MarketData", "Risk", "Settlement", "Identity", "Audit"
    ];

    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void No_dedicated_Sagas_project_exists()
    {
        var sagaProjects = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => Path.GetFileNameWithoutExtension(p).Contains("Saga", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetRelativePath(RepoRoot, p))
            .ToArray();

        Assert.True(sagaProjects.Length == 0,
            $"Sagas must live inside the owning module, not in a dedicated project (ADR-0008): {string.Join(", ", sagaProjects)}");
    }

    [Fact]
    public void Saga_types_live_only_in_module_implementation_assemblies()
    {
        var violations = new List<string>();

        // Check Contracts assemblies — sagas must not be there
        foreach (var moduleName in ModuleNames)
        {
            var assembly = Assembly.Load($"Seamline.Modules.{moduleName}.Contracts");
            var sagaTypes = assembly.GetTypes()
                .Where(t => t.GetInterfaces().Any(i =>
                    i.FullName?.Contains("SagaStateMachineInstance", StringComparison.Ordinal) == true
                    || i.FullName?.Contains("ISaga", StringComparison.Ordinal) == true))
                .ToArray();

            foreach (var type in sagaTypes)
                violations.Add($"{type.FullName} in Contracts assembly");
        }

        // Check host assemblies — sagas must not be there either
        foreach (var hostName in new[] { "Seamline.Api", "Seamline.Valuation.Worker", "Seamline.Reporting.Worker" })
        {
            Assembly assembly;
            try { assembly = Assembly.Load(hostName); }
            catch { continue; }

            var sagaTypes = assembly.GetTypes()
                .Where(t => t.GetInterfaces().Any(i =>
                    i.FullName?.Contains("SagaStateMachineInstance", StringComparison.Ordinal) == true
                    || i.FullName?.Contains("ISaga", StringComparison.Ordinal) == true))
                .ToArray();

            foreach (var type in sagaTypes)
                violations.Add($"{type.FullName} in host assembly {hostName}");
        }

        Assert.True(violations.Count == 0,
            $"Saga types must live in module impl assemblies only (ADR-0008): {string.Join(", ", violations)}");
    }
}
