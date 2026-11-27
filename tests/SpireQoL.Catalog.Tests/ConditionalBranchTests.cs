using System.Reflection;
using System.Reflection.Emit;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A monster's conditional move branches as simulator conditions (decision #195, MoveConditions): every branch of every monster in the game,
/// its condition translated and asked of the adapter's state as that monster, gives what the game's own lambda gives on the same board, at
/// full HP and at 1, alone and with an ally, without and with each power its branches ask about. Every lambda reads on both branches.
/// </summary>
[Collection(GameFightCollection.Name)]
public class ConditionalBranchTests
{
    /// <summary>The monsters whose move rules have a conditional branch.</summary>
    private static IEnumerable<MonsterModel> Conditional()
    {
        GameModels.EnsureLoaded();
        foreach (var canon in ModelDb.All.OfType<MonsterModel>())
        {
            var monster = canon.ToMutable();
            monster.SetUpForCombat();
            if (monster.MoveStateMachine!.States.Values.OfType<ConditionalBranchState>().Any()) yield return canon;
        }
    }

    [Fact]
    public void EveryBranchsConditionIsTheGamesLambdaOnTheSameBoard()
    {
        var wrong = new List<string>();
        var checkedBranches = 0;
        foreach (var canon in Conditional())
        {
            foreach (var board in Boards)
            {
                var fight = new GameFight();
                var creature = fight.Combat.CreateCreature(canon.ToMutable(), CombatSide.Enemy, slot: null);
                fight.Combat.AddCreature(creature);
                var monster = creature.Monster!;
                monster.SetUpForCombat();
                board(fight, creature);
                var (state, ids) = Enemies(fight);
                foreach (var branchState in monster.MoveStateMachine!.States.Values.OfType<ConditionalBranchState>())
                    foreach (var (to, lambda) in MoveConditions.BranchesOf(branchState))
                    {
                        checkedBranches++;
                        var game = lambda == null || (bool)lambda.DynamicInvoke()!;
                        var condition = MoveConditions.Of(monster, lambda);
                        var simulated = condition == null ? null : SimEngine.Holds(state, condition, ids[creature]);
                        if (simulated != game) wrong.Add($"{canon.GetType().Name} {branchState.Id} -> {to}: the game {game}, the simulator {simulated?.ToString() ?? "unread"} ({condition?.ToString() ?? (lambda == null ? "" : ProgramReader.ReadReturned(monster.GetType(), lambda.Method, RuntimeCode.Read).ToString())})");
                    }
            }
        }
        Assert.True(checkedBranches > 100, $"only {checkedBranches} branches checked");
        Assert.True(wrong.Count == 0, "Conditional branches the simulator asks differently from the game's lambda:\n" + string.Join("\n", wrong.Distinct()));
    }

    /// <summary>
    /// The enemies as the adapter reads what a branch asks of them (SimAdapter.Build: their HP, alive or not, and their powers through
    /// SimReaders.Powers, and the numbers of its own its rules ask, MoveConditions.CounterFields: decision #411), without the rest of the board:
    /// Build reads a monster's title, which for some (Test Subject's kill count) reads the player's save, not there outside the game.
    /// </summary>
    private static (SimState State, Dictionary<Creature, int> Ids) Enemies(GameFight fight)
    {
        var ids = new Dictionary<Creature, int>();
        var state = new SimState();
        foreach (var enemy in fight.Combat.Enemies) ids[enemy] = ids.Count;
        foreach (var (enemy, id) in ids)
            state.Enemies.Add(new SimEnemy
            {
                Id = id, Hp = enemy.IsAlive ? enemy.CurrentHp : 0, Powers = SimReaders.Powers(enemy, creature => ids.TryGetValue(creature, out var known) ? known : null),
                Fields = enemy.Monster is { } monster ? MoveConditions.CounterFields(monster, NamedSet<string>.Empty) : NamedSet<string>.Empty,
            });
        return (state, ids);
    }

    /// <summary>The boards each monster's branches are asked on: as made, at 1 HP, with an ally, with every power a monster's branches ask about.</summary>
    private static readonly Action<GameFight, Creature>[] Boards =
    {
        (_, _) => { },
        (_, creature) => creature.SetCurrentHpInternal(1),
        (fight, creature) => fight.Combat.AddCreature(fight.Combat.CreateCreature(creature.Monster!.CanonicalInstance!.ToMutable(), CombatSide.Enemy, slot: null)),
        (_, creature) =>
        {
            foreach (var power in new PowerModel[] { ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.AsleepPower>(), ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.SlumberPower>() })
                power.ToMutable().ApplyInternal(creature, amount: 1m);
        },
    };

