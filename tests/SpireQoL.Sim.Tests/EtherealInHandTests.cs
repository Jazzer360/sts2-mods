using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card you pick made Ethereal (EtherealInHandAction, decision #348; decompile v0.114.0: Sculpting Strike's CardCmd.ApplyKeyword of Ethereal on a card of
/// your hand not Ethereal on its own, CombatManager.DoTurnEnd exhausting every Ethereal card left in hand). A pick is a card (decision #188): one that doesn't
/// fade yet when there is one, a Hexed one otherwise, none with nothing to pick.
/// </summary>
public class EtherealInHandTests
{
    private static SimState You(int hand)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 50, Hits = new() { 10 } } } };
        state.You.HandCount = hand;
        state.You.Hp = 50;
        return state;
    }

    /// <summary>Sculpting Strike from your hand: it leaves it, then makes a card there Ethereal.</summary>
    private static SimPlay SculptingStrike() => new() { Name = "SculptingStrike", HandCard = 7, HandDelta = -1, Body = { new EtherealInHandAction() } };

    [Fact]
    public void ThePickedCardExhaustsAsTheTurnEnds()
    {
        // Feel No Pain 3: the card made Ethereal exhausts as the turn ends, 3 block against the 10.
        var state = You(hand: 4);
        state.You.Powers = state.You.Powers.With(PowerId.FeelNoPain, amount: 3);
        var outcome = SimEngine.Run(state, SculptingStrike());
        Assert.Equal(7, outcome.TakenMax);
        Assert.Equal(7, outcome.TakenMin);
    }

    [Fact]
    public void WithNothingElseInHandNothingIsPicked()
    {
        var state = You(hand: 1);
        state.You.Powers = state.You.Powers.With(PowerId.FeelNoPain, amount: 3);
        var outcome = SimEngine.Run(state, SculptingStrike());
        Assert.Equal(10, outcome.TakenMax);
    }

    [Fact]
    public void AHandOfEtherealCardsHasNothingToPick()
    {
        // Two cards left, both Ethereal on their own: the pick's filter leaves none, so two exhausts, not three.
        var state = You(hand: 3);
        state.You.Fading = 2;
        state.You.Powers = state.You.Powers.With(PowerId.FeelNoPain, amount: 3);
        var outcome = SimEngine.Run(state, SculptingStrike());
        Assert.Equal(4, outcome.TakenMax);
    }

    [Fact]
    public void AHexedCardPickedFadesOnItsOwn()
    {
        // The only card left fades through Hex alone: picked, it's Ethereal on its own, still one exhaust.
        var state = You(hand: 2);
        state.You.Fading = 1;
        state.You.HexFading = 1;
        var outcome = SimEngine.Run(state, SculptingStrike());
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(0, branch.You.HexFading);
    }
}
