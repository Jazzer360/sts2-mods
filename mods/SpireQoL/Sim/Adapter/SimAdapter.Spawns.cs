using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// The creatures a creature's death adds to the fight, read from code (decision #195): each of its powers' AfterDeath (Surprise's gremlins,
/// Infested's Wrigglers, Stock's next Axebot), each creature it adds set up as its code sets it up before it joins (decision #276), with the HP its
/// own class rolls (every value it can start with, never the roll) and the powers its AfterAddedToRoom gives it.
/// </summary>
public static partial class SimAdapter
{
    /// <summary>What a spawn can't be read from (a reviewed kind of gap: SpawnTests); such a creature isn't added, and a read SpawnStep finding none is unknown.</summary>
    internal static readonly IReadOnlyDictionary<string, string> SpawnGaps = new Dictionary<string, string>
    {
        ["a loop"] = "creatures added in a loop the reader can't count (a foreach, a bound it can't work out on the board)",
        ["set up"] = "a property set on a monster before it joins that the adapter can't set on a copy of it (no such property, a value it can't work out)",
        ["unread"] = "a condition or a monster the evaluator can't read on the board",
    };

    /// <summary>
    /// The creatures <paramref name="creature"/>'s death adds (SimEnemy.Spawns), in the order its powers' AfterDeath code adds them, each with the power
    /// that adds it (SimSpawn.Source): a CreatureCmd.Add&lt;T&gt;, a CreatureCmd.Add of a creature CombatState.CreateCreature made from
    /// ModelDb.Monster&lt;T&gt;(), or a CreatureCmd.Add of a monster its code makes and sets up first ((T)ModelDb.Monster&lt;T&gt;().ToMutable() and its
    /// properties set: Infested's StartStunned, Stock's StockAmount), each round of a counter loop its own, on a condition that holds when it dies now
    /// (its hook's target the creature, its removal not prevented). Null when it adds none.
    /// </summary>
    internal static List<SimSpawn>? SpawnsOf(Creature creature, ICombatState combat, List<string>? gaps = null)
    {
        List<SimSpawn>? spawns = null;
        var taken = combat.Enemies.Select(enemy => enemy.MaxHp).ToHashSet();
        foreach (var power in creature.Powers)
        {
            var source = PowerId.Of(SimPowerName(power.GetType()));
            // A creature its code adds that the adapter can't read: listed as such, so its death is unknown, never one that adds nothing.
            void Unread(string gap)
            {
                gaps?.Add(gap);
                (spawns ??= new List<SimSpawn>()).Add(new SimSpawn("", Array.Empty<int>()) { Source = source, Unread = SpawnGaps[gap] });
            }
            try
            {
                var program = ModelPrograms.Of(power.GetType(), "AfterDeath");
                if (!program.All().Any(step => step is CommandStep { Command.FullName: "CreatureCmd.Add" })) continue;
                var parameters = new Dictionary<string, object?> { ["target"] = creature, ["creature"] = creature, ["wasRemovalPrevented"] = false };
                var board = ProgramBoard.Of(power);
                static bool Adds(ProgramStep step) => step is CommandStep { Command.FullName: "CreatureCmd.Add" } || step is LoopStep loop && loop.Body.Any(Adds);
                void Walk(IReadOnlyList<ProgramStep> steps, Dictionary<string, List<(string Property, object? Value)>> setUp)
                {
                    foreach (var step in steps)
                    {
                        var holds = step.Condition == null ? true : ProgramEvaluator.Holds(step.Condition, board, program, parameters);
                        if (holds == null && Adds(step)) Unread("unread");
                        if (holds != true) continue;
                        switch (step)
                        {
                            case LoopStep loop:
                                // A counter loop (for i < n): each round makes and adds its own creature.
                                if (CounterRounds(loop, board, program, parameters) is not { } rounds)
                                {
                                    if (Adds(loop)) Unread("a loop");
                                    continue;
                                }
                                for (var round = 0; round < rounds; round++) Walk(loop.Body, new Dictionary<string, List<(string, object?)>>(setUp, StringComparer.Ordinal));
                                break;
                            // A property set on a monster the code makes before it joins: kept for that monster's CreatureCmd.Add.
                            case StoreStep made when made.Field.StartsWith(ProgramReader.MadeMonsterPrefix, StringComparison.Ordinal):
                            {
                                var monsterClass = made.Field[ProgramReader.MadeMonsterPrefix.Length..made.Field.LastIndexOf('.')];
                                var property = made.Field[(made.Field.LastIndexOf('.') + 1)..];
                                setUp[monsterClass] = (setUp.TryGetValue(monsterClass, out var earlier) ? earlier : new List<(string, object?)>())
                                    .Append((property, ProgramEvaluator.Value(made.Value, board, program, parameters))).ToList();
                                break;
                            }
                            case CommandStep { Command.FullName: "CreatureCmd.Add" } add:
                            {
                                var madeClass = add.Values.Count > 0 ? ProgramReader.MadeMonster(add.Values[0]) : null;
                                var name = SpawnedMonster(add) ?? madeClass;
                                if (name == null || MonsterType(name) is not { } type)
                                {
                                    Unread("unread");
                                    continue;
                                }
                                var monster = madeClass != null ? SetUp(type, setUp.TryGetValue(madeClass, out var properties) ? properties : new List<(string, object?)>(), combat) : CanonicalMonster(type);
                                if (monster == null)
                                {
                                    Unread("set up");
                                    continue;
                                }
                                // Made as it's added (Add<T>, Add(monster, ...)), its HP is none the creatures this death added before it joined with; a
                                // creature made earlier (CombatState.CreateCreature, then Add(creature): Surprise's Fat Gremlin) was given its HP then.
                                var madeAsAdded = add.Command.GenericArguments.Count == 1 || madeClass != null;
                                (spawns ??= new List<SimSpawn>()).Add(Spawn(type, monster, combat, taken) with { Source = source, Monster = name, UniqueFromEarlier = madeAsAdded });
                                break;
                            }
                        }
                    }
                }
                Walk(program.Steps, new Dictionary<string, List<(string, object?)>>(StringComparer.Ordinal));
            }
            catch
            {
                Unread("unread");
            }
        }
        return spawns;
    }


