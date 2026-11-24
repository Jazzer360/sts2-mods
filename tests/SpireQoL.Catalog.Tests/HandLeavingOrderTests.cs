using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A play's cards leave your hand before what their leaving sets off (a dev review: Fiend Fire with Dark Embrace drew 7 where 2 were
/// predicted). The game exhausts or discards them one at a time, each draw it sets off following a card out, so the hand has room for
/// the draws; and a play that takes the whole hand takes what was in it then, not the cards those draws brought in.
/// </summary>
[Collection(GameFightCollection.Name)]
public class HandLeavingOrderTests
{
    [Fact]
    public void FiendFiresHandLeavesBeforeItsExhaustsSetAnythingOff()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<FiendFire>();
        for (var i = 0; i < 9; i++) gameFight.Add<StrikeIronclad>();
        var play = SimAdapter.Play(card, gameFight.Me, gameFight.Combat, new Dictionary<Creature, int>(), targetId: 0, plays: 1);
        // One action takes the hand (decision #189: TakeFromHandAction), the cards leaving before their exhausts set anything off.
        var exhausts = play.Body.FindIndex(simAction => simAction is TakeFromHandAction { Way: HandTakeWay.Each, Filter: null, Event: CardEvent.Exhausted });
        Assert.True(exhausts >= 0, string.Join(", ", play.Body.Select(simAction => simAction.GetType().Name)));
        // Its code exhausts the hand, then attacks (FiendFire.OnPlay): what the exhausts set off (Charon's Ashes' hits) lands before its
        // kill (a dev review: the lethal play showed no Charon's Ashes tab).
        Assert.True(Procs.Programs.CardPrograms.Before(Procs.Programs.CardPrograms.Of(typeof(FiendFire)), "Exhaust", "Attack"));
        var attack = play.Body.FindIndex(simAction => simAction is AttackAction);
        Assert.True(exhausts < attack, "its exhausts come after its attack");
        Assert.Equal(SimCount.CardsTaken, ((AttackAction)play.Body[attack]).HitsFrom);
    }
}
