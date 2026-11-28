using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards you pick from your hand put on top of your draw pile (TakeFromHandAction.OntoDrawTop; decompile v0.114.0: Glimmer's, Thinking Ahead's and
/// Photon Cut's CardSelectCmd.FromHand, then CardPileCmd.Add to PileType.Draw at CardPilePosition.Top). They leave your hand and join the draw pile's
/// top, so a draw later in the same simulation (a replay of the card, a draw a listener makes) draws them, not a shuffle. Which card is your later
/// decision (decisions #167, #415): a plain one when your hand held one as the play started.
/// </summary>
public class SimHandOntoDrawTopTests
{
    private static readonly SimDrawCard Plain = new("plain");
    private static readonly SimDrawCard Burn = new("burn") { TurnEnd = new TurnEndCard("Burn", "Burn", Amount: 2) };

    private static SimState You(int hand, int drawPile, int discardPile)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.HandCount = hand;
        state.You.Hp = 50;
        state.You.Draw = new SimDrawPile
        {
            Rest = drawPile > 0 ? new List<SimDrawGroup> { new(Plain, drawPile) } : new List<SimDrawGroup>(),
            Discard = discardPile > 0 ? new List<SimDrawGroup> { new(Plain, discardPile) } : new List<SimDrawGroup>(),
        };
        return state;
    }

    /// <summary>A card that puts a card from your hand on top of your draw pile, then draws (as a listener's draw after it, or a later play's, would).</summary>
    private static SimPlay PutBackThenDraw(int drawsAfter, IReadOnlyList<SimDrawGroup> hand) => new()
    {
        Name = "PutBack", HandCard = 7, HandDelta = -1,
        Body = { new TakeFromHandAction(HandTakeWay.Pick) { OntoDrawTop = hand }, new CardEventAction(CardEvent.Drawn, drawsAfter) },
    };

    [Fact]
    public void TheDrawAfterThePickDrawsThePickedCardWithoutAShuffle()
    {
        // An empty draw pile, three cards in the discard pile, two in your hand besides the card played: the card put back is drawn, nothing shuffles
        // (the discard pile keeps its three, and the card played joins them).
        var outcome = SimEngine.Run(You(hand: 3, drawPile: 0, discardPile: 3), PutBackThenDraw(drawsAfter: 1, hand: new[] { new SimDrawGroup(Plain, Count: 2) }));
        var branch = Assert.Single(outcome.Branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(0, branch.You.Draw!.Count);
        Assert.Equal(3 + 1, branch.You.Draw.DiscardCount);
        Assert.Equal(2, branch.You.HandCount);
    }

    [Fact]
    public void WithoutADrawThePickedCardStaysOnTop()
    {
        var outcome = SimEngine.Run(You(hand: 3, drawPile: 2, discardPile: 0), PutBackThenDraw(drawsAfter: 0, hand: new[] { new SimDrawGroup(Burn, Count: 1), new SimDrawGroup(Plain, Count: 1) }));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(3, branch.You.Draw!.Count);
        Assert.Equal("plain", Assert.Single(branch.You.Draw.Top).Key);
        Assert.Equal(1, branch.You.HandCount);
    }

    [Fact]
    public void AReplayedPutBackDrawsTheCardItsFirstPlayPutBack()
    {
        // Thinking Ahead played twice with two cards in your draw pile and none in your discard pile: the first play draws both and puts one back;
        // the second draws that one, then finds both piles empty, and puts one back.
        var thinkingAhead = new SimPlay
        {
            Name = "ThinkingAhead", HandCard = 7, HandDelta = -1, Plays = 2,
            Body = { new CardEventAction(CardEvent.Drawn, Count: 2), new TakeFromHandAction(HandTakeWay.Pick) { OntoDrawTop = new[] { new SimDrawGroup(Plain, Count: 1) } } },
        };
        var branch = Assert.Single(SimEngine.Run(You(hand: 2, drawPile: 2, discardPile: 0), thinkingAhead).Branches);
        Assert.Equal(3, branch.You.Drawn);
        Assert.Equal(1, branch.You.Draw!.Count);
        Assert.Equal(2, branch.You.HandCount);
    }

    [Fact]
    public void AmongCardsTheSimulationFollowsThePickIsYoursAndItsDrawUnknown()
    {
        // Your hand held only cards the simulation follows, of two kinds: which goes on top is yours, so drawing it is unknown; not drawn, nothing is.
        var ethereal = new SimDrawCard("ethereal") { Fading = true };
        var hand = new[] { new SimDrawGroup(Burn, Count: 1), new SimDrawGroup(ethereal, Count: 1) };
        Assert.All(SimEngine.Run(You(hand: 3, drawPile: 0, discardPile: 2), PutBackThenDraw(drawsAfter: 1, hand)).Branches, branch => Assert.NotNull(branch.Unmodelled));
        var kept = Assert.Single(SimEngine.Run(You(hand: 3, drawPile: 0, discardPile: 2), PutBackThenDraw(drawsAfter: 0, hand)).Branches);
        Assert.Equal(1, kept.You.Draw!.Count);
    }

    [Fact]
    public void WithNoPileReadOnlyTheHandsCountMoves()
    {
        var state = You(hand: 3, drawPile: 0, discardPile: 0);
        state.You.Draw = null;
        var branch = Assert.Single(SimEngine.Run(state, PutBackThenDraw(drawsAfter: 0, hand: new[] { new SimDrawGroup(Plain, Count: 2) })).Branches);
        Assert.Equal(1, branch.You.HandCount);
    }
}
