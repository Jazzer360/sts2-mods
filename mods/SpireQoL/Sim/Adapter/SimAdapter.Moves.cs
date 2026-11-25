using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Procs.Programs;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

/// <summary>
/// A monster's code as the simulator's (decision #195): each command a monster's move or hook calls, read into its program
/// (ModelPrograms, MonsterCode), is translated into the enemy turn's action for it or is a named gap with why. The scan holds every
/// command of every monster's moves and hooks on both branches to one or the other (MonsterProgramTests).
/// </summary>
public static partial class SimAdapter
{
    /// <summary>The commands a monster's move is translated from, each to the enemy turn's action standing for it.</summary>
    internal static readonly IReadOnlyDictionary<string, string> MoveCommandsTranslated = new Dictionary<string, string>
    {
        ["DamageCmd.Attack"] = "MoveAttackAction: the intent's hits of the attack at its place in the move (the game's numbers, through the damage pipeline as each lands)",
        ["PowerCmd.Apply"] = "MovePowerAction: the power (its type argument) on each creature its target argument names, its amount its third argument, read on the monster",
        ["CreatureCmd.GainBlock"] = "MoveBlockAction: block on each creature its first argument names, its amount through the game's block math",
        ["CreatureCmd.Stun"] = "MoveStunAction: the monster stunning itself (Bowlbug Rock's Headbutt when off balance), on its condition asked of its own state: its next turn's move",
    };

    /// <summary>The commands a monster's moves and hooks call that the enemy turn doesn't follow, and why.</summary>
    internal static readonly IReadOnlyDictionary<string, string> MoveCommandsNotTranslated = new Dictionary<string, string>
    {
        ["PowerCmd.Remove"] = "a power a move takes away (its own: Soar, Steam Eruption, Plating, its revive's): after its attack, nothing later this turn reads it",
        ["CardPileCmd.AddToCombatAndPreview"] = "status cards put into your piles (Dazed, Slimed, Burn...): your next turn's, not this enemy turn's",
        ["CardPileCmd.AddGeneratedCardToCombat"] = "cards put into your piles (Dazed, Beckon, Frantic Escape): your next turn's, not this enemy turn's",
        ["CardPileCmd.RemoveFromCombat"] = "cards a move steals from your piles (Thieving Hopper): your next turn's, not this enemy turn's",
        ["CreatureCmd.Add"] = "a creature a move summons: it joins with a move for its next turn (gap: the summon isn't in the enemy turn's state)",
        ["CreatureCmd.Heal"] = "HP a move heals: not followed in the enemy turn (gap)",
        ["CreatureCmd.SetMaxHp"] = "a move's new max HP (Test Subject's respawn): not followed in the enemy turn (gap)",
        ["CreatureCmd.SetMaxAndCurrentHp"] = "HP set as a monster joins or hatches (Decimillipede, Tough Egg): not followed (gap)",
        ["CreatureCmd.Kill"] = "a monster killing itself after its attack (Gas Bomb, Waterfall Giant): its death isn't followed in the enemy turn (gap)",
        ["CreatureCmd.Escape"] = "a monster fleeing (Fat Gremlin, Thieving Hopper): it leaves after its move (gap)",
        ["ThieveryPower.Steal"] = "gold stolen (Gremlin Merc): not a fight's number",
        ["SwipePower.Steal"] = "a card stolen (Thieving Hopper): your next turn's, not this enemy turn's",
        ["ReattachPower.DoReattach"] = "a Decimillipede segment coming back: its next turn's (gap)",
        ["MonsterModel.SetMoveImmediate"] = "a monster's move changed by a hook (Queen's Enrage when her Amalgam dies): not followed (gap)",
    };

    /// <summary>
    /// What a translated move step can't be built from, besides its command (a reviewed kind of gap, MonsterProgramTests): the step keeps the
    /// enemy turn as it was (no action), never a guess.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> MoveStepGaps = new Dictionary<string, string>
    {
        ["on a condition"] = "a step the move runs only on a condition the simulator doesn't ask of it yet (Plow's Strength while it has Plow, Pheromone Spit's by its Personal Hive, Dampen on a target without it; a stun of itself on its own state is asked: decision #271); none of them comes before a later hit this turn",
        ["a power made first"] = "PowerCmd.Apply of a power the move makes and sets up first (Sandpit, Dampen, Swipe, Thievery): its own amounts and targets aren't read",
        ["unread value"] = "a target or an amount the evaluator can't work out on the board",
    };

