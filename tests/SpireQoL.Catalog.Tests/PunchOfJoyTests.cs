using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #413, from the owner's Fool run: Punch of Joy transforms the Sadness in your hand into Joy (up to its Cards) once its attack is over, when
/// there's any (PileType.Hand's cards OfType&lt;Sadness&gt;(), Count 0: none). That count of one class of card in your hand is followed as any filter of
/// your hand is (SimCounted.HandMatching): the play was unknown where its attack ran before the test.
/// </summary>
[Collection(GameFightCollection.Name)]
public class PunchOfJoyTests
{
    private static (GameFight Fight, CardModel Punch, Creature Enemy) Board(int sadness)
    {
        var fight = new GameFight();
        var enemy = fight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(enemy);
        var punch = fight.Add<PunchOfJoy>();
        for (var i = 0; i < sadness; i++) fight.Add<Sadness>();
        fight.Add<StrikeIronclad>();
        return (fight, punch, enemy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void ItsTransformOfTheSadnessInYourHandIsFollowed(int sadness)
    {
        var (fight, punch, enemy) = Board(sadness);
        var start = SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var play = SimAdapter.Play(punch, fight.Me, fight.Combat, ids, ids[enemy]);
        Assert.DoesNotContain(play.Body, action => action is NotModelledAction);
        SimEngine.ApplyPlayTo(start, play, out var branches, out _);
        Assert.All(branches, branch => Assert.Null(branch.Unmodelled));
        // The Sadness become Joy in place: your hand is the Sadness (as Joy) and the Strike.
        Assert.All(branches, branch => Assert.Equal(sadness + 1, branch.You.HandCount));
    }
}