    /// <summary>A counter loop's rounds (for i &lt; n, n worked out on the power's board), or null for any other loop or a bound it can't work out.</summary>
    private static int? CounterRounds(LoopStep loop, ProgramBoard board, ModelProgram program, IReadOnlyDictionary<string, object?> parameters)
    {
        if (loop.Items != null || loop.While is not { Terms: [[{ Holds: true, Test: CompareValue { Op: Comparison.Less or Comparison.LessOrEqual } bound }]] }) return null;
        return Number(ProgramEvaluator.Value(bound.Right, board, program, parameters)) is { } most ? Math.Max(0, (int)most + (bound.Op == Comparison.LessOrEqual ? 1 : 0)) : null;
    }

    /// <summary>
    /// The monster a CreatureCmd.Add adds, by its class's name: its type argument, or the ModelDb.Monster&lt;T&gt;() a creature it's given was
    /// made from (CombatState.CreateCreature). Null for a monster given as a model the code makes and sets up first (ProgramReader.MadeMonster).
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

    /// <summary>The canonical monster of a class (ModelDb.Monster&lt;T&gt;()).</summary>
    private static MonsterModel? CanonicalMonster(Type type) => typeof(ModelDb).GetMethod(nameof(ModelDb.Monster))!.MakeGenericMethod(type).Invoke(null, null) as MonsterModel;

    /// <summary>
    /// The monsters set up before they join, per fight (each made once: ModelDb.Monster&lt;T&gt;().ToMutable() with its properties set as the code sets
    /// them), by class and what's set: what their HP, starting powers and first move are read from.
    /// </summary>
    private static readonly ConditionalWeakTable<ICombatState, System.Collections.Concurrent.ConcurrentDictionary<string, MonsterModel?>> SetUpMonsters = new();