    /// <summary>
    /// The move an enemy is about to make (its NextMove), as the enemy turn's actions in its code's order (decision #195): its attacks as its
    /// intent's hits (one attack step for each attack intent, in order), the powers it applies and the block it gives. Null when it isn't read
    /// whole (a hidden intent: decision #72 keeps it hidden; an attack the intent doesn't show at that place; code the reader couldn't
    /// follow): the enemy is its intent's hits alone, as before. A step that can't be built (a gap: MoveStepGaps) is left out.
    /// </summary>
    /// <param name="hitsPerAttack">The hits of each of its intent's attacks, in order (AttackIntent.Repeats).</param>
    /// <param name="idOf">A creature's simulator id (an enemy's, Who.You, Who.Teammate(k)), or null for one the state doesn't hold.</param>
    internal static IReadOnlyList<SimAction>? MoveOf(MonsterModel monster, IReadOnlyList<int> hitsPerAttack, Func<Creature, int?> idOf, ICombatState combat, List<string>? gaps = null)
    {
        try
        {
            var move = monster.NextMove;
            if (move == null || move.Intents.Any(intent => intent is HiddenIntent or UnknownIntent)) return null;
            if (Sts2State.Private<Delegate?>(move, "_onPerform", fallback: null)?.Method is not { DeclaringType: { } owner } perform) return null;
            var program = ModelPrograms.Of(owner, perform.Name);
            if (program.Unknowns.Any() || AttackPlacementGap(program, hitsPerAttack.Count) != null) return null;
            var players = combat.Players.Select(player => player.Creature).Where(creature => creature.IsAlive).ToList();
            var context = new MoveContext(program, ProgramBoard.Of(monster), idOf, combat, players, gaps ?? new List<string>());
            var actions = new List<SimAction>();
            var attacks = 0;
            var firstHit = 0;
            foreach (var step in program.Steps)
            {
                if (step is CommandStep { Command.FullName: "DamageCmd.Attack" })
                {
                    actions.Add(new MoveAttackAction(firstHit, hitsPerAttack[attacks]));
                    firstHit += hitsPerAttack[attacks++];
                }
                else actions.AddRange(MoveStep(step, context, each: null));
            }
            return actions;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The steps of a move that change the fight's creatures (a power, block) but can't be built whatever the board (MoveStepGaps: on a
    /// condition, a power made first), each with its gap: what the scan holds to a reviewed list (MonsterProgramTests).
    /// </summary>
    internal static IEnumerable<(CommandStep Step, string Gap)> MoveGaps(ModelProgram program)
    {
        IEnumerable<(CommandStep, string)> Of(IEnumerable<ProgramStep> steps, bool conditional)
        {
            foreach (var step in steps)
            {
                if (step is LoopStep loop)
                {
                    foreach (var found in Of(loop.Body, conditional || loop.Condition != null)) yield return found;
                    continue;
                }
                if (step is not CommandStep { Command.FullName: "PowerCmd.Apply" or "CreatureCmd.GainBlock" } command) continue;
                if (conditional || command.Condition != null) yield return (command, "on a condition");
                else if (command.Command.FullName == "PowerCmd.Apply" && command.Command.GenericArguments.Count == 0) yield return (command, "a power made first");
            }
        }
        return Of(program.Steps, conditional: false);
    }

    /// <summary>
    /// Why a move's attacks can't be placed against its intent (one unconditional attack step for each attack intent, none in a loop), or null
    /// when they can: such a move stays its intent's hits alone.
    /// </summary>
    internal static string? AttackPlacementGap(ModelProgram program, int attackIntents)
    {
        var attacks = program.Steps.OfType<CommandStep>().Where(command => command.Command.FullName == "DamageCmd.Attack").ToList();
        if (program.All().OfType<CommandStep>().Count(command => command.Command.FullName == "DamageCmd.Attack") != attacks.Count) return "an attack in a loop";
        if (attacks.Any(attack => attack.Condition != null)) return "an attack on a condition";
        return attacks.Count != attackIntents ? $"{attacks.Count} attacks, {attackIntents} attack intents" : null;
    }

    /// <summary>What a move's steps are worked out against: the monster (its "this"), its program, and the fight's creatures.</summary>
    private sealed record MoveContext(ModelProgram Program, ProgramBoard Board, Func<Creature, int?> IdOf, ICombatState Combat,
        IReadOnlyList<Creature> Players, List<string> Gaps);

    /// <summary>One step's actions (a loop's body once for each creature it goes over); a step that can't be built adds its gap and none.</summary>
    private static IEnumerable<SimAction> MoveStep(ProgramStep step, MoveContext context, Creature? each)
    {
        // CreatureCmd.Stun of its own creature (Bowlbug Rock's Headbutt when off balance), on its condition as the simulator asks it of the monster's own
        // state (MoveConditions.OfStep: decision #271); a condition it can't ask is a gap.
        if (step is CommandStep { Command.FullName: "CreatureCmd.Stun" } stun && stun.Values.Count > 0 && each == null
            && context.Board.Self is MonsterModel monster && Value(stun.Values[0], context, each) is Creature stunned && ReferenceEquals(stunned, monster.Creature))
        {
            if (step.Condition == null) return new SimAction[] { new MoveStunAction() };
            if (MoveConditions.OfStep(monster, step.Condition) is { } when) return new SimAction[] { new WhenAction(when, new SimAction[] { new MoveStunAction() }) };
            context.Gaps.Add("on a condition");
            return Array.Empty<SimAction>();
        }
        if (step.Condition != null)
        {
            if (step is CommandStep { Command.FullName: "PowerCmd.Apply" or "CreatureCmd.GainBlock" } or LoopStep) context.Gaps.Add("on a condition");
            return Array.Empty<SimAction>();
        }
        if (step is LoopStep loop)
        {
            if (loop.Items is not { } items || Value(items, context, each) is not IEnumerable<object?> over)
            {
                if (loop.Body.Any(inner => inner is CommandStep { Command.FullName: "PowerCmd.Apply" or "CreatureCmd.GainBlock" })) context.Gaps.Add("unread value");
                return Array.Empty<SimAction>();
            }
            var actions = new List<SimAction>();
            foreach (var item in over.ToList())
                foreach (var inner in loop.Body) actions.AddRange(MoveStep(inner, context, item as Creature));
            return actions;
        }
        if (step is not CommandStep command) return Array.Empty<SimAction>();
        return command.Command.FullName switch
        {
            "PowerCmd.Apply" => MovePower(command, context, each),
            "CreatureCmd.GainBlock" => MoveBlock(command, context, each),
            _ => Array.Empty<SimAction>(),
        };
    }

    /// <summary>PowerCmd.Apply&lt;T&gt;(choiceContext, target or targets, amount, applier, card): the power on each creature named.</summary>
    private static IEnumerable<SimAction> MovePower(CommandStep command, MoveContext context, Creature? each)
    {
        if (command.Command.GenericArguments is not [var powerName] || command.Values.Count < 3)
        {
            context.Gaps.Add("a power made first");
            return Array.Empty<SimAction>();
        }
        var targets = Creatures(Value(command.Values[1], context, each));
        var powerType = PowerType(powerName);
        if (targets == null || Number(Value(command.Values[2], context, each)) is not { } amount || powerType == null)
        {
            context.Gaps.Add("unread value");
            return Array.Empty<SimAction>();
        }
        // PowerCmd.Apply's scaling of a new one on an enemy for the players is the engine's (SimEngine.ApplyPower, from the power's code: decision #234).
        var ids = targets.Select(context.IdOf).OfType<int>().ToList();
        return ids.Count == 0 ? Array.Empty<SimAction>()
            : new[] { new MovePowerAction(PowerId.Of(SimPowerName(powerType)), (int)amount, ids, SimReaders.IsDebuffType(powerType)) };
    }

    /// <summary>CreatureCmd.GainBlock(creature, amount, props, card): block on each enemy named, through the game's block math now.</summary>
    private static IEnumerable<SimAction> MoveBlock(CommandStep command, MoveContext context, Creature? each)
    {
        if (command.Values.Count < 3 || Creatures(Value(command.Values[0], context, each)) is not { } gainers || Number(Value(command.Values[1], context, each)) is not { } amount
            || Number(Value(command.Values[2], context, each)) is not { } props)
        {
            context.Gaps.Add("unread value");
            return Array.Empty<SimAction>();
        }
        var actions = new List<SimAction>();
        foreach (var gainer in gainers)
        {
            if (gainer.IsPlayer || context.IdOf(gainer) is not { } id) continue;
            var block = (int)Hook.ModifyBlock(context.Combat, gainer, amount, (MegaCrit.Sts2.Core.ValueProps.ValueProp)(int)props, null, null, out _);
            if (block > 0) actions.Add(new MoveBlockAction(block, new[] { id }));
        }
        return actions;
    }

    /// <summary>A value of the move worked out on the board: "this" the monster, its targets the living players, a loop's item "each".</summary>
    private static object? Value(ProgramValue value, MoveContext context, Creature? each) =>
        ProgramEvaluator.Value(value, context.Board, context.Program, new Dictionary<string, object?> { ["targets"] = context.Players, ["each"] = each });

    /// <summary>A number the evaluator gave (a property's int, a constant's decimal), or null.</summary>
    private static decimal? Number(object? value) => value switch
    {
        decimal number => number,
        int number => number,
        long number => number,
        _ => null,
    };

    /// <summary>A creature or creatures (a list the move names), or null when it's neither.</summary>
    private static List<Creature>? Creatures(object? value) => value switch
    {
        Creature creature => new List<Creature> { creature },
        IEnumerable<Creature> creatures => creatures.ToList(),
        System.Collections.IEnumerable items => items.Cast<object?>().All(item => item is Creature) ? items.Cast<Creature>().ToList() : null,
        _ => null,
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Type?> PowerTypes = new();

    /// <summary>The game's power class by its name (a step's type argument).</summary>
    private static Type? PowerType(string name) => PowerTypes.GetOrAdd(name, simple =>
        typeof(PowerModel).Assembly.GetTypes().FirstOrDefault(type => type.Name == simple && typeof(PowerModel).IsAssignableFrom(type)));

    private static PowerModel? Canonical(Type powerType) =>
        typeof(ModelDb).GetMethod(nameof(ModelDb.Power))!.MakeGenericMethod(powerType).Invoke(null, null) as PowerModel;

    /// <summary>The move whose code a MoveState performs (its _onPerform), for the forecast and the scan's checks.</summary>
    internal static System.Reflection.MethodInfo? PerformOf(MoveState move) =>
        Sts2State.Private<Delegate?>(move, "_onPerform", fallback: null)?.Method;
}
