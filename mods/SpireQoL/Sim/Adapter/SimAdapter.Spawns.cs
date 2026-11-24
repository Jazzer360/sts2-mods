using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// The creatures a creature's death adds to the fight, read from code (decision #195): each of its powers' AfterDeath (Surprise's
/// gremlins), each creature it adds with the HP its own class rolls (every value it can start with, never the roll) and the powers its
/// AfterAddedToRoom gives it.
/// </summary>
public static partial class SimAdapter
{
    /// <summary>What a spawn can't be read from (a reviewed kind of gap: SpawnTests); such a creature isn't added, as before.</summary>
    internal static readonly IReadOnlyDictionary<string, string> SpawnGaps = new Dictionary<string, string>
    {
        ["in a loop"] = "creatures added in a loop (Infested's four Wrigglers): each a branch per HP it can start with, too many to search (and set up before they join)",
        ["set up before it joins"] = "a monster made and changed before CreatureCmd.Add(monster, ...) (Stock's next Axebot: its stock and HP; a Wriggler starting stunned)",
        ["unread"] = "a condition or a monster the evaluator can't read on the board",
    };

    /// <summary>
    /// The creatures <paramref name="creature"/>'s death adds (SimEnemy.Spawns), in the order its powers' AfterDeath code adds them: a
    /// CreatureCmd.Add&lt;T&gt;, or a CreatureCmd.Add of a creature CombatState.CreateCreature made from ModelDb.Monster&lt;T&gt;(), on a
    /// condition that holds when it dies now (its hook's target the creature, its removal not prevented). Null when it adds none.
    /// </summary>
    internal static List<SimSpawn>? SpawnsOf(Creature creature, ICombatState combat, List<string>? gaps = null)
    {
        List<SimSpawn>? spawns = null;
        try
        {
            var taken = combat.Enemies.Select(enemy => enemy.MaxHp).ToHashSet();
            foreach (var power in creature.Powers)
            {
                var program = ModelPrograms.Of(power.GetType(), "AfterDeath");
                var parameters = new Dictionary<string, object?> { ["target"] = creature, ["creature"] = creature, ["wasRemovalPrevented"] = false };
                foreach (var (add, inLoop) in Adds(program.Steps, inLoop: false))
                {
                    if (inLoop) { gaps?.Add("in a loop"); continue; }
                    if (add.Condition != null)
                    {
                        var holds = ProgramEvaluator.Holds(add.Condition, ProgramBoard.Of(power), program, parameters);
                        if (holds == null) gaps?.Add("unread");
                        if (holds != true) continue;
                    }
                    if (SpawnedMonster(add) is not { } name) { gaps?.Add("set up before it joins"); continue; }
                    if (MonsterType(name) is not { } type || Spawn(type, combat, taken) is not { } spawn) { gaps?.Add("unread"); continue; }
                    (spawns ??= new List<SimSpawn>()).Add(spawn);
                }
            }
        }
        catch
        {
            return null;
        }
        return spawns;
    }

    private static IEnumerable<(CommandStep Add, bool InLoop)> Adds(IEnumerable<ProgramStep> steps, bool inLoop)
    {
        foreach (var step in steps)
        {
            if (step is LoopStep loop)
                foreach (var found in Adds(loop.Body, inLoop: true)) yield return found;
            else if (step is CommandStep { Command.FullName: "CreatureCmd.Add" } add) yield return (add, inLoop);
        }
    }

    /// <summary>
    /// The monster a CreatureCmd.Add adds, by its class's name: its type argument, or the ModelDb.Monster&lt;T&gt;() a creature it's given was
    /// made from (CombatState.CreateCreature). Null for a monster given as a model (CreatureCmd.Add(monster, ...): set up before it joins).
    /// </summary>
    internal static string? SpawnedMonster(CommandStep add)
    {
        if (add.Command.GenericArguments is [var generic]) return generic;
        if (add.Command.ParameterTypes is not [nameof(Creature), ..] || add.Values.Count == 0) return null;
        return add.Values[0].Parts().OfType<QueryValue>().FirstOrDefault(query => query is { Name: nameof(ModelDb.Monster), Type: nameof(ModelDb), GenericArguments.Count: 1 })?.GenericArguments[0];
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Type?> MonsterTypes = new();

    private static Type? MonsterType(string name) => MonsterTypes.GetOrAdd(name, simple =>
        typeof(MonsterModel).Assembly.GetTypes().FirstOrDefault(type => type.Name == simple && typeof(MonsterModel).IsAssignableFrom(type) && !type.IsAbstract));

    /// <summary>
    /// A monster joining: every HP it can start with, equally likely (Creature.SetUniqueMonsterHpValue: a value in its range no enemy in the
    /// fight has, from all of them when none is free; spawns made before the others join don't exclude each other), scaled for the players
    /// (ScaleMonsterHpForMultiplayer); and the powers its AfterAddedToRoom gives itself.
    /// </summary>
    private static SimSpawn? Spawn(Type type, ICombatState combat, IReadOnlySet<int> taken)
    {
        if (typeof(ModelDb).GetMethod(nameof(ModelDb.Monster))!.MakeGenericMethod(type).Invoke(null, null) is not MonsterModel monster) return null;
        var players = combat.Players.Count;
        var act = combat.RunState.CurrentActIndex;
        var range = Enumerable.Range(monster.MinInitialHp, monster.MaxInitialHp - monster.MinInitialHp + 1).ToList();
        var free = range.Where(hp => !taken.Contains(hp)).ToList();
        if (free.Count == 0) free = range;
        var hps = free.Select(hp => players == 1 ? hp : (int)Creature.ScaleHpForMultiplayer(hp, combat.Encounter, players, act)).ToList();
        return new SimSpawn(Combat.TurnEndModel.SafeTitle(monster), hps) { Powers = StartingPowers(type, monster) };
    }

    /// <summary>
    /// The powers a monster's AfterAddedToRoom gives itself (PowerCmd.Apply&lt;T&gt;(base.Creature, amount)), amounts read on the monster's
    /// class; a power on a condition or of an amount it can't read isn't given (a gap the spawn scan lists).
    /// </summary>
    internal static PowerSet StartingPowers(Type type, MonsterModel monster, List<string>? gaps = null)
    {
        var powers = PowerSet.Empty;
        var program = ModelPrograms.Of(type, "AfterAddedToRoom");
        foreach (var apply in program.All().OfType<CommandStep>().Where(command => command.Command.FullName == "PowerCmd.Apply"))
        {
            if (apply.Condition != null || apply.Command.GenericArguments is not [var power] || apply.Values.Count < 3
                || apply.Values[1] is not QueryValue { Name: nameof(MonsterModel.Creature), Of: NamedValue { Name: "this" } }
                || Number(ProgramEvaluator.Value(apply.Values[2], ProgramBoard.Of(monster), program)) is not { } amount || PowerType(power) is not { } powerType)
            {
                gaps?.Add($"{type.Name}: {apply.Name}");
                continue;
            }
            powers = powers.Add(PowerId.Of(SimPowerName(powerType)), delta: (int)amount);
        }
        return powers;
    }
}
