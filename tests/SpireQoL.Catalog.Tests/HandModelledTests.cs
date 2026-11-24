using System.Reflection;
using System.Text;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The method list (decision #209): every game method the simulator still follows by hand, with the decompiled Class.Method each stands in for,
/// so a game update's code diff of those methods is the review list (docs/DEVELOPMENT.md "When main updates"). Snapshots/hand-modelled.txt, written
/// with SPIREQOL_UPDATE_SNAPSHOT=1: the engine's rules (HandModelled.Engine), the adapter's per-class rules (HandModelled.Adapter), and every
/// hand-written model with its game class's hooks. Adding a SimEngine method that's neither a rule's nor plumbing fails here; so does a hand-written
/// model added or removed (the snapshot), and a game method named that neither branch has.
/// </summary>
public class HandModelledTests
{
    private const BindingFlags Declared = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>The engine's own methods, by name (local functions and compiler-made ones left out).</summary>
    private static IEnumerable<string> EngineMethods() => typeof(SimEngine).GetMethods(Declared)
        .Where(method => !method.Name.Contains('<', StringComparison.Ordinal) && !method.IsSpecialName).Select(method => "SimEngine." + method.Name).Distinct();

    [Fact]
    public void EverySimEngineMethodIsARuleOrPlumbing()
    {
        var listed = HandModelled.Engine.SelectMany(rule => rule.Simulator.Select(method => (method, rule.Rule)))
            .Concat(HandModelled.Plumbing.Select(method => (method, Rule: "plumbing"))).Where(entry => entry.method.StartsWith("SimEngine.", StringComparison.Ordinal)).ToList();
        var twice = listed.GroupBy(entry => entry.method).Where(group => group.Count() > 1).Select(group => $"{group.Key} ({string.Join("; ", group.Select(entry => entry.Rule))})").ToList();
        Assert.True(twice.Count == 0, "listed in more than one rule: " + string.Join(", ", twice));
        var names = listed.Select(entry => entry.method).ToHashSet(StringComparer.Ordinal);
        var missing = EngineMethods().Where(method => !names.Contains(method)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "SimEngine methods no rule names (HandModelled.Engine: the game methods each stands in for; or HandModelled.Plumbing): " + string.Join(", ", missing));
    }

    [Fact]
    public void EverySimulatorMethodNamedIsThere()
    {
        var types = typeof(SimEngine).Assembly.GetTypes().Where(type => type.Namespace?.StartsWith("SpireQoL", StringComparison.Ordinal) == true).ToList();
        var gone = HandModelled.Engine.Concat(HandModelled.Adapter).SelectMany(rule => rule.Simulator).Concat(HandModelled.Plumbing).Distinct()
            .Where(method => !Exists(types, method)).ToList();
        Assert.True(gone.Count == 0, "simulator methods named that aren't there: " + string.Join(", ", gone));
    }

    [Fact]
    public void EveryGameMethodNamedIsOnABranch()
    {
        if (!File.Exists(BranchCode.BetaPath)) return; // no decompile here (docs/DEVELOPMENT.md "Decompiled game source")
        var beta = BranchCode.Of(BranchCode.BetaPath);
        var main = File.Exists(BranchCode.MainPath) ? BranchCode.Of(BranchCode.MainPath) : null;
        var betaTypes = GameTypes(beta);
        var mainTypes = main == null ? new List<Type>() : GameTypes(main);
        var gone = HandModelled.Engine.Concat(HandModelled.Adapter).SelectMany(rule => rule.Game).Distinct()
            .Where(method => !Exists(betaTypes, method) && !Exists(mainTypes, method)).ToList();
        Assert.True(gone.Count == 0, "game methods named that neither branch has: " + string.Join(", ", gone));
    }

    [Fact]
    public void TheMethodListIsTheSnapshot()
    {
        if (!File.Exists(BranchCode.BetaPath) || !File.Exists(BranchCode.MainPath)) return;
        var beta = BranchCode.Of(BranchCode.BetaPath);
        var main = BranchCode.Of(BranchCode.MainPath);
        var betaTypes = GameTypes(beta);
        var mainTypes = GameTypes(main);
        string Branches(string method) => (Exists(betaTypes, method), Exists(mainTypes, method)) switch
        {
            (true, true) => method,
            (true, false) => method + " (beta only)",
            _ => method + " (main only)",
        };
        var text = new StringBuilder();
        var handWritten = SimModels.HandWrittenNames.Order(StringComparer.Ordinal).ToList();
        var engineMethods = HandModelled.Engine.SelectMany(rule => rule.Game).Distinct().Count();
        text.Append("# The game methods the simulator still follows by hand (HandModelledTests: SPIREQOL_UPDATE_SNAPSHOT=1 regenerates; decision #209).\n");
        text.Append("# A game update's diff of these methods (both branches) is what to review: each one's simulator code follows it by hand.\n");
        text.Append($"# {HandModelled.Engine.Count} engine rules naming {engineMethods} game methods; {HandModelled.Adapter.Count} adapter rules; {handWritten.Count} hand-written models.\n\n");
        text.Append("## The engine's rules (HandModelled.Engine)\n\n");
        foreach (var rule in HandModelled.Engine)
            text.Append($"{rule.Rule}\n  game: {string.Join(", ", rule.Game.Select(Branches))}\n  simulator: {string.Join(", ", rule.Simulator)}\n");
        text.Append("\n## The adapter's per-class rules (HandModelled.Adapter)\n\n");
        foreach (var rule in HandModelled.Adapter)
            text.Append($"{rule.Rule}\n  game: {string.Join(", ", rule.Game.Select(Branches))}\n  simulator: {string.Join(", ", rule.Simulator)}\n");
        text.Append("\n## The hand-written models (Sim/Models): each game class's hooks, and what its model answers\n\n");
        var classes = HookReader.Powers(beta.Assembly.GetTypes()).Concat(HookReader.Relics(beta.Assembly.GetTypes())).ToDictionary(type => type.Name, StringComparer.Ordinal);
        var mainClasses = HookReader.Powers(main.Assembly.GetTypes()).Concat(HookReader.Relics(main.Assembly.GetTypes())).ToDictionary(type => type.Name, StringComparer.Ordinal);
        foreach (var name in handWritten)
        {
            var model = SimModels.Get(name);
            var answers = model == null ? new List<string>() : model.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method => method.IsVirtual && !method.IsSpecialName && method.GetBaseDefinition().DeclaringType is { } declaring && declaring != method.DeclaringType && typeof(SimModel).IsAssignableFrom(declaring))
                .Select(method => method.Name).Distinct().Order(StringComparer.Ordinal).ToList();
            var type = classes.GetValueOrDefault(name) ?? mainClasses.GetValueOrDefault(name);
            var hooks = type == null ? "no game class by its name (the simulator's own)"
                : string.Join(", ", HookReader.Hooks(type).Select(hook => Branches($"{name}.{hook.Name}")).Distinct().Order(StringComparer.Ordinal));
            text.Append($"{name}: {(hooks.Length == 0 ? "no hooks of its own" : hooks)}\n  model: {(answers.Count == 0 ? "nothing it overrides" : string.Join(", ", answers))}\n");
        }
        var path = Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Catalog.Tests", "Snapshots", "hand-modelled.txt");
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(path, text.ToString());
            return;
        }
        Assert.True(File.Exists(path), $"missing {path}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        Assert.True(File.ReadAllText(path).ReplaceLineEndings("\n") == text.ToString(),
            $"{path} isn't the method list: a hand-written model or a rule changed; regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1 and explain the change");
    }

    /// <summary>The game's types on a branch (any namespace: the hooks, the commands, the models).</summary>
    private static List<Type> GameTypes(BranchCode code) => code.Assembly.GetTypes().Where(type => type.Namespace?.StartsWith("MegaCrit", StringComparison.Ordinal) == true && !type.IsNested).ToList();

    /// <summary>A Type.Member among the types: a type by that name with a member by that name, its own or a base class's.</summary>
    private static bool Exists(IEnumerable<Type> types, string method)
    {
        var dot = method.LastIndexOf('.');
        var (typeName, member) = (method[..dot], method[(dot + 1)..]);
        foreach (var type in types.Where(candidate => candidate.Name == typeName))
            for (var current = type; current != null; current = current.BaseType)
            {
                try
                {
                    if (current.GetMember(member, Declared).Length > 0) return true;
                }
                catch (FileNotFoundException)
                {
                    break;
                }
            }
        return false;
    }
}
