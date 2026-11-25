using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A monster's own state a power's code sets and its move's code reads (decision #271), on the game's own objects: Bowlbug Rock's Headbutt is its
/// attack, then its stun of itself on its IsOffBalance (`if (IsOffBalance) await Stun()`), which Imbalanced's AfterDamageGiven sets on a hit of its
/// fully blocked; its state starts as the game's monster has it.
/// </summary>
public class MonsterStateTests
{
    [Fact]
    public void BowlbugRocksHeadbuttStunsItselfOnItsOwnState()
    {
        var fight = new GameFight();
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<BowlbugRock>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var monster = (BowlbugRock)creature.Monster!;
        monster.SetUpForCombat();
        // Its first move (the machine's initial state).
        var headbutt = (MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState)monster.MoveStateMachine!.States["HEADBUTT_MOVE"];
        typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(monster, headbutt);
        Assert.Equal("HEADBUTT_MOVE", monster.NextMove!.Id);
        var gaps = new List<string>();
        var move = SimAdapter.MoveOf(monster, hitsPerAttack: new[] { 1 }, idOf: candidate => candidate == creature ? 0 : null, fight.Combat, gaps);
        Assert.NotNull(move);
        Assert.Empty(gaps);
        Assert.IsType<MoveAttackAction>(move![0]);
        var when = Assert.IsType<WhenAction>(Assert.Single(move.Skip(1)));
        var offBalance = Assert.IsType<CountCondition>(when.Condition);
        Assert.Equal(SimCounted.MonsterState, offBalance.Counted);
        Assert.Equal("Monster.BowlbugRock.IsOffBalance", offBalance.Field);
        Assert.IsType<MoveStunAction>(Assert.Single(when.Then));
        // The state as the game's monster has it.
        Assert.Equal(0, MoveConditions.StateFields(monster, move, NamedSet<string>.Empty)["Monster.BowlbugRock.IsOffBalance"]);
        monster.IsOffBalance = true;
        Assert.Equal(1, MoveConditions.StateFields(monster, move, NamedSet<string>.Empty)["Monster.BowlbugRock.IsOffBalance"]);
    }
}
