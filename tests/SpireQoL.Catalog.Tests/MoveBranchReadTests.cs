using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The powers a move with branches gives, read from its code (decision #377: MoveBranches.FromCode, each PowerCmd.Apply on its condition asked of the
/// monster now), against what the per-move rules picked by hand, on real monsters in each state that decides it. A move whose condition reads a field
/// of its own the move stores first (Test Subject's Respawns) isn't read: its rule stands.
/// </summary>
[Collection(GameFightCollection.Name)]
public class MoveBranchReadTests
{
    private static Creature Monster<T>(GameFight fight) where T : MonsterModel
    {
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<T>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        creature.Monster!.SetUpForCombat();
        return creature;
    }

    private static (MethodInfo Perform, List<MovePowers.Applied> All) Move(Creature creature, string moveId)
    {
        var move = (MoveState)creature.Monster!.MoveStateMachine!.States[moveId];
        var perform = SimAdapter.PerformOf(move)!;
        var all = MovePowers.CallsOf(perform).Select(call => new MovePowers.Applied(call.Power, call.Constant, Maybe: !call.Certain)).ToList();
        return (perform, all);
    }

    private static string Text(IEnumerable<MovePowers.Applied>? applied) =>
        applied == null ? "none read" : string.Join(", ", applied.Select(each => $"{each.Power.Name} {each.Amount}"));

    private static string Read(Creature creature, string moveId)
    {
        var (perform, all) = Move(creature, moveId);
        return Text(MoveBranches.FromCode(creature.Monster!, perform, all));
    }

    [Fact]
    public void EntomancersSpitIsReadFromItsCodeAsItsRuleHadIt()
    {
        // Its Personal Hive below 3 (it joins with 1): +1 Personal Hive and +1 Strength; at 3, or none: +2 Strength.
        var fight = new GameFight();
        var entomancer = Monster<Entomancer>(fight);
        Assert.Equal("StrengthPower 2", Read(entomancer, "PHEROMONE_SPIT_MOVE"));
        var hive = ModelDb.Power<PersonalHivePower>().ToMutable();
        hive.ApplyInternal(entomancer, amount: 1m);
        Assert.Equal("PersonalHivePower 1, StrengthPower 1", Read(entomancer, "PHEROMONE_SPIT_MOVE"));
        hive.SetAmount(3);
        Assert.Equal("StrengthPower 2", Read(entomancer, "PHEROMONE_SPIT_MOVE"));
    }

    [Fact]
    public void CeremonialBeastsPlowIsReadFromItsCodeAsItsRuleHadIt()
    {
        // Strength while it still has Plow (beta: the condition is in its code; main's always gives it, a power on every path).
        var fight = new GameFight();
        var beast = Monster<CeremonialBeast>(fight);
        Assert.Equal("", Read(beast, "PLOW_MOVE"));
        ModelDb.Power<PlowPower>().ToMutable().ApplyInternal(beast, amount: 150m);
        Assert.Matches(@"^StrengthPower [1-9]\d*$", Read(beast, "PLOW_MOVE"));
    }

    [Fact]
    public void TestSubjectsRespawnIsLeftToItsRule()
    {
        // RespawnMove adds one to Respawns before its switch on it: asked of the monster now, the test would read the count before: not read.
        var fight = new GameFight();
        var subject = Monster<TestSubject>(fight);
        var (perform, all) = Move(subject, "RESPAWN_MOVE");
        Assert.Null(MoveBranches.FromCode(subject.Monster!, perform, all));
    }
}
