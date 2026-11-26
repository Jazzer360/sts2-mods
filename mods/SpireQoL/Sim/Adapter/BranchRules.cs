using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireQoL.Sim;

/// <summary>
/// A fact about the running game's code: <see cref="Method"/>, declared on <see cref="Type"/>, names <see cref="Mentions"/> (a type, or a
/// member, by its name) in its body, an async method's state machine included; with no <see cref="Mentions"/>, the type declares the method.
/// </summary>
public sealed record CodeFact(Type Type, string Method, string? Mentions);

/// <summary>
/// A game rule whose code differs between Steam's branches (decision #169), told from the running game's own code (<see cref="Fact"/>, as
/// it is on beta: <see cref="OnBeta"/>), never from its version. <see cref="Sim"/>: the simulator's flag for main's behaviour (SimState.MainRules),
/// or none for a rule the adapter applies itself. The branch checks (GameBranchTests) read every fact on both branches' assemblies: one both
/// branches agree on fails them, so the rule, and main's path with it, comes out once main has caught up.
/// </summary>
public sealed record BranchRule(string Name, CodeFact Fact, bool OnBeta, MainRule Sim, string Why);

/// <summary>Every rule whose game code differs between the branches (decision #169), and which behaviour the running game has.</summary>
internal static class BranchRules
{
    public static readonly BranchRule OmnisliceSplashIsPowered = new(nameof(OmnisliceSplashIsPowered),
        new CodeFact(typeof(ThornsPower), nameof(ThornsPower.BeforeDamageReceived), nameof(Omnislice)), OnBeta: false, MainRule.SplashIsUnpowered,
        "beta's Omnislice splashes a powered hit (ValueProp.Move) with only the damage caps; main's splash is unpowered (Unpowered | Move) through " +
        "every damage modifier, and main's ThornsPower answers it anyway (cardSource is Omnislice): told by that Thorns case, made in the same change");

    public static readonly BranchRule DamageWorkedOutFirst = new(nameof(DamageWorkedOutFirst),
        new CodeFact(typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd), "GetDamageResults", Mentions: null), OnBeta: true, MainRule.ReceivedBeforeTheResults,
        "beta's CreatureCmd.Damage works out what a damage does first (GetDamageResults: its redirects, block and HP loss, a dry run), then tells " +
        "Hook.BeforeDamageReceived, then applies the results and tells the HP-loss changes (ApplyDamageResults); main's tells BeforeDamageReceived first and " +
        "each HP-loss change as it's made: a Thorns kill whose death hits back (Explosive) meets the hit's Buffer and block before it, or after it");

    public static readonly BranchRule DebuffTickSkippedOnTheEnemiesTurnOnly = new(nameof(DebuffTickSkippedOnTheEnemiesTurnOnly),
        new CodeFact(typeof(MegaCrit.Sts2.Core.Commands.PowerCmd), nameof(MegaCrit.Sts2.Core.Commands.PowerCmd.Apply), "get_CurrentSide"), OnBeta: true, MainRule.DebuffSkipsTickOnAnyTurn,
        "beta's PowerCmd.Apply sets a new debuff's SkipNextDurationTick on a creature of the players' side only during the enemies' turn " +
        "(combatState.CurrentSide == CombatSide.Enemy); main's sets it whenever one lands there, so a Weak, Vulnerable or Frail landing on you during " +
        "your own turn keeps its stack through the enemies' turn end on main and ticks down there on beta");

    public static IReadOnlyList<BranchRule> All { get; } = new[]
    {
        OmnisliceSplashIsPowered, DamageWorkedOutFirst, DebuffTickSkippedOnTheEnemiesTurnOnly,
    };

    private static readonly ConcurrentDictionary<BranchRule, bool> Beta = new();

    /// <summary>The running game has beta's behaviour for this rule: its fact is as it is on beta (main's otherwise).</summary>
    public static bool BetaBehaviour(BranchRule rule) => Beta.GetOrAdd(rule, branchRule => Holds(branchRule.Fact) == branchRule.OnBeta);

    /// <summary>The simulator's flags for main's behaviour in the running game (SimState.MainRules).</summary>
    /// <remarks>Worked out once: the running game's code doesn't change, and it's read on every board read.</remarks>
    public static MainRule MainRulesNow() => MainRules.Value;

    /// <summary>Worked out once by whichever thread asks first (the load's worker: SimAdapter.ReadFightCode), never half-written for another.</summary>
    private static readonly Lazy<MainRule> MainRules = new(() => All.Where(rule => rule.Sim != MainRule.None && !BetaBehaviour(rule))
        .Aggregate(MainRule.None, (flags, rule) => flags | rule.Sim), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Whether the fact holds in the running game's code (false when its method isn't there).</summary>
    internal static bool Holds(CodeFact fact)
    {
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var methods = fact.Type.GetMethods(declared).Where(method => method.Name == fact.Method).ToList();
        if (fact.Mentions == null) return methods.Count > 0;
        foreach (var method in methods)
        {
            // A generic method's body (PowerCmd.Apply<T>'s state machine, a generic type) can't be read here: a fact names what a plain overload says.
            if (method.IsGenericMethodDefinition) continue;
            var body = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                       ?? (MethodBase)method;
            if (Diagnostics.GameCode.Instructions(body).Any(instruction => Names(instruction.operand, fact.Mentions))) return true;
        }
        return false;
    }

    /// <summary>An instruction's operand names this type or member: itself, its declaring type, or a generic argument.</summary>
    private static bool Names(object? operand, string name) => operand switch
    {
        Type type => type.Name == name || type.IsGenericType && type.GetGenericArguments().Any(argument => argument.Name == name),
        MethodBase method => method.Name == name || method.DeclaringType?.Name == name
                             || method is MethodInfo { IsGenericMethod: true } generic && generic.GetGenericArguments().Any(argument => argument.Name == name),
        FieldInfo field => field.Name == name || field.DeclaringType?.Name == name,
        _ => false,
    };
}
