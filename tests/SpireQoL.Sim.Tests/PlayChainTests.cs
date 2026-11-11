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
}
