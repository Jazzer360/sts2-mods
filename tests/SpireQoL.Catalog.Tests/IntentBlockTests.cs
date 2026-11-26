using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The block number on a defend intent (decision #98): the intent on screen shows the move's block through the game's block math as things stand;
/// Intent Forecast shows a later move's own amounts (decision #399).
/// </summary>
[Collection(GameFightCollection.Name)]
public class IntentBlockTests
{
    private static Creature Enemy<T>(GameFight fight, string moveId) where T : MonsterModel
    {
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<T>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var monster = creature.Monster!;
        monster.SetUpForCombat();
        var move = (MoveState)monster.MoveStateMachine!.States[moveId];
        typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(monster, move);
        monster.MoveStateMachine!.ForceCurrentState(move);
        return creature;
    }

    [Fact]
    public void AForecastMoveShowsItsOwnBlock()
    {
        // A Seapunk's Bubble Burp (CreatureCmd.GainBlock(base.Creature, BubbleBlock, ValueProp.Move)): on a later turn as on screen.
        var fight = new GameFight();
        var monster = Enemy<Seapunk>(fight, "BUBBLE_BURP_MOVE").Monster!;
        var own = MovePowers.BlockOf(monster, monster.NextMove, now: false);
        Assert.True(own > 0);
        Assert.Equal(own, MovePowers.BlockOf(monster, monster.NextMove));
    }

    [Fact]
    public void AnEnemyWhoseMovesGiveDexterityHasNoForecastBlock()
    {
        // The Forgotten's Miasma blocks 8 through its Dexterity, then steals 2 more of yours: a later Miasma blocks for more, which the forecast doesn't
        // follow. The intent on screen shows it as things stand.
        var fight = new GameFight();
        var monster = Enemy<TheForgotten>(fight, "MIASMA").Monster!;
        Assert.Equal(8, MovePowers.BlockOf(monster, monster.NextMove));
        Assert.Null(MovePowers.BlockOf(monster, monster.NextMove, now: false));
    }
}
