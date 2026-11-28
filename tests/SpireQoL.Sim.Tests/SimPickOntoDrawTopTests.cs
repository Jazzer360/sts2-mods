using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card you pick from your discard pile put on top of your draw pile (PickDiscardOntoDrawTopAction; decompile v0.114.0: Wah Ki's and Cosmic
/// Indifference's CardSelectCmd.FromCombatPile of the discard pile, then CardPileCmd.Add to the draw pile at CardPilePosition.Top). From the owner's
/// Fool run: Wah Ki with an empty draw pile, then Motif's draw drew the card it put there; the Shuffle tab said the draw would shuffle. Which card
/// is your later decision (decision #167): a plain one when the pile has one.
/// </summary>
public class SimPickOntoDrawTopTests
{
    private static readonly SimDrawCard Plain = new("plain");
    private static readonly SimDrawCard Burn = new("burn") { TurnEnd = new TurnEndCard("Burn", "Burn", Amount: 2) };

    private static SimState You(params (SimDrawCard Card, int Count)[] discard)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.HandCount = 5;
        state.You.Hp = 50;
        state.You.Draw = new SimDrawPile { Discard = discard.Select(cardCount => new SimDrawGroup(cardCount.Card, cardCount.Count)).ToList() };
        return state;
    }

    private static SimPlay WahKi(int drawsAfter) => new()
    {
        Name = "WahKi", HandCard = 7, HandDelta = -1 + drawsAfter, Draws = drawsAfter,
        Body = { new PickDiscardOntoDrawTopAction(), new CardEventAction(CardEvent.Drawn, drawsAfter) },
    };

    [Fact]
    public void TheDrawAfterThePickDrawsThePickedCardWithoutAShuffle()
    {
        var outcome = SimEngine.Run(You((Plain, 3), (Burn, 1)), WahKi(drawsAfter: 1));
        var branch = Assert.Single(outcome.Branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(0, branch.You.Draw!.Count);
        // The plain card was picked and drawn; the Burn and the other two stay in the discard pile, with Wah Ki once played.
        Assert.Equal(1, branch.You.Draw.Discard.Single(group => group.Card.Key == "burn").Count);
        Assert.Equal(2 + 1, branch.You.Draw.DiscardCount - 1);
    }

    [Fact]
    public void WithoutADrawThePickedCardStaysOnTop()
    {
        var outcome = SimEngine.Run(You((Plain, 2)), WahKi(drawsAfter: 0));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(1, branch.You.Draw!.Count);
        Assert.Equal("plain", Assert.Single(branch.You.Draw.Top).Key);
    }

    [Fact]
    public void ADrawOfASecondCardShufflesTheRestIn()
    {
        var outcome = SimEngine.Run(You((Plain, 3)), WahKi(drawsAfter: 2));
        Assert.All(outcome.Branches, branch => Assert.Equal(0, branch.You.Draw!.DiscardCount - 1));
    }

    [Fact]
    public void AnEmptyDiscardPileMovesNothing()
    {
        var outcome = SimEngine.Run(You(), WahKi(drawsAfter: 0));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(0, branch.You.Draw!.Count);
    }

    [Fact]
    public void AmongCardsTheSimulationFollowsEachThePickIsYoursAndItsDrawUnknown()
    {
        var ethereal = new SimDrawCard("ethereal") { Fading = true };
        var outcome = SimEngine.Run(You((Burn, 1), (ethereal, 2)), WahKi(drawsAfter: 1));
        Assert.All(outcome.Branches, branch => Assert.NotNull(branch.Unmodelled));
        // Not drawn, its count is right and nothing is unknown.
        var kept = Assert.Single(SimEngine.Run(You((Burn, 1), (ethereal, 2)), WahKi(drawsAfter: 0)).Branches);
        Assert.Null(kept.Unmodelled);
        Assert.Equal(1, kept.You.Draw!.Count);
        Assert.Equal(2 + 1, kept.You.Draw.DiscardCount);
    }
}
