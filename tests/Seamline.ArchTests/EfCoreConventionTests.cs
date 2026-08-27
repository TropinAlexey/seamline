using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Seamline.ArchTests;

// CLAUDE.md (global): any HasDefaultValueSql / HasDefaultValue must be paired
// with ValueGeneratedNever() when the column is not truly DB-generated.
// Scans fluent configuration source files rather than the compiled model —
// the compiled model loses the distinction between "intentionally generated"
// and "accidentally generated" because both set ValueGenerated.OnAdd.
public partial class EfCoreConventionTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [GeneratedRegex(@"\.HasDefaultValue(?:Sql)?\s*\(")]
    private static partial Regex HasDefaultValuePattern();

    [Fact]
    public void HasDefaultValue_must_be_paired_with_ValueGeneratedNever()
    {
        var violations = new List<string>();
        var srcDir = Path.Combine(RepoRoot, "src");

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!HasDefaultValuePattern().IsMatch(lines[i]))
                    continue;

                // Look in a 5-line window around the call for ValueGeneratedNever
                var window = string.Join("\n", lines.Skip(Math.Max(0, i - 2)).Take(7));
                if (!window.Contains("ValueGeneratedNever", StringComparison.Ordinal))
                {
                    var relativePath = Path.GetRelativePath(RepoRoot, file);
                    violations.Add($"{relativePath}:{i + 1}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "HasDefaultValueSql/HasDefaultValue without ValueGeneratedNever() — " +
            "EF will silently skip INSERT for CLR-default values (e.g. false for bool). " +
            $"Violations: {string.Join(", ", violations)}");
    }
}
