using SpireQoL.Procs.Programs;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Cards a play discards, transforms or moves into your hand, read from its program (decision #182, CardEffects.Discards, Transforms,
/// TakesIntoHandOf) and counted on the board: a pick after the play's own draws, a test on the picked card, a whole hand, the cards a
/// draw gave, the top of your draw pile looked at, a filter read from the card's own method, and a selector that lets you choose how many.
/// </summary>
[Collection(GameFightCollection.Name)]
public class CardsTakenTests
{
    private static List<CardModel> Others(GameFight gameFight, CardModel card) =>
        PileType.Hand.GetPile(gameFight.Me).Cards.Where(other => !ReferenceEquals(other, card)).ToList();

    /// <summary>The card's takes counted on the board before its play (BoardCounts), as the step translators and the card scan read them.</summary>
    private sealed record Counted(int Discards, bool MayDiscardSly, int TakenToHand, bool HandTakeChosen, int Transforms, bool TransformsChosen);

    private static Counted Facts(GameFight gameFight, CardModel card)
    {
        var others = Others(gameFight, card);
        var drawPile = PileType.Draw.GetPile(gameFight.Me).Cards.Count;
        return new Counted(BoardCounts.DiscardCount(card, others, BoardCounts.CardsDrawn(card, others.Count + 1)), BoardCounts.DiscardsSlyWithoutPick(card),
            BoardCounts.TakenToHandCount(card), BoardCounts.HandTakeChosenOf(card, others.Count + 1), BoardCounts.TransformCount(card, others, drawPile), BoardCounts.TransformsChosen(card));
    }

    private static void DrawPile(GameFight gameFight, int count)
    {
        for (var i = 0; i < count; i++) gameFight.Add<StrikeIronclad>(PileType.Draw);
    }

    // ---- discards

    [Fact]
    public void AcrobaticsDiscardsAfterItsDraws()
    {
        // Acrobatics.OnPlay draws, then you pick a card to discard: with nothing else in hand, a card it drew.
        var gameFight = new GameFight();
        var card = gameFight.Add<Acrobatics>();
        DrawPile(gameFight, count: 5);
        Assert.Equal(1, BoardCounts.DiscardCount(card, Others(gameFight, card), draws: 3));
        Assert.Equal(0, BoardCounts.DiscardCount(card, Others(gameFight, card), draws: 0));
    }

    [Fact]
    public void SurvivorDiscardsOnlyFromTheHandItFinds()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<Survivor>();
        Assert.Equal(0, BoardCounts.DiscardCount(card, Others(gameFight, card), draws: 0));
        gameFight.Add<DefendIronclad>();
        Assert.Equal(1, BoardCounts.DiscardCount(card, Others(gameFight, card), draws: 0));
    }

    [Fact]
    public void TheHermitDiscardsAPickThatIsNotACurse()
    {
        // TheHermit.OnPlay: the card you pick is exhausted if it's a Curse, else discarded. With only a Curse in hand, the pick is
        // exhausted: nothing is discarded (the per-card table counted one).
        var gameFight = new GameFight();
        var card = gameFight.Add<TheHermit>();
        gameFight.Add<Normality>();
        Assert.Equal(0, BoardCounts.DiscardCount(card, Others(gameFight, card), draws: 0));
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(1, BoardCounts.DiscardCount(card, Others(gameFight, card), draws: 0));
    }

    [Fact]
    public void StormOfSteelDiscardsYourWholeHandAndASlyCardThere()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<StormOfSteel>();
        gameFight.Add<StrikeIronclad>();
        gameFight.Add<Reflex>();
        var facts = Facts(gameFight, card);
        Assert.Equal(2, facts.Discards);
        Assert.True(facts.MayDiscardSly);
        // A pick is a card, never a particular one: a Sly card you could pick isn't followed (decision #188).
        var picking = new GameFight();
        var acrobatics = picking.Add<Acrobatics>();
        picking.Add<Reflex>();
        Assert.False(Facts(picking, acrobatics).MayDiscardSly);
    }

    [Fact]
    public void ScrapeDiscardsAtMostWhatItDrew()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<Scrape>();
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(4, BoardCounts.DiscardCount(card, Others(gameFight, card), draws: 4));

    }

    [Fact]
    public void KiChantDiscardsWhatItLooksAtButTheCardItTakes()
    {
        // KiChant.OnPlay: the top Cards of your draw pile (after a shuffle when it's short), one into your hand, the rest discarded.
        var gameFight = new GameFight();
        var card = gameFight.Add<KiChant>();
        DrawPile(gameFight, count: 6);
        var facts = Facts(gameFight, card); // one play's (decision #189)
        Assert.Equal(2, facts.Discards);
        Assert.Equal(1, facts.TakenToHand);
        // An empty draw pile with cards in the discard pile: the shuffle first, then the same.
        var empty = new GameFight();
        var chant = empty.Add<KiChant>();
        for (var i = 0; i < 2; i++) empty.Add<StrikeIronclad>(PileType.Discard);
        Assert.Equal(1, Facts(empty, chant).Discards);
        Assert.Equal(1, Facts(empty, chant).TakenToHand);
    }

    // ---- transforms

    [Fact]
    public void PunchOfJoyTransformsOneSadness()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<PunchOfJoy>();
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(0, BoardCounts.TransformCount(card, Others(gameFight, card), drawPile: 0));
        gameFight.Add<Sadness>();
        gameFight.Add<Sadness>();
        Assert.Equal(1, BoardCounts.TransformCount(card, Others(gameFight, card), drawPile: 0));
    }

    [Fact]
    public void CompactTransformsEveryStatusAndGuardsNoneCertainly()
    {
        var gameFight = new GameFight();
        var compact = gameFight.Add<Compact>();
        gameFight.Add<Dazed>();
        gameFight.Add<Dazed>();
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(2, BoardCounts.TransformCount(compact, Others(gameFight, compact), drawPile: 0));
        // Guards: you pick how many (0 or more): at most your hand, none certainly.
        var guards = gameFight.Add<Guards>();
        var facts = Facts(gameFight, guards);
        Assert.Equal(Others(gameFight, guards).Count, facts.Transforms);
        Assert.True(facts.TransformsChosen);
    }

    [Fact]
    public void ChargeTransformsFromYourDrawPile()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<Charge>();
        DrawPile(gameFight, count: 1);
        Assert.Equal(1, Facts(gameFight, card).Transforms);
        DrawPile(gameFight, count: 5);
        Assert.Equal(Sts2Mods.Core.Game.Sts2State.Var(card, "Cards"), Facts(gameFight, card).Transforms);
    }

    // ---- moved into your hand

    [Fact]
    public void AllForOneTakesTheZeroCostCardsItsFilterReads()
    {
        // AllForOne.Filter (its own method, read from its code): costs 0, not X, and an Attack, Skill or Power.
        var gameFight = new GameFight();
        var card = gameFight.Add<AllForOne>();
        gameFight.Add<StrikeIronclad>(PileType.Discard);
        Assert.Equal(0, Facts(gameFight, card).TakenToHand);
        gameFight.Add<Anger>(PileType.Discard);
        gameFight.Add<Dazed>(PileType.Discard);
        Assert.Equal(1, Facts(gameFight, card).TakenToHand);
        Assert.NotNull(SimAdapter.HandTakeOf(card));
    }

    [Fact]
    public void SummonForthTakesTheBladesNotInYourHand()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<SummonForth>();
        gameFight.Add<SovereignBlade>();
        gameFight.Add<SovereignBlade>(PileType.Discard);
        gameFight.Add<SovereignBlade>(PileType.Draw);
        Assert.Equal(2, Facts(gameFight, card).TakenToHand);
        Assert.Equal(new[] { PileType.Draw, PileType.Discard }, SimAdapter.HandTakeOf(card)!.Piles.Order());
    }

    [Fact]
    public void TheWorldTakesItsCardsOrWhatTheDiscardPileHas()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<TheWorld>();
        gameFight.Add<StrikeIronclad>(PileType.Discard);
        Assert.Equal(1, Facts(gameFight, card).TakenToHand);
        for (var i = 0; i < 9; i++) gameFight.Add<StrikeIronclad>(PileType.Discard);
        Assert.Equal(Sts2Mods.Core.Game.Sts2State.Var(card, "Cards"), Facts(gameFight, card).TakenToHand);
    }

    [Fact]
    public void AutoPlaysFromYourDrawPileAreReadFromTheCommand()
    {
        // CardPileCmd.AutoPlayFromDrawPile's count, its exhaust and its upgrading callback (the table it replaced: Havoc 1 that exhausts,
        // Cascade its X and one more upgraded, The Devil your hand's cards, upgraded when it is).
        var gameFight = new GameFight();
        gameFight.Me.PlayerCombatState!.Energy = 2;
        Assert.Equal((1, true, false), BoardCounts.AutoPlaysOf(gameFight.Add<Havoc>()));
        Assert.Equal((3, false, false), BoardCounts.AutoPlaysOf(gameFight.Add<Cascade>(upgraded: true)));
        var devil = gameFight.Add<TheDevil>(upgraded: true);
        Assert.Equal((Others(gameFight, devil).Count, false, true), BoardCounts.AutoPlaysOf(devil));
        Assert.Null(BoardCounts.AutoPlaysOf(gameFight.Add<StrikeIronclad>()));
    }

    [Fact]
    public void NeowsFuryLetsYouChooseHowMany()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<NeowsFury>();
        Assert.False(Facts(gameFight, card).HandTakeChosen);
        gameFight.Add<StrikeIronclad>(PileType.Discard);
        var facts = Facts(gameFight, card);
        Assert.True(facts.HandTakeChosen);
        Assert.Equal(0, facts.TakenToHand);
        Assert.Null(SimAdapter.HandTakeOf(card));
    }
}
