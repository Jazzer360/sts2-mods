using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A replayed card plays the same play again on the board the play before it left (CardModel.OnPlayWrapper: for each play,
/// BeforeCardPlayed, OnPlay, AfterCardPlayed; decision #189). What a play takes from your hand, and what counts what it took, is read
/// by the simulator as the action runs (TakeFromHandAction, SimCount), never counted once before the play and shared out.
/// </summary>
public class SimPerPlayTests
{
    private static readonly SimDrawCard Plain = new("plain");

    private static SimState Hand(int cards, int pile = 0, SimDrawCard? drawn = null)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 200 } } };
        state.You.HandCount = cards;
        if (pile > 0) state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(drawn ?? Plain, pile) } };
        return state;
    }

    /// <summary>Fiend Fire: every card in your hand exhausted, then a hit for each (WithHitCount of the hand it took).</summary>
    private static SimPlay FiendFire(int plays) => new()
    {
        Name = "FiendFire", HandCard = 7, HandDelta = -1, Plays = plays, TargetId = 0, IsAttack = true,
        Body =
        {
            new TakeFromHandAction(HandTakeWay.Each) { Event = CardEvent.Exhausted },
            new AttackAction(Hits: 0, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [0] = 7m }, Fixed = true })
                { HitsFrom = SimCount.CardsTaken },
        },
    };

    [Fact]
    public void AReplayedWholeHandExhaustFindsTheHandTheFirstPlayLeft()
    {
        // Fiend Fire with 3 other cards, played twice: the first play exhausts the 3 and hits 3 times; the second finds an empty hand and
        // exhausts nothing, hits nothing (the shared-out count fired an exhaust and a hit and a half on each).
        var branch = Assert.Single(SimEngine.Run(Hand(cards: 4), FiendFire(plays: 2)).Branches);
        Assert.Equal(200 - 3 * 7, branch.Enemies[0].Hp);
        Assert.Equal(0, branch.You.HandCount);
        Assert.Equal(0, branch.PlayEvents[CardEvent.Exhausted]); // the last play's own events
    }

    [Fact]
    public void AReplayedWholeHandExhaustTakesWhatDarkEmbraceDrew()
    {
        // With Dark Embrace and a draw pile: the first play's 3 exhausts draw 3; the second play exhausts those 3, hits 3 times, and draws
        // 3 more.
        var state = Hand(cards: 4, pile: 10);
        state.You.Powers = PowerSet.Empty.With(PowerId.DarkEmbrace, amount: 1);
        var branch = Assert.Single(SimEngine.Run(state, FiendFire(plays: 2)).Branches);
        Assert.Equal(200 - 6 * 7, branch.Enemies[0].Hp);
        Assert.Equal(6, branch.You.Drawn);
        Assert.Equal(3, branch.You.HandCount);
        Assert.Equal(3, branch.PlayEvents[CardEvent.Exhausted]);
    }

    [Fact]
    public void EachPlayOfAReplayedPickTakesItsCard()
    {
        // Acrobatics replayed: each play draws 3, then discards the card you pick (a card, never a particular one). Shared out, the
        // second play discarded none (1 / 2).
        var play = new SimPlay
        {
            Name = "Acrobatics", HandCard = 7, HandDelta = -1, Plays = 2,
            Body = { new CardEventAction(CardEvent.Drawn, Count: 3) { Indirect = true }, new TakeFromHandAction(HandTakeWay.Pick) { Event = CardEvent.Discarded } },
        };
        var state = Hand(cards: 1, pile: 10);
        state.You.Relics = RelicSet.Empty.With(RelicId.ToughBandages, amount: 3);
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        Assert.Equal(1, branch.PlayEvents[CardEvent.Discarded]);
        Assert.Equal(6 - 2, branch.You.HandCount);
        Assert.Equal(2, branch.Fired["ToughBandages"]);
    }

    [Fact]
    public void APickWithNothingToTakeTakesNothing()
    {
        // Survivor alone in your hand, played twice: no card to discard on either play.
        var play = new SimPlay { Name = "Survivor", HandCard = 7, HandDelta = -1, Plays = 2, Body = { new TakeFromHandAction(HandTakeWay.Pick) { Event = CardEvent.Discarded } } };
        var branch = Assert.Single(SimEngine.Run(Hand(cards: 1), play).Branches);
        Assert.Equal(0, branch.PlayEvents[CardEvent.Discarded]);
        Assert.Equal(0, branch.You.HandCount);
    }

    [Fact]
    public void APickTheCardDecidesSetsOffNeitherWayWhenTheHandHoldsBoth()
    {
        // The Hermit with a Curse and a Strike: one card leaves; exhausted or discarded is your pick (decision #188), so neither is set
        // off. With only the Curse: exhausted.
        TakeFromHandAction Hermit(int curses) => new(HandTakeWay.Pick) { Event = CardEvent.Exhausted, EventIfNot = CardEvent.Discarded, Filter = "TheHermit#0", Matching = curses };
        var mixed = Assert.Single(SimEngine.Run(Hand(cards: 3), new SimPlay { Name = "TheHermit", HandCard = 7, HandDelta = -1, Body = { Hermit(curses: 1) } }).Branches);
        Assert.Equal(1, mixed.You.HandCount);
        Assert.Equal(0, mixed.PlayEvents[CardEvent.Exhausted] + mixed.PlayEvents[CardEvent.Discarded]);
        var curse = Assert.Single(SimEngine.Run(Hand(cards: 2), new SimPlay { Name = "TheHermit", HandCard = 7, HandDelta = -1, Body = { Hermit(curses: 1) } }).Branches);
        Assert.Equal(1, curse.PlayEvents[CardEvent.Exhausted]);
        Assert.Equal(0, curse.PlayEvents[CardEvent.Discarded]);
    }

    [Fact]
    public void AReplayedFilteredTakeFindsTheMatchingCardsItDrew()
    {
        // Second Wind (each non-Attack exhausted, block for each) replayed with Dark Embrace: the first play exhausts 2 Skills and
        // draws 2; the draw pile holds Skills (it would take them: TakenBy), so the second play exhausts those 2 and gains 2 more blocks.
        var skill = new SimDrawCard("skill") { TakenBy = new[] { "SecondWind#0" } };
        var state = Hand(cards: 4, pile: 10, drawn: skill);
        state.You.Powers = PowerSet.Empty.With(PowerId.DarkEmbrace, amount: 1);
        var play = new SimPlay
        {
            Name = "SecondWind", HandCard = 7, HandDelta = -1, Plays = 2,
            Body =
            {
                new TakeFromHandAction(HandTakeWay.Each)
                {
                    Event = CardEvent.Exhausted, Filter = "SecondWind#0", Matching = 2,
                    EachCard = new SimAction[] { new GainBlockAction(Times: 1, Base: 5m, SimProps.Move) { Fixed = true } },
                },
            },
        };
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        Assert.Equal(4 * 5, branch.You.Block);
        Assert.Equal(1 + 2, branch.You.HandCount); // the Attack it left, and the 2 the second play's exhausts drew
    }

    [Fact]
    public void CardsMadeIntoYourHandAreThereForTheNextPlay()
    {
        // Storm of Steel (your hand discarded, a Shiv for each card) replayed: the second play discards the Shivs the first made.
        var play = new SimPlay
        {
            Name = "StormOfSteel", HandCard = 7, HandDelta = -1, Plays = 2,
            Body =
            {
                new TakeFromHandAction(HandTakeWay.Each) { Event = CardEvent.Discarded },
                new CardEventAction(CardEvent.Generated, Count: 0) { CountFrom = SimCount.CardsTaken, JoinsHand = true, Indirect = true },
            },
        };
        var branch = Assert.Single(SimEngine.Run(Hand(cards: 4), play).Branches);
        Assert.Equal(3, branch.PlayEvents[CardEvent.Discarded]);
        Assert.Equal(3, branch.PlayEvents[CardEvent.Generated]);
        Assert.Equal(3, branch.You.HandCount);
    }

    [Fact]
    public void ACountOfYourHandsRoomIsReadAsItRuns()
    {
        // The Moon (Shivs until your hand is full) replayed: the first play fills the hand, the second makes none.
        var play = new SimPlay
        {
            Name = "TheMoon", HandCard = 7, HandDelta = -1, Plays = 2,
            Body = { new CardEventAction(CardEvent.Generated, Count: int.MaxValue) { CountFrom = SimCount.HandRoom, JoinsHand = true, Indirect = true } },
        };
        var branch = Assert.Single(SimEngine.Run(Hand(cards: 4), play).Branches);
        Assert.Equal(0, branch.PlayEvents[CardEvent.Generated]);
        Assert.Equal(SimEngine.MaxHand, branch.You.HandCount); // the card itself left: 3 others and 7 Shivs
    }

    // ---- the room in your hand: one rule (CardPileCmd.Add, CardPileCmd.Draw), applied as each card comes

    [Fact]
    public void ADrawIntoAHandWithOneRoomDrawsOne()
    {
        // CardPileCmd.Draw stops at a full hand: 9 others and the card played (it has left), 3 to draw: 1 drawn.
        var play = new SimPlay { Name = "Draw3", HandCard = 7, HandDelta = -1, Body = { new CardEventAction(CardEvent.Drawn, Count: 3) { Indirect = true, JoinsHand = true } } };
        var branch = Assert.Single(SimEngine.Run(Hand(cards: 10, pile: 5), play).Branches);
        Assert.Equal(1, branch.You.Drawn);
        Assert.Equal(SimEngine.MaxHand, branch.You.HandCount);
    }

    [Fact]
    public void AMadeCardIntoAFullHandIsStillMadeButDoesNotFitThere()
    {
        // CardPileCmd.Add into a hand of 10: the card goes to the discard pile instead (isFullHandAdd); it's still made.
        var play = new SimPlay { Name = "MakeOne", HandCard = 7, HandDelta = -1, Body = { new CardEventAction(CardEvent.Generated, Count: 2) { Indirect = true, JoinsHand = true } } };
        var branch = Assert.Single(SimEngine.Run(Hand(cards: 10), play).Branches);
        Assert.Equal(2, branch.PlayEvents[CardEvent.Generated]);
        Assert.Equal(SimEngine.MaxHand - 1 + 1, branch.You.HandCount); // 9 others, then one fits once the card itself left
    }

    [Fact]
    public void ACardTakenIntoAFullHandGoesToTheDiscardPile()
    {
        // A card moved from your draw pile into a full hand (CardPileCmd.Add) lands in your discard pile.
        var play = new SimPlay
        {
            Name = "Take", HandCard = 0, HandDelta = 0,
            Body = { new TakeToHandAction(Count: 1, new Dictionary<string, int> { ["plain"] = 1 }) { JoinsHand = true } },
        };
        var branch = Assert.Single(SimEngine.Run(Hand(cards: 10, pile: 1), play).Branches);
        Assert.Equal(SimEngine.MaxHand, branch.You.HandCount);
        Assert.Equal(1, branch.You.Draw!.DiscardCount);
        Assert.Equal(0, branch.You.Draw.Count);
    }

    [Fact]
    public void AnExhaustEarlierInThePlayMakesRoomForALaterAdd()
    {
        // A hand of 10: the card played and an exhaust you pick leave it, so both cards it then makes fit.
        var play = new SimPlay
        {
            Name = "ExhaustThenMake", HandCard = 7, HandDelta = -1,
            Body = { new TakeFromHandAction(HandTakeWay.Pick) { Event = CardEvent.Exhausted }, new CardEventAction(CardEvent.Generated, Count: 2) { Indirect = true, JoinsHand = true } },
        };
        var branch = Assert.Single(SimEngine.Run(Hand(cards: 10), play).Branches);
        Assert.Equal(SimEngine.MaxHand, branch.You.HandCount);
    }

    [Fact]
    public void AnExhaustCardReplayedExhaustsItselfOnce()
    {
        // Its own exhaust is where it goes after the last play (CardModel.OnPlayWrapper's result pile): once, however many plays.
        var state = Hand(cards: 1);
        state.You.Powers = PowerSet.Empty.With(PowerId.FeelNoPain, amount: 3);
        var play = new SimPlay
        {
            Name = "Offering", HandCard = 7, HandDelta = -1, Plays = 3,
            After = { new CardEventAction(CardEvent.Exhausted, Count: 1) },
        };
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        Assert.Equal(3, branch.You.Block);
    }
}
