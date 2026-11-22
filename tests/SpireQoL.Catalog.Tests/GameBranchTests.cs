using System.Text.RegularExpressions;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The mod supports both of the game's branches (decision #78), written for beta and accommodating main (decision #163). A game class
/// is named natively: <c>nameof(PenNib)</c> in game-facing code (a compile-time name, safe on main even for a beta-only class),
/// <c>RelicId.PenNib</c> in the simulator, a type check inside a [BetaOnly] method for a beta-only class. Only a class main alone has
/// is named by string (the build is beta's). The first test fails on a string literal naming any other game class
/// (Sim/GameNames.g.cs, GameBranches); the rest hold the beta-only parts to their gates.
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

    private static HashSet<string> NamedNatively()
    {
        var names = typeof(PowerId).GetFields().Where(field => field.FieldType == typeof(PowerId)).Select(field => ((PowerId)field.GetValue(null)!).Name)
            .Concat(typeof(RelicId).GetFields().Where(field => field.FieldType == typeof(RelicId)).Select(field => ((RelicId)field.GetValue(null)!).Name))
            .Concat(typeof(CardId).GetFields().Where(field => field.FieldType == typeof(CardId)).Select(field => ((CardId)field.GetValue(null)!).Name));
        return names.Where(name => !GameBranches.MainOnly.Contains(name)).ToHashSet();
    }

    [Fact]
    public void GameClassesAreNamedNatively()
    {
        var both = NamedNatively();
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
        Assert.True(found.Count == 0, "Game classes named by string (use nameof, or PowerId/RelicId/CardId in the simulator): " + string.Join("; ", found));
        Assert.True(!Reviewed.Keys.Except(used).Any(), "Reviewed literals no longer in the code: " + string.Join(", ", Reviewed.Keys.Except(used)));
    }

    // ------------------------------------------------------------------ written for beta, accommodating main (decisions #162, #163)

    /// <summary>
    /// The main branch's version this code was last reviewed against (docs/DEVELOPMENT.md "When main updates"). A newer main
    /// decompile fails <see cref="MainIsReviewedAtItsVersion"/> until the review is done and this is moved up.
    /// </summary>
    private const string MainReviewed = "v0.111.0";

    private static string MainDecompile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "git", "sts2-decompiled-main");

    private static readonly Lazy<SourceIndex?> MainSource = new(() => Directory.Exists(MainDecompile) ? SourceIndex.Build(MainDecompile) : null);

    /// <summary>The built mods: Spire QoL (this test's reference) and Main Saves (its newest build, when there is one).</summary>
    private static List<string> ModAssemblies()
    {
        var assemblies = new List<string> { typeof(PowerId).Assembly.Location };
        var mainSaves = new DirectoryInfo(Path.Combine(RepoRoot(), "mods", "MainSaves", "bin"));
        if (mainSaves.Exists && mainSaves.EnumerateFiles("MainSaves.dll", SearchOption.AllDirectories).MaxBy(dll => dll.LastWriteTimeUtc) is { } newest) assemblies.Add(newest.FullName);
        return assemblies;
    }

    private static readonly Lazy<List<GameMembers>?> AgainstMain = new(() =>
        MainSource.Value is { } source ? ModAssemblies().Select(assembly => GameMembers.Of(assembly, source)).ToList() : null);

    /// <summary>
    /// A game part main doesn't have (a type, or a member with that signature) is named only inside a [BetaOnly] method: anywhere
    /// else, it breaks the method holding it on main, as Explosive, Simpleton, Free Card, Bath Robes, RunModel, Holders, IsDread,
    /// the late damage multiplier and the extra turn's source did before the gates (1.27.0). Nothing to check without the main
    /// decompile (docs/DEVELOPMENT.md "Decompiled game source").
    /// </summary>
    [Fact]
    public void BetaOnlyPartsAreNamedOnlyInGates()
    {
        if (AgainstMain.Value is not { } mods) return;
        Assert.True(mods.Sum(members => members.Checked) > 500, $"only {mods.Sum(members => members.Checked)} game references found: the reader isn't seeing them");
        var outside = mods.SelectMany(members => members.Outside).ToList();
        Assert.True(outside.Count == 0, "Game parts main doesn't have, named outside a [BetaOnly] method (move them into one, called through BetaOnly.Gate): "
            + string.Join("; ", outside));
    }

    /// <summary>
    /// A gate comes off once main has what it names (decision #163: beta's workarounds don't linger): the method becomes plain code
    /// and main's fallback at its call goes.
    /// </summary>
    [Fact]
    public void GatesComeOffWhenMainCatchesUp()
    {
        if (AgainstMain.Value is not { } mods) return;
        var caughtUp = mods.SelectMany(members => members.CaughtUp).ToList();
        Assert.True(caughtUp.Count == 0, "Main has everything these [BetaOnly] methods name now: make each plain code, call it directly, and drop main's fallback: "
            + string.Join("; ", caughtUp));
    }

    /// <summary>A [BetaOnly] method is called only through BetaOnly.Gate (or from another one): a direct call would compile it on main.</summary>
    [Fact]
    public void GatedMethodsAreCalledThroughTheirGate()
    {
        var source = MainSource.Value ?? new SourceIndex();
        var direct = ModAssemblies().SelectMany(assembly => GameMembers.Of(assembly, source).CalledDirectly).ToList();
        Assert.True(direct.Count == 0, "[BetaOnly] methods called directly: " + string.Join("; ", direct));
    }

    /// <summary>Every gate opens on beta, where the mod is built: what it names resolves, so beta runs the plain code.</summary>
    [Fact]
    public void EveryGateOpensOnBeta()
    {
        GameModels.EnsureLoaded();
        var gated = typeof(PowerId).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes(typeof(Sts2Mods.Core.Game.BetaOnlyAttribute), inherit: false).Length > 0).ToList();
        Assert.True(gated.Count >= 9, $"only {gated.Count} [BetaOnly] methods found");
        var closed = gated.Where(method => !Sts2Mods.Core.Game.BetaOnly.Runs(method)).Select(method => $"{method.DeclaringType?.Name}.{method.Name}").ToList();
        Assert.True(closed.Count == 0, "[BetaOnly] methods that don't resolve on beta: " + string.Join(", ", closed));
    }

    /// <summary>
    /// The main decompile is the version this code was reviewed against: a newer one (Steam's main updated) fails until the steps
    /// in docs/DEVELOPMENT.md "When main updates" are done and <see cref="MainReviewed"/> is moved up.
    /// </summary>
    [Fact]
    public void MainIsReviewedAtItsVersion()
    {
        var version = Path.Combine(MainDecompile, "VERSION.json");
        if (!File.Exists(version)) return;
        var decompiled = System.Text.Json.JsonDocument.Parse(File.ReadAllText(version)).RootElement.GetProperty("version").GetString();
        Assert.True(decompiled == MainReviewed, $"The main decompile is {decompiled}, reviewed at {MainReviewed}: follow docs/DEVELOPMENT.md \"When main updates\", then set MainReviewed.");
    }

    /// <summary>The late damage multiplier's gate: open on beta, and 1 for a model with no late multiplier.</summary>
    [Fact]
    public void TheLateDamageMultiplierBindsOnBeta()
    {
        Assert.NotNull(SimReaders.LateMultiplier);
        GameModels.EnsureLoaded();
        var model = MegaCrit.Sts2.Core.Models.ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>();
        Assert.Equal(1m, SimReaders.LateMultiplier!.Invoke(model, null, 10m, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, null, null));
    }
}
