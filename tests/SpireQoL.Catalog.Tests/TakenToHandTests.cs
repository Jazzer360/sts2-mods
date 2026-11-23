using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Procs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Cards whose own play moves cards into or out of your hand (CardPileCmd.Add: PlayFacts.TakenToHand, CardEffects.PutsBackFromHand), on the
/// installed game's cards and piles (GameFight). Reports: after Wish your hand had a card more than predicted.
/// </summary>
[Collection(GameFightCollection.Name)]
public class TakenToHandTests
{
    private static PlayFacts FactsOf<T>(GameFight gameFight) where T : CardModel =>
        PlayFacts.For(gameFight.Add<T>(), gameFight.Me, gameFight.Combat, plays: 1);

    [Fact]
    public void WishTakesACardFromYourDrawPile()
    {
        var gameFight = new GameFight();
        Assert.Equal(0, FactsOf<Wish>(gameFight).TakenToHand); // an empty draw pile: nothing to take
        gameFight.Add<StrikeIronclad>(PileType.Draw);
        gameFight.Add<DefendIronclad>(PileType.Draw);
        Assert.Equal(1, FactsOf<Wish>(gameFight).TakenToHand);
    }

    [Fact]
    public void HologramTakesACardFromYourDiscardPile()
    {
        var gameFight = new GameFight();
        Assert.Equal(0, FactsOf<Hologram>(gameFight).TakenToHand);
        gameFight.Add<StrikeIronclad>(PileType.Discard);
        Assert.Equal(1, FactsOf<Hologram>(gameFight).TakenToHand);
    }

    [Fact]
    public void SecretWeaponTakesOnlyAnAttack()
    {
        var gameFight = new GameFight();
        gameFight.Add<DefendIronclad>(PileType.Draw);
        Assert.Equal(0, FactsOf<SecretWeapon>(gameFight).TakenToHand);
        gameFight.Add<StrikeIronclad>(PileType.Draw);
        Assert.Equal(1, FactsOf<SecretWeapon>(gameFight).TakenToHand);
    }

    [Fact]
    public void AllForOneTakesEveryFreeCardInYourDiscardPile()
    {
        // Two 0-cost cards come back; a Strike (1 energy) stays.
        var gameFight = new GameFight();
        gameFight.Add<Anger>(PileType.Discard);
        gameFight.Add<Anger>(PileType.Discard);
        gameFight.Add<StrikeIronclad>(PileType.Discard);
        Assert.Equal(2, FactsOf<AllForOne>(gameFight).TakenToHand);
    }

    [Fact]
    public void ThinkingAheadPutsOneCardBack()
    {
        // It draws, then puts one card from your hand on your draw pile: one card leaves the hand.
        var gameFight = new GameFight();
        gameFight.Add<DefendIronclad>();
        for (var i = 0; i < 5; i++) gameFight.Add<StrikeIronclad>(PileType.Draw);
        var facts = FactsOf<ThinkingAhead>(gameFight);
        Assert.Equal(1, facts.LeavesHand);
    }

    [Fact]
    public void NeowsFuryTakesAsManyAsYouChoose()
    {
        var gameFight = new GameFight();
        gameFight.Add<StrikeIronclad>(PileType.Discard);
        var facts = FactsOf<NeowsFury>(gameFight);
        Assert.Equal(0, facts.TakenToHand);
        Assert.True(facts.HandTakeChosen);
    }
}
