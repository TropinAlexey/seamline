using System.Xml.Linq;
using Xunit;

namespace Seamline.ArchTests;

// ADR-0009: MassTransit pinned to 8.5.10 — 9.x requires a commercial license.
// CLAUDE.md: no MediatR — endpoints call services directly via DI.
public class DependencyVersionTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void MassTransit_must_be_pinned_to_8_5_10()
    {
        var violations = new List<string>();

        foreach (var csproj in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var doc = XDocument.Load(csproj);
            var packages = doc.Descendants("PackageReference")
                .Where(e => e.Attribute("Include")?.Value.StartsWith("MassTransit", StringComparison.Ordinal) == true);

            foreach (var pkg in packages)
            {
                var version = pkg.Attribute("Version")?.Value;
                if (version != "8.5.10")
                    violations.Add($"{Path.GetFileName(csproj)}: {pkg.Attribute("Include")!.Value} = {version}");
            }
        }

        Assert.True(violations.Count == 0,
            $"MassTransit must be pinned to 8.5.10 (ADR-0009, 9.x is commercial): {string.Join(", ", violations)}");
    }

    [Fact]
    public void No_project_references_MediatR()
    {
        var violations = new List<string>();

        foreach (var csproj in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var doc = XDocument.Load(csproj);
            var packages = doc.Descendants("PackageReference")
                .Where(e => e.Attribute("Include")?.Value.StartsWith("MediatR", StringComparison.OrdinalIgnoreCase) == true);

            foreach (var pkg in packages)
                violations.Add($"{Path.GetFileName(csproj)}: {pkg.Attribute("Include")!.Value}");
        }

        Assert.True(violations.Count == 0,
            $"MediatR is banned — use MassTransit for cross-module events, DI for in-process calls: {string.Join(", ", violations)}");
    }
}
