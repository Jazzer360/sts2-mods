using SpireQoL.Procs.Programs;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Cards whose own play moves cards into or out of your hand (CardPileCmd.Add: BoardCounts.TakenToHandCount, CardEffects.PutsBackFromHand), on the
/// installed game's cards and piles (GameFight). Reports: after Wish your hand had a card more than predicted.
/// </summary>
[Collection(GameFightCollection.Name)]
public class TakenToHandTests
{
    /// <summary>The card's takes into your hand counted on the board before its play (BoardCounts), and whether you choose how many.</summary>
    private static (int TakenToHand, bool HandTakeChosen) FactsOf<T>(GameFight gameFight) where T : CardModel
    {
        var card = gameFight.Add<T>();
        return (BoardCounts.TakenToHandCount(card), BoardCounts.HandTakeChosenOf(card, PileType.Hand.GetPile(gameFight.Me).Cards.Count));
    }

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
        // It draws, then puts one card from your hand on your draw pile (its pick's most: one): one card leaves the hand.
        var gameFight = new GameFight();
        gameFight.Add<DefendIronclad>();
        for (var i = 0; i < 5; i++) gameFight.Add<StrikeIronclad>(PileType.Draw);
        var card = gameFight.Add<ThinkingAhead>();
        var start = SpireQoL.Sim.SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        SpireQoL.Sim.SimEngine.ApplyPlayTo(start, SpireQoL.Sim.SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, SpireQoL.Sim.Who.None), out var branches, out _);
        var draws = Sts2Mods.Core.Game.Sts2State.Var(card, "Cards");
        Assert.All(branches, branch => Assert.Equal(1 + draws - 1, branch.You.HandCount));
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
