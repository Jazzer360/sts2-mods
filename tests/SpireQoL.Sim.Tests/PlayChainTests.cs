using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Draws a play sets off, the hand limit and shuffles (PlayChain; decompile v0.111.0: the sources'
/// hooks, CardPileCmd.DrawInternal stopping at a full hand, ShuffleIfNecessary before each draw).
/// </summary>
public class PlayChainTests
{
    private static PlayChain.DrawInputs BurningPact(int drawPile, int discard = 5, int hand = 5)
        => new() { OwnDraws = 2, Exhausts = 1, ExhaustedFromHand = 1, HandCount = hand, DrawPile = drawPile, DiscardPile = discard };

    [Fact]
    public void BurningPactWithDarkEmbraceDrawsThreeAndShufflesAtTwo()
    {
        // Exhaust 1 (Dark Embrace draws 1), then draw 2: three draws, so a draw pile of 2 runs out.
        var i = BurningPact(drawPile: 2);
        i.DarkEmbrace = 1;
        var r = PlayChain.Resolve(i);
        Assert.Equal(3, r.Draws);
        Assert.Equal(1, r.Chained);
        Assert.True(r.Shuffles);

        i.DarkEmbrace = 0;
        Assert.False(PlayChain.Resolve(i).Shuffles); // its own 2 draws fit
    }

    [Fact]
    public void NoShuffleWithAnEmptyDiscardPile()
    {
        var i = BurningPact(drawPile: 0, discard: 0);
        i.DarkEmbrace = 2;
        Assert.False(PlayChain.Resolve(i).Shuffles);
    }

    [Fact]
    public void DrawsStopAtAFullHand()
    {
        // 10 cards in hand: the card leaves (9), Burning Pact exhausts one (8): room for 2 of its 3 draws.
        var i = BurningPact(drawPile: 2, hand: 10);
        i.DarkEmbrace = 1;
        var r = PlayChain.Resolve(i);
        Assert.Equal(2, r.Draws);
        Assert.False(r.Shuffles);
    }

    [Fact]
    public void NoDrawOrFiddleBlocksEverything()
    {
        var i = BurningPact(drawPile: 0);
        i.DarkEmbrace = 3;
        i.Blocked = true;
        var r = PlayChain.Resolve(i);
        Assert.Equal(0, r.Draws);
        Assert.False(r.Shuffles);
    }

    [Fact]
    public void JossPaperDrawsWhenItsCountIsReached()
    {
        var i = new PlayChain.DrawInputs { Exhausts = 2, HandCount = 5, DrawPile = 10, JossPaperCount = 2, JossPaperEvery = 5 };
        Assert.Equal(0, PlayChain.Resolve(i).Draws);
        i.JossPaperCount = 3;
        Assert.Equal(1, PlayChain.Resolve(i).Draws);
    }

    [Fact]
    public void IronClubAndGamePieceAddDrawsAfterThePlay()
    {
        var i = new PlayChain.DrawInputs { HandCount = 5, DrawPile = 10, IronClubCount = 3, IronClubEvery = 4, IsPower = true, GamePiece = 1 };
        Assert.Equal(2, PlayChain.Resolve(i).Draws);
        i.Plays = 2; // Echo Form: two plays, Iron Club completes once, Game Piece twice
        Assert.Equal(3, PlayChain.Resolve(i).Draws);
    }

    [Fact]
    public void ViciousAndCentennialPuzzle()
    {
        var i = new PlayChain.DrawInputs { HandCount = 5, DrawPile = 10, Vicious = 1, VulnerableLandings = 2, LosesHp = true, CentennialPuzzle = 3 };
        Assert.Equal(5, PlayChain.Resolve(i).Draws);
        i.LosesHp = false;
        Assert.Equal(2, PlayChain.Resolve(i).Draws);
    }

    [Fact]
    public void CleanseShrinksTheDrawPileBeforeDrawing()
    {
        // Cleanse exhausts from the draw pile: with Dark Embrace, its draws come from a smaller pile.
        var i = new PlayChain.DrawInputs { Exhausts = 2, ExhaustedFromDrawPile = 1, HandCount = 5, DrawPile = 2, DiscardPile = 3, DarkEmbrace = 1 };
        var r = PlayChain.Resolve(i);
        Assert.Equal(2, r.Draws);
        Assert.True(r.Shuffles);
    }


    [Fact]
    public void SwiftDrawsAfterTheCardsItsCardPutInYourHand()
    {
        // Vitriol with Swift from a full hand: Vitriol leaves (9), its Sadness goes into your hand (10), then Swift's
        // draw (EnchantmentModel.OnPlay runs after the card's play) finds no room.
        var full = new PlayChain.DrawInputs { HandCount = 10, DrawPile = 7, EnchantmentDraws = 1, AddedToHandBeforeEnchantment = 1 };
        Assert.Equal(0, PlayChain.Resolve(full).Draws);
        // One card less in hand: room for it.
        var nine = new PlayChain.DrawInputs { HandCount = 9, DrawPile = 7, EnchantmentDraws = 1, AddedToHandBeforeEnchantment = 1 };
        Assert.Equal(1, PlayChain.Resolve(nine).Draws);
    }
    [Fact]
    public void CalculatedGambleRefillsAnEmptyDiscardPileBeforeDrawing()
    {
        // Discards the 4 other cards, then draws 4 from a 2-card draw pile: the discards are shuffled in.
        var i = new PlayChain.DrawInputs { OwnDraws = 4, HandCount = 5, DrawPile = 2, DiscardPile = 0, DiscardedBeforeDraw = 4 };
        var r = PlayChain.Resolve(i);
        Assert.Equal(4, r.Draws);
        Assert.True(r.Shuffles);
    }

    [Fact]
    public void RebootShufflesEvenWhenItsDrawsFit()
    {
        // Reboot: your hand goes into the draw pile, CardPileCmd.Shuffle, then draw 4. The shuffle
        // happens whatever the piles hold (play test: draw pile 10, discard 1, not predicted).
        var i = new PlayChain.DrawInputs { OwnDraws = 4, HandCount = 6, DrawPile = 10, DiscardPile = 1, ShufflesFirst = true };
        var r = PlayChain.Resolve(i);
        Assert.True(r.Shuffles);
        Assert.Equal(4, r.Draws);

        // An empty discard pile still shuffles (the draw pile and your hand).
        i.DiscardPile = 0;
        Assert.True(PlayChain.Resolve(i).Shuffles);

        // No Draw stops the draws, not the shuffle.
        i.Blocked = true;
        r = PlayChain.Resolve(i);
        Assert.True(r.Shuffles);
        Assert.Equal(0, r.Draws);
    }

    [Fact]
    public void RebootDrawsFromEveryCardYouHaveWithAWholeHandOfRoom()
    {
        // Your hand is in the draw pile first: a hand of 10 still has room for all 4 draws.
        var full = new PlayChain.DrawInputs { OwnDraws = 4, HandCount = 10, DrawPile = 5, DiscardPile = 5, ShufflesFirst = true };
        Assert.Equal(4, PlayChain.Resolve(full).Draws);

        // The draws can't exceed the cards there are: draw 1 + discard 1 + the other cards in hand.
        var few = new PlayChain.DrawInputs { OwnDraws = 4, HandCount = 3, DrawPile = 1, DiscardPile = 1, ShufflesFirst = true };
        Assert.Equal(4, PlayChain.Resolve(few).Draws);
        few.HandCount = 2;
        Assert.Equal(3, PlayChain.Resolve(few).Draws);
    }

    [Fact]
    public void DrawsStopWhenYouRunOutOfCards()
    {
        // CardPileCmd.Draw stops when both piles are empty: draw 3 with 1 card in the draw pile and
        // 1 in the discard pile draws 2 (shuffling once).
        var i = new PlayChain.DrawInputs { OwnDraws = 3, HandCount = 3, DrawPile = 1, DiscardPile = 1 };
        var r = PlayChain.Resolve(i);
        Assert.Equal(2, r.Draws);
        Assert.True(r.Shuffles);

        i.DiscardPile = 0;
        r = PlayChain.Resolve(i);
        Assert.Equal(1, r.Draws);
        Assert.False(r.Shuffles);
    }

    [Fact]
    public void CardsYourPowersAddAfterThePlayFillTheHandBeforeIronClubDraws()
    {
        // Auto-reports: FTL with 10 cards in hand (9 after it leaves), Iron Club completing its count
        // and Calamity 1. Hook order (CombatState.IterateHookListeners): your powers, then your relics.
        // Calamity's attack makes 10, so Iron Club draws nothing.
        var i = new PlayChain.DrawInputs { HandCount = 10, DrawPile = 6, DiscardPile = 30, IronClubCount = 3, IronClubEvery = 4, AddedToHandAfterPlay = 1 };
        Assert.Equal(0, PlayChain.Resolve(i).Draws);

        // Without Calamity, Iron Club draws into the last slot.
        i.AddedToHandAfterPlay = 0;
        Assert.Equal(1, PlayChain.Resolve(i).Draws);

        // The card's own draws come first and still happen (during the play, before Calamity).
        i = new PlayChain.DrawInputs { OwnDraws = 1, HandCount = 9, DrawPile = 6, DiscardPile = 30, IronClubCount = 3, IronClubEvery = 4, AddedToHandAfterPlay = 1 };
        Assert.Equal(1, PlayChain.Resolve(i).Draws);
    }

    [Fact]
    public void MotifDrawsAfterTheFirstMelodyOfEachKindAndCanShuffle()
    {
        // MotifPower.AfterCardPlayed: Amount cards per melody (Wah, Cho, Ki) the card plays first this turn.
        // Auto-report: Cho Firm (first Cho, Motif 1) with an empty draw pile and 11 in discard: 1 draw, shuffled in.
        var i = new PlayChain.DrawInputs { HandCount = 5, DrawPile = 0, DiscardPile = 11, PowerDrawsAfterPlay = 1 };
        var r = PlayChain.Resolve(i);
        Assert.Equal(1, r.Draws);
        Assert.Equal(1, r.Chained);
        Assert.True(r.Shuffles);

        // Wah Cho Ki after a Cho this turn: its own 2 draws, then Motif's 2 (Wah, Ki) run past a draw pile of 3.
        i = new PlayChain.DrawInputs { OwnDraws = 2, HandCount = 5, DrawPile = 3, DiscardPile = 11, PowerDrawsAfterPlay = 2 };
        r = PlayChain.Resolve(i);
        Assert.Equal(4, r.Draws);
        Assert.True(r.Shuffles);

        // Wah Ki with a full hand: it leaves one slot, so Motif's 2 draws stop at 1 (still shuffling the empty pile in).
        i = new PlayChain.DrawInputs { HandCount = 10, DrawPile = 0, DiscardPile = 17, PowerDrawsAfterPlay = 2 };
        r = PlayChain.Resolve(i);
        Assert.Equal(1, r.Draws);
        Assert.True(r.Shuffles);

        // Enough cards in the draw pile: no shuffle.
        i = new PlayChain.DrawInputs { HandCount = 5, DrawPile = 2, DiscardPile = 11, PowerDrawsAfterPlay = 2 };
        Assert.Equal(2, PlayChain.Resolve(i).Draws);
        Assert.False(PlayChain.Resolve(i).Shuffles);

        // No Draw blocks them.
        i.Blocked = true;
        Assert.Equal(0, PlayChain.Resolve(i).Draws);
    }

    [Fact]
    public void KiChantReplayedShufflesWhenALaterPlayFindsTheDrawPileShort()
    {
        // KiChant.OnPlay: looks at the top 4, takes 1, discards the rest; a draw pile short of 4 is shuffled
        // (with the discard pile holding cards). Auto-report: Ki Chant+ played 4 times (draw pile 14, discard 2):
        // 14 → 10 → 6 → 2, so the fourth play shuffles.
        Assert.True(PlayChain.ShortPileShufflesOnLaterPlay(drawPile: 14, discardPile: 2, looksAt: 4, plays: 4));
        // Three plays leave 2 behind without shuffling.
        Assert.False(PlayChain.ShortPileShufflesOnLaterPlay(14, 2, 4, 3));
        // One play: only the first play's check (ShufflesFirst) applies.
        Assert.False(PlayChain.ShortPileShufflesOnLaterPlay(3, 5, 4, 1));
        // The first play shuffles already: not a later play's shuffle.
        Assert.False(PlayChain.ShortPileShufflesOnLaterPlay(3, 5, 4, 2));
        // Empty discard pile: the first play's 3 discards fill it, so the second play still shuffles.
        Assert.True(PlayChain.ShortPileShufflesOnLaterPlay(6, 0, 4, 2));
        // Looking at a single card discards nothing: with no discard pile, nothing to shuffle in.
        Assert.False(PlayChain.ShortPileShufflesOnLaterPlay(1, 0, 1, 3));
    }
}
