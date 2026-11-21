using System.Text.RegularExpressions;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The mod supports both of the game's branches (decision #78). A class only one branch has can only be named by string where the other
/// branch needs to compile; that's a workaround with a shelf life. Once both branches have the class, the code names it natively:
/// <c>nameof(PenNib)</c> or a type check in game-facing code, <c>RelicId.PenNib</c> in the simulator. This fails on a string literal naming
/// a class both branches have (Sim/GameNames.g.cs, GameBranches), so each workaround is cleaned up as soon as the game catches up.
/// </summary>
public class GameBranchTests
{
    /// <summary>Tables that list game classes by name on purpose (reviews over every class, on either branch).</summary>
    private static readonly Regex ReviewTables = new(@"CoverageLedger|RelicTracking|RelicChoiceTracking|ChoiceReminders|SettingsCatalog|CardAffinity|RelicSpecs|GameNames\.g\.cs");

    /// <summary>A literal inside these calls is a value's or a private member's name that can match a class's (DynamicVars["StrengthPower"]).</summary>
    private static readonly Regex KeyCall = new(@"\b(Var|TryGetValue|Private|Internal|GetValue)\([^()""]*$");

    /// <summary>Literals that name something else that shares a class's name: the text, and why.</summary>
    private static readonly Dictionary<string, string> Reviewed = new()
    {
        ["(\"Juggernaut\", j)"] = "Incoming Damage's breakdown label (what the player reads), not the class",
    };

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "mods")) && Directory.Exists(Path.Combine(d.FullName, "core"))) return d.FullName;
        throw new DirectoryNotFoundException("the repository root (mods/ and core/) not found above the test's folder");
    }

    private static HashSet<string> OnBothBranches()
    {
        var names = typeof(PowerId).GetFields().Where(f => f.FieldType == typeof(PowerId)).Select(f => ((PowerId)f.GetValue(null)!).Name)
            .Concat(typeof(RelicId).GetFields().Where(f => f.FieldType == typeof(RelicId)).Select(f => ((RelicId)f.GetValue(null)!).Name))
            .Concat(typeof(CardId).GetFields().Where(f => f.FieldType == typeof(CardId)).Select(f => ((CardId)f.GetValue(null)!).Name));
        return names.Where(n => !GameBranches.BetaOnly.Contains(n) && !GameBranches.MainOnly.Contains(n)).ToHashSet();
    }

    [Fact]
    public void ClassesBothBranchesHaveAreNamedNatively()
    {
        var both = OnBothBranches();
        var found = new List<string>();
        var used = new HashSet<string>();
        var root = RepoRoot();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "mods"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/") || ReviewTables.IsMatch(rel)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;
                foreach (Match m in Regex.Matches(line, "\"(\\w+)\""))
                {
                    if (!both.Contains(m.Groups[1].Value) || KeyCall.IsMatch(line[..m.Index])) continue;
                    if (Reviewed.Keys.FirstOrDefault(line.Contains) is { } key) { used.Add(key); continue; }
                    found.Add($"{rel}:{i + 1}: {m.Value}");
                }
            }
        }
        Assert.True(found.Count == 0, "Classes both branches have, named by string (use nameof, a type check, or PowerId/RelicId/CardId): " + string.Join("; ", found));
        Assert.True(!Reviewed.Keys.Except(used).Any(), "Reviewed literals no longer in the code: " + string.Join(", ", Reviewed.Keys.Except(used)));
    }
}
