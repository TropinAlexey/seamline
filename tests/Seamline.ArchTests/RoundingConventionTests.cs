using System.Reflection;
using Xunit;

namespace Seamline.ArchTests;

// ADR-0007: rounding is explicit at the point it happens. Every Math.Round
// call must specify MidpointRounding to avoid the default (ToEven in .NET,
// but the rule is about being explicit, not about choosing a specific mode).
public class RoundingConventionTests
{
    private static readonly string[] ModuleNames =
    [
        "Reference", "Trading", "MarketData", "Risk", "Settlement", "Identity", "Audit"
    ];

    public static IEnumerable<object[]> Modules =>
        ModuleNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Modules))]
    public void Math_Round_must_specify_MidpointRounding(string moduleName)
    {
        var assembly = Assembly.Load($"Seamline.Modules.{moduleName}");
        var violations = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            if (type.Namespace?.Contains(".Migrations", StringComparison.Ordinal) == true)
                continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                byte[]? il;
                try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                catch { continue; }
                if (il is null) continue;

                // Math.Round overloads WITHOUT MidpointRounding:
                //   Round(decimal)              — 1 param
                //   Round(decimal, int)          — 2 params
                //   Round(double)               — 1 param
                //   Round(double, int)           — 2 params
                // Overloads WITH MidpointRounding have 3 params.
                // Check via reflection if the method calls any 1- or 2-param Round.
                var roundMethods = typeof(Math).GetMethods()
                    .Where(m => m.Name == "Round"
                        && m.GetParameters().Length <= 2
                        && m.GetParameters().All(p => p.ParameterType != typeof(MidpointRounding)));

                foreach (var roundMethod in roundMethods)
                {
                    var token = roundMethod.MetadataToken;
                    if (IlContainsToken(il, token))
                        violations.Add($"{type.FullName}.{method.Name}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"{moduleName} calls Math.Round without explicit MidpointRounding (ADR-0007): " +
            string.Join(", ", violations));
    }

    private static bool IlContainsToken(byte[] il, int token)
    {
        var tokenBytes = BitConverter.GetBytes(token);
        for (var i = 0; i < il.Length - 4; i++)
        {
            // call (0x28) or callvirt (0x6F) opcode followed by 4-byte token
            if ((il[i] == 0x28 || il[i] == 0x6F)
                && il[i + 1] == tokenBytes[0] && il[i + 2] == tokenBytes[1]
                && il[i + 3] == tokenBytes[2] && il[i + 4] == tokenBytes[3])
                return true;
        }
        return false;
    }
}
