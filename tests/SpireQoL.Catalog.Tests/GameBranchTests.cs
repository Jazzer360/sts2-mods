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
    private static readonly Regex ReviewTables = new(@"CoverageLedger|RelicTracking|RelicChoiceTracking|ModelEffectCheck|ChoiceReminders|SettingsCatalog|CardAffinity|RelicSpecs|GameNames\.g\.cs");

    /// <summary>A literal inside these calls is a value's or a private member's name that can match a class's (DynamicVars["StrengthPower"]).</summary>
    private static readonly Regex KeyCall = new(@"\b(Var|TryGetValue|Private|Internal|GetValue)\([^()""]*$");

    /// <summary>A class's nameof where a value's key goes: Var(card, nameof(StrengthPower)).</summary>
    private static readonly Regex KeyNameof = new(@"\b(Var|TryGetValue|Private|Internal)\([^()]*nameof\(");

    /// <summary>Literals that name something else that shares a class's name: the text, and why.</summary>
    private static readonly Dictionary<string, string> Reviewed = new()
    {
        ["EndOfTurnDamage.Add((\"Juggernaut\", "] = "Incoming Damage's breakdown label (what the player reads), not the class",
        ["\"Accelerant\" }; // DynamicVars keys"] = "a card's DynamicVars key (SimAdapter.PlainPowerVars), not the Accelerant card",
    };

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "mods")) && Directory.Exists(Path.Combine(directory.FullName, "core"))) return directory.FullName;
        throw new DirectoryNotFoundException("the repository root (mods/ and core/) not found above the test's folder");
    }

    private static HashSet<string> OnBothBranches()
    {
        var names = typeof(PowerId).GetFields().Where(field => field.FieldType == typeof(PowerId)).Select(field => ((PowerId)field.GetValue(null)!).Name)
            .Concat(typeof(RelicId).GetFields().Where(field => field.FieldType == typeof(RelicId)).Select(field => ((RelicId)field.GetValue(null)!).Name))
            .Concat(typeof(CardId).GetFields().Where(field => field.FieldType == typeof(CardId)).Select(field => ((CardId)field.GetValue(null)!).Name));
        return names.Where(name => !GameBranches.BetaOnly.Contains(name) && !GameBranches.MainOnly.Contains(name)).ToHashSet();
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
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (Match match in Regex.Matches(line, "\"(\\w+)\""))
                {
                    if (!both.Contains(match.Groups[1].Value) || KeyCall.IsMatch(line[..match.Index])) continue;
                    if (Reviewed.Keys.FirstOrDefault(line.Contains) is { } key) { used.Add(key); continue; }
                    found.Add($"{rel}:{i + 1}: {match.Value}");
                }
                // The other way round: a value's key written as a class (DynamicVars["PoisonPower"] is the value's name, not the class).
                if (KeyNameof.IsMatch(line)) found.Add($"{rel}:{i + 1}: a DynamicVars or member key written with nameof (keep the key a string)");
            }
        }
        Assert.True(found.Count == 0, "Classes both branches have, named by string (use nameof, a type check, or PowerId/RelicId/CardId): " + string.Join("; ", found));
        Assert.True(!Reviewed.Keys.Except(used).Any(), "Reviewed literals no longer in the code: " + string.Join(", ", Reviewed.Keys.Except(used)));
    }

    /// <summary>
    /// Every game type and member the built mods name exists on the main branch too (decompiled to ~/git/sts2-decompiled-main, the
    /// version in its VERSION.json; nothing to check where it isn't): a reference main lacks breaks the whole method holding it
    /// there, as Explosive, Simpleton, Free Card, Bath Robes, RunModel, Holders, IsDread, the late damage multiplier and the extra
    /// turn's source once did. Read a one-branch member by name instead (SimReaders.PowerOf, a name check, reflection).
    /// </summary>
    [Fact]
    public void EveryGameMemberTheModsNameIsOnMain()
    {
        var main = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "git", "sts2-decompiled-main");
        if (!Directory.Exists(main)) return; // no main decompile here (docs/DEVELOPMENT.md "Decompiled game source")
        var source = SourceIndex.Build(main);
        var assemblies = new List<string> { typeof(PowerId).Assembly.Location };
        var mainSaves = new DirectoryInfo(Path.Combine(RepoRoot(), "mods", "MainSaves", "bin"));
        if (mainSaves.Exists && mainSaves.EnumerateFiles("MainSaves.dll", SearchOption.AllDirectories).MaxBy(dll => dll.LastWriteTimeUtc) is { } newest) assemblies.Add(newest.FullName);
        var missing = new List<string>();
        var total = 0;
        foreach (var assembly in assemblies)
        {
            missing.AddRange(GameMembers.Missing(assembly, source, out var checkedCount));
            total += checkedCount;
        }
        Assert.True(total > 500, $"only {total} game references found: the reader isn't seeing them");
        Assert.True(missing.Count == 0, $"Game members the mod names that main ({File.ReadAllText(Path.Combine(main, "VERSION.json")).Split('\"')[3]}) doesn't have: " + string.Join("; ", missing));
    }

    /// <summary>The late damage multiplier, called through a delegate so main (no such hook) still runs: it binds on beta and gives 1 by default.</summary>
    [Fact]
    public void TheLateDamageMultiplierBindsOnBeta()
    {
        Assert.NotNull(SimReaders.LateMultiplier);
        GameModels.EnsureLoaded();
        var model = MegaCrit.Sts2.Core.Models.ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>();
        Assert.Equal(1m, SimReaders.LateMultiplier!.Invoke(model, null, 10m, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, null, null, null));
    }
}
