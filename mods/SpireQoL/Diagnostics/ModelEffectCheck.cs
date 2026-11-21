using System.Reflection;
using System.Reflection.Emit;
using SpireQoL.Sim;
using SpireQoL.Sim.Models;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Models that fire their tab but do nothing their game class does (decision #156; a report: Mini Regent's model fired, and its
/// Strength never landed). For each relic and power model: the commands its game class's hooks (After*, Before*) call that change a
/// fight, against what the model's own code does in the simulation (an effect call, or a change to the state). A model that changes
/// nothing while its game class does is reported, unless it's reviewed here with why. ModelEffectTests runs it on the installed game.
/// </summary>
internal static class ModelEffectCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Game commands that change a fight.</summary>
    private static readonly HashSet<string> GameEffects = new()
    {
        "PowerCmd.Apply", "PowerCmd.ModifyAmount", "PlayerCmd.GainEnergy", "PlayerCmd.GainStars", "CreatureCmd.GainBlock", "CreatureCmd.Damage",
        "CreatureCmd.Heal", "CreatureCmd.Stun", "CreatureCmd.Kill", "CardPileCmd.Draw", "CardPileCmd.AddGeneratedCardToCombat",
        "CardPileCmd.AddGeneratedCardsToCombat", "CardPileCmd.AddToCombatAndPreview", "OrbCmd.Channel",
    };

    /// <summary>SimEngine members a model reads, or keeps its own count with: not an effect.</summary>
    private static readonly HashSet<string> SimReads = new()
    {
        "PowersOf", "PowerOn", "RelicsOf", "TurnOf", "Var", "Title", "TallyFor", "TakesPart", "IsImpaled", "CalcDamage", "FreshOrb", "SetPowers", "SetRelics",
    };

    /// <summary>The models' own effect helpers (ProcModel, RelicProc): each changes the simulation.</summary>
    private static readonly HashSet<string> Helpers = new() { "Block", "DamageAll", "DamageRandom", "Cause", "MakeInHand", "Channel", "RelicBlock", "RelicDamageAll", "RelicDamageRandom" };

    /// <summary>The simulation's state: a write to one of these is an effect.</summary>
    private static readonly HashSet<Type> StateTypes = new() { typeof(SimState), typeof(SimPlayer), typeof(SimEnemy) };

    /// <summary>The models' framework (helpers, not a model's own code).</summary>
    private static readonly HashSet<Type> Framework = new() { typeof(SimModel), typeof(ProcModel), typeof(RelicProc) };

    /// <summary>
    /// Reviewed: models that change nothing in the simulation on purpose, and why; "gap:" for an effect not simulated yet (the ledger
    /// script lists those in docs/TRACKER.md).
    /// </summary>
    internal static readonly Dictionary<string, string> Reviewed = new()
    {
        // Their effect comes next turn: outside the turn the simulator predicts (their tab says what a play gives up or sets off).
        ["ArtOfWar"] = "energy next turn when no Attack was played this turn: outside the turn predicted",
        ["PaelsTears"] = "energy next turn for energy left over: outside the turn predicted",
        ["PaleBlueDotPower"] = "draws next turn (DrawCardsNextTurnPower): outside the turn predicted",
        ["SelfFormingClay"] = "block next turn (SelfFormingClayPower): outside the turn predicted",
        // Doom kills at the end of the enemy's own turn, after it attacks: nothing predicted depends on it.
        ["OblivionPower"] = "Doom on each card played: Doom kills only after the enemy has acted",
        ["ReaperFormPower"] = "Doom for the damage dealt: Doom kills only after the enemy has acted",
        // The engine's own.
        ["ExplosivePower"] = "its death damages every player: the engine's (SimEnemy.Explosive)",
        ["ImbalancedPower"] = "the stun when its first hit is fully blocked: the engine's (SimEnemy.FirstHitFullyBlocked)",
        ["RupturePower"] = "Strength after the card that lost you HP (AfterCardPlayed), or at once for HP lost to something else on your turn (end-of-turn damage: unpowered, Strength doesn't touch it): later plays read it from the game",
        // Not simulated yet.
        ["BiiigHug"] = "gap: a Soot shuffled into your draw pile at an unknown spot: later draws in the same simulation don't see it",
        ["PersonalHivePower"] = "gap: a Dazed into your draw pile when you attack it: later draws in the same simulation don't see it",
        ["SurprisePower"] = "gap: the gremlins it calls when it dies: a later hit in the same play can't target them (the next frame reads them)",
        ["UnceasingTop"] = "gap: a draw when your hand empties during your turn: not simulated (your hand size, and what the draw sets off)",
    };

    /// <summary>Each model whose game class changes the fight while the model changes nothing in the simulation, with the game's commands.</summary>
    public static List<(string Model, string GameCommands)> Unexplained()
    {
        var gameTypes = CoverageLedger.ModelTypes().GroupBy(type => type.Name).ToDictionary(group => group.Key, group => group.ToList());
        var result = new List<(string, string)>();
        foreach (var name in SimModels.Names.OrderBy(modelName => modelName, StringComparer.Ordinal))
        {
            if (Reviewed.ContainsKey(name) || !gameTypes.TryGetValue(name, out var types) || SimModels.Get(name) is not { } model) continue;
            var commands = types.SelectMany(HookCalls).Where(GameEffects.Contains).Distinct().OrderBy(command => command, StringComparer.Ordinal).ToList();
            if (commands.Count == 0 || ChangesTheSimulation(model.GetType())) continue;
            result.Add((name, string.Join(", ", commands)));
        }
        return result;
    }

    /// <summary>"Type.Method" for every call in a game class's hooks (After*, Before*), their async state machines and lambdas.</summary>
    private static IEnumerable<string> HookCalls(Type gameType)
    {
        static bool IsHook(string name) => name.StartsWith("After", StringComparison.Ordinal) || name.StartsWith("Before", StringComparison.Ordinal);
        static string Inner(string name) => name.StartsWith('<') ? name[1..] : name;
        var methods = gameType.GetMethods(All).Where(method => IsHook(Inner(method.Name))).Cast<MethodBase>()
            .Concat(Nested(gameType).Where(type => IsHook(Inner(type.Name))).SelectMany(type => type.GetMethods(All)))
            .Concat(Nested(gameType).SelectMany(type => type.GetMethods(All)).Where(method => IsHook(Inner(method.Name))));
        foreach (var method in methods)
            foreach (var call in Calls(method))
                yield return call.DeclaringType!.Name + "." + call.Name;
    }

    /// <summary>The model's own code (its class, the model classes it builds on, their lambdas) changes the simulation somewhere.</summary>
    private static bool ChangesTheSimulation(Type modelType)
    {
        var types = new List<Type>();
        for (var type = modelType; type != null && !Framework.Contains(type) && type != typeof(object); type = type.BaseType) types.Add(type);
        // A model that changes the numbers through the game's math hooks (Vigor's damage, Stretch's block, a cap) acts there.
        if (types.SelectMany(type => type.GetMethods(All)).Any(method => method.GetBaseDefinition().DeclaringType == typeof(SimModel)
                && (method.Name.StartsWith("Modify", StringComparison.Ordinal) || method.Name.StartsWith("Should", StringComparison.Ordinal)))) return true;
        foreach (var method in types.Concat(types.SelectMany(Nested)).SelectMany(type => type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All))))
        {
            List<HarmonyLib.CodeInstruction> code;
            try { code = GameCode.Instructions(method); } catch { continue; }
            foreach (var instruction in code)
            {
                if (instruction.operand is FieldInfo field && (instruction.opcode == OpCodes.Stfld || instruction.opcode == OpCodes.Stsfld)
                    && field.DeclaringType is { } fieldOwner && StateTypes.Contains(fieldOwner)) return true;
                if (instruction.operand is not MethodInfo called || called.DeclaringType is not { } owner) continue;
                if (owner == typeof(SimEngine) && !SimReads.Contains(called.Name)) return true;
                if (typeof(SimModel).IsAssignableFrom(owner) && Helpers.Contains(called.Name)) return true;
                if (StateTypes.Contains(owner) && called.Name.StartsWith("set_", StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    private static IEnumerable<MethodInfo> Calls(MethodBase method)
    {
        List<HarmonyLib.CodeInstruction> code;
        try { code = GameCode.Instructions(method); } catch { yield break; }
        foreach (var instruction in code)
            if (instruction.operand is MethodInfo called && called.DeclaringType != null) yield return called;
    }

    private static IEnumerable<Type> Nested(Type type) => type.GetNestedTypes(All).SelectMany(nested => Nested(nested).Prepend(nested));
}