    [Fact]
    public void HpBreakpointsPowersAndTheSideAreFollowedNotDecided()
    {
        // What the simulator follows as a fight goes on is translated, not decided when read: Frog Knight's half HP, Lagavulin Matriarch's
        // and Slumbering Beetle's sleep, Fabricator's, Ovicopter's and Living Shield's side.
        var followed = new Dictionary<string, SimCounted>();
        foreach (var canon in Conditional())
        {
            var fight = new GameFight();
            var creature = fight.Combat.CreateCreature(canon.ToMutable(), CombatSide.Enemy, slot: null);
            fight.Combat.AddCreature(creature);
            creature.Monster!.SetUpForCombat();
            foreach (var branchState in creature.Monster.MoveStateMachine!.States.Values.OfType<ConditionalBranchState>())
                foreach (var (_, lambda) in MoveConditions.BranchesOf(branchState))
                    if (MoveConditions.Of(creature.Monster, lambda) is { } condition)
                        foreach (var counted in SimEngine.ConditionParts(condition).OfType<CountCondition>()) followed[canon.GetType().Name] = counted.Counted;
        }
        Assert.Equal(SimCounted.MonsterHp, followed["FrogKnight"]);
        Assert.Equal(SimCounted.MonsterPower, followed["LagavulinMatriarch"]);
        Assert.Equal(SimCounted.MonsterPower, followed["SlumberingBeetle"]);
        Assert.Equal(SimCounted.MonsterSideAlive, followed["Fabricator"]);
        Assert.Equal(SimCounted.MonsterSideAlive, followed["Ovicopter"]);
        Assert.Equal(SimCounted.MonsterSideAlive, followed["LivingShield"]);
    }

    [Fact]
    public void TheNextMoveComesFromTheSimulatedState()
    {
        // Frog Knight's rules read with their conditions (IntentReader.Graph with the monster), asked of a simulated state (IntentForecast.NextOn):
        // at full HP its conditional branch goes one way, with the simulator's HP below half the other, as its lambdas would.
        var fight = new GameFight();
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.FrogKnight>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var monster = creature.Monster!;
        monster.SetUpForCombat();
        var machine = monster.MoveStateMachine!;
        var graph = IntentReader.Graph(machine, monster)!;
        var branch = machine.States.Values.OfType<ConditionalBranchState>().Single();
        var before = graph.Nodes.Values.OfType<MoveNodeMove>().First(move => move.FollowUp == branch.Id);
        var (state, ids) = Enemies(fight);
        var full = IntentForecast.NextOn(graph, before.Id, new[] { before.Id }, state, ids[creature])!;
        state.Enemies[0].Hp = 1;
        var low = IntentForecast.NextOn(graph, before.Id, new[] { before.Id }, state, ids[creature])!;
        Assert.NotEqual(Assert.Single(full).Move, Assert.Single(low).Move);
        creature.SetCurrentHpInternal(1);
        var lambdas = MoveConditions.BranchesOf(branch);
        Assert.Equal(lambdas.First(entry => entry.Lambda == null || entry.Lambda.DynamicInvoke() is true).To, Assert.Single(low).Move);
    }

    public static TheoryData<string> Branches => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryBranchsLambdaReads(string branch)
    {
        // Each ConditionalBranchState.AddState's lambda (the ldftn before it in GenerateMoveStateMachine) reads with nothing unknown.
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return;
        var code = BranchCode.Of(path);
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var read = 0;
        var unread = new List<string>();
        foreach (var monster in MonsterCode.Monsters(code.Assembly.GetTypes()))
        {
            if (monster.GetMethod("GenerateMoveStateMachine", declared) is not { } generate) continue;
            var ops = code.Read(generate);
            for (var i = 0; i < ops.Count; i++)
            {
                if (ops[i].Member is not { Name: "AddState", DeclaringType: nameof(ConditionalBranchState) }) continue;
                var lambda = ops.Take(i).Last(instruction => instruction.OpCode == OpCodes.Ldftn).Member!;
                var owner = new[] { monster }.Concat(monster.GetNestedTypes(declared)).First(type => type.Name == lambda.DeclaringType);
                var condition = ProgramReader.ReadReturned(monster, owner.GetMethods(declared).First(method => method.Name == lambda.Name), code.Read);
                read++;
                if (condition.Literals.Any(literal => literal.Test.Parts().Any(part => part is UnknownValue)))
                    unread.Add($"{monster.Name}: {condition}");
            }
        }
        Assert.True(read > 40, $"only {read} conditional branches found ({branch})");
        Assert.True(unread.Count == 0, $"Conditional branches whose lambda doesn't read ({branch}):\n" + string.Join("\n", unread));
    }
}