    /// <summary>
    /// A copy of the monster of <paramref name="type"/> with each property set as the code sets it before the monster joins (Wriggler.StartStunned,
    /// Axebot.StockAmount), through the property's own setter: the game's own code works out its HP and starting powers from it. Null when a
    /// property can't be set (none of that name, a value of another kind).
    /// </summary>
    private static MonsterModel? SetUp(Type type, IReadOnlyList<(string Property, object? Value)> properties, ICombatState combat)
    {
        var key = type.Name + ":" + string.Join(";", properties.Select(property => property.Property + "=" + property.Value));
        return SetUpMonsters.GetOrCreateValue(combat).GetOrAdd(key, _ =>
        {
            if (CanonicalMonster(type)?.ToMutable() is not { } monster) return null;
            foreach (var (name, value) in properties)
            {
                var property = type.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (property?.SetMethod == null || Number(value) is not { } number) return null;
                object? given = property.PropertyType == typeof(bool) ? number != 0m
                    : property.PropertyType == typeof(int) ? (int)number
                    : property.PropertyType == typeof(decimal) ? number
                    : null;
                if (given == null) return null;
                property.SetValue(monster, given);
            }
            return monster;
        });
    }

    /// <summary>
    /// A monster joining: every HP it can start with, equally likely (Creature.SetUniqueMonsterHpValue: a value in its range no enemy in the
    /// fight has, from all of them when none is free; the creatures the same death adds exclude each other in the engine: SimSpawn.UniqueFromEarlier),
    /// scaled for the players (ScaleMonsterHpForMultiplayer), each value before the scaling kept (SimSpawn.RawHp); and the powers its AfterAddedToRoom
    /// gives itself.
    /// </summary>
    private static SimSpawn Spawn(Type type, MonsterModel monster, ICombatState combat, IReadOnlySet<int> taken)
    {
        var players = combat.Players.Count;
        var act = combat.RunState.CurrentActIndex;
        var range = Enumerable.Range(monster.MinInitialHp, monster.MaxInitialHp - monster.MinInitialHp + 1).ToList();
        var free = range.Where(hp => !taken.Contains(hp)).ToList();
        if (free.Count == 0) free = range;
        var hps = free.Select(hp => players == 1 ? hp : (int)Creature.ScaleHpForMultiplayer(hp, combat.Encounter, players, act)).ToList();
        return new SimSpawn(Combat.TurnEndModel.SafeTitle(monster), hps) { Powers = StartingPowers(type, monster), RawHp = players == 1 ? null : free };
    }

    /// <summary>
    /// The powers a monster's AfterAddedToRoom gives itself (PowerCmd.Apply&lt;T&gt;(base.Creature, amount)), amounts read on the monster as it's set
    /// up, each on its condition read there (Axebot's Stock while its StockAmount is above 0); a power on a condition or of an amount it can't read
    /// isn't given (a gap the spawn scan lists).
    /// </summary>
    internal static PowerSet StartingPowers(Type type, MonsterModel monster, List<string>? gaps = null)
    {
        var powers = PowerSet.Empty;
        var program = ModelPrograms.Of(type, "AfterAddedToRoom");
        var board = ProgramBoard.Of(monster);
        foreach (var apply in program.All().OfType<CommandStep>().Where(command => command.Command.FullName == "PowerCmd.Apply"))
        {
            var holds = apply.Condition == null ? true : ProgramEvaluator.Holds(apply.Condition, board, program);
            if (holds == false) continue;
            if (holds == null || apply.Command.GenericArguments is not [var power] || apply.Values.Count < 3
                || apply.Values[1] is not QueryValue { Name: nameof(MonsterModel.Creature), Of: NamedValue { Name: "this" } }
                || Number(ProgramEvaluator.Value(apply.Values[2], board, program)) is not { } amount || PowerType(power) is not { } powerType)
            {
                gaps?.Add($"{type.Name}: {apply.Name}");
                continue;
            }
            powers = powers.Add(PowerId.Of(SimPowerName(powerType)), delta: (int)amount);
        }
        return powers;
    }
}
