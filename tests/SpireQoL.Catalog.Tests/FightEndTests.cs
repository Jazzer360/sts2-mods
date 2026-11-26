using System.Reflection;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The fight's end inside a turn (decision #301), read from the game's code on both branches: each hook dispatch the simulator runs as a step reaches
/// no one once the fight is ending (its Hook method loops over IterateCombatHookListeners) unless the simulator says it still runs
/// (SimEngine.HooksAfterTheFightEnds: its Hook method loops over IterateHookListeners itself); and each game command the simulator stops for a branch
/// whose fight is over (every translated card command, every read step's command) returns at its start then (its code, or a method of its own class
/// it calls, reads CombatManager.IsEnding, IsOverOrEnding or ShouldSkipCombatEffect), or is reviewed below with why stopping it changes nothing.
/// </summary>
public class FightEndTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    private static string PathOf(string branch) => branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;

    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Commands with no guard of their own, each with why the simulator stopping it once the fight is over changes nothing it predicts.</summary>
    private static readonly Dictionary<string, string> Reviewed = new(StringComparer.Ordinal)
    {
        ["DamageCmd.Attack"] = "it builds an AttackCommand, whose ExecuteInternal returns once CombatManager.IsOverOrEnding (another class of the Commands namespace)",
        ["ArrowCmd.ShootArrowAtTarget"] = "its effects are an attack, CreatureCmd.Damage and PowerCmd.Apply (each guarded) and Hook.AfterArrowShot and AfterArrowImpaled (combat hooks: no one once the fight is ending); the arrow it spends matters to nothing after the fight",
        ["ArrowCmd.TryShootArrow"] = "the same as ShootArrowAtTarget, when you have an arrow",
        ["ArrowCmd.ImpaleTarget"] = "PowerCmd.Apply of Impaled (guarded) and Hook.AfterArrowImpaled (a combat hook)",
        ["ArrowCmd.DislodgeArrow"] = "Hook.AfterArrowDislodged (a combat hook: Twine and Yah hear nothing once the fight is ending) and the arrow back to you, which matters to nothing after the fight",
        ["CardCmd.ApplyKeyword"] = "a keyword on a card in hand (Ethereal: it exhausts as the turn ends): no turn end follows a fight that's over",
        ["ArrowCmd.GainArrowSlot"] = "an arrow slot for the fight: nothing reads it once the fight is over",
        ["CardCmd.Enchant"] = "an enchantment on a card the play makes, acting on that card's play: no play follows a fight that's over",
        ["CardCmd.ApplySingleTurnSly"] = "Sly on a card for the turn: a discard of it, which it changes, doesn't follow a fight that's over",
        ["CardCmd.ApplySingleTurnRetain"] = "Retain on a card for the turn: nothing it changes (the flush as the turn ends) follows a fight that's over",
        ["CreatureCmd.Stun"] = "the stunned creature's next move: no move follows a fight that's over",
        ["OstyCmd.Summon"] = "Osty's HP for the fight: nothing reads it once the fight is over",
        ["PowerCmd.Remove"] = "a power off a creature: no turn follows a fight that's over for it to change",
    };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryHookDispatchStopsOnceTheFightEndsAsTheGamesDoes(string branch)
    {
        if (!File.Exists(PathOf(branch))) return;
        var code = BranchCode.Of(PathOf(branch));
        var hook = code.Assembly.GetTypes().Single(type => type.FullName == "MegaCrit.Sts2.Core.Hooks.Hook");
        var wrong = new List<string>();
        foreach (var (simulated, gameHooks) in SimEngine.HookDispatches)
            foreach (var gameHook in gameHooks)
            {
                var methods = hook.GetMethods(All).Where(method => method.Name == gameHook).ToList();
                Assert.True(methods.Count > 0, $"No Hook.{gameHook} ({branch})");
                var skips = methods.All(method => Reads(code, hook, method, new HashSet<MethodBase>(), member => member.Name == "IterateCombatHookListeners"));
                var runs = SimEngine.HooksAfterTheFightEnds.ContainsKey(simulated);
                if (skips == runs) wrong.Add($"{simulated} (Hook.{gameHook}): the game's {(skips ? "reaches no one" : "reaches every listener")} once the fight is ending, the simulator's {(runs ? "runs" : "doesn't")}");
            }
        Assert.True(wrong.Count == 0, $"Hook dispatches the simulator stops differently from the game ({branch}):\n" + string.Join("\n", wrong));
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryCommandTheSimulatorStopsReturnsOnceTheFightIsEnding(string branch)
    {
        if (!File.Exists(PathOf(branch))) return;
        var code = BranchCode.Of(PathOf(branch));
        var commands = SimAdapter.CommandsTranslated.Keys.Concat(ReadModel.StoppedOnceTheFightEnds.Values).Distinct().Order(StringComparer.Ordinal);
        var unguarded = new List<string>();
        foreach (var command in commands)
        {
            var (typeName, name) = (command[..command.IndexOf('.')], command[(command.IndexOf('.') + 1)..]);
            var owner = code.Assembly.GetTypes().SingleOrDefault(type => type.Name == typeName && type.Namespace?.StartsWith("MegaCrit.Sts2.Core.Commands", StringComparison.Ordinal) == true);
            if (owner == null) continue; // a power's own command (PoisonPower.Trigger): its read steps are guarded as their commands are
            var methods = owner.GetMethods(All).Where(method => method.Name == name).ToList();
            if (methods.Count == 0) continue; // not on this branch
            if (methods.Any(method => Reads(code, owner, method, new HashSet<MethodBase>(), IsEndingGuard))) continue;
            if (!Reviewed.ContainsKey(command)) unguarded.Add(command);
        }
        Assert.True(unguarded.Count == 0, $"Commands the simulator stops once the fight is over whose code has no guard ({branch}):\n" + string.Join("\n", unguarded));
    }

    private static bool IsEndingGuard(CodeMember member) =>
        member.DeclaringType == "CombatManager" && member.Name is "get_IsEnding" or "get_IsOverOrEnding" or "get_ShouldSkipCombatEffect";

    /// <summary>The method's code (its async body), or a method of its own class (or a nested one) it calls, calls a member <paramref name="matches"/> takes.</summary>
    private static bool Reads(BranchCode code, Type owner, MethodBase method, HashSet<MethodBase> visiting, Func<CodeMember, bool> matches)
    {
        if (!visiting.Add(method)) return false;
        var body = AsyncBody(method) ?? method;
        foreach (var instruction in code.Read(body))
        {
            if (!instruction.IsCall || instruction.Member is not { Kind: CodeMemberKind.Method } member) continue;
            if (matches(member)) return true;
            if (code.Callee(body, instruction) is { } callee && Within(callee.DeclaringType, owner) && Reads(code, owner, callee, visiting, matches)) return true;
        }
        return false;
    }

    private static bool Within(Type? type, Type owner)
    {
        for (var current = type; current != null; current = current.DeclaringType)
            if (current == owner) return true;
        return false;
    }

    private static MethodBase? AsyncBody(MethodBase method)
    {
        var machine = method.GetCustomAttributesData().FirstOrDefault(attribute => attribute.AttributeType.Name == "AsyncStateMachineAttribute");
        return machine?.ConstructorArguments[0].Value is Type type ? type.GetMethod("MoveNext", All) : null;
    }
}
