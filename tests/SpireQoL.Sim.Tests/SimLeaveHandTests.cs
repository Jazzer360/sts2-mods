using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards a play discards or exhausts from your hand leave it as they go (LeaveHandAction, CardCmd.Discard and CardCmd.Exhaust
/// move them out): the hand is smaller from then on, so a draw after it has their room, and what reads your hand at the end of the
/// turn counts what's left. A play that takes the whole hand takes what was in it too.
/// </summary>
public class SimLeaveHandTests
{
    private static readonly SimDrawCard Skill = new("skill") { Type = SimCardType.Skill };

    private static SimState Hand(int cards, int pile = 0)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 50 } } };
        state.You.HandCount = cards;
        if (pile > 0) state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(Skill, pile) } };
        return state;
    }

    [Fact]
    public void ADiscardLeavesTheHandSmaller()
    {
        // Survivor in a hand of 5: it goes, and so does the card it discards: 3 left.
        var play = new SimPlay { Name = "Survivor", HandCard = 7, HandDelta = -1, Body = { new CardEventAction(CardEvent.Discarded, Count: 1), new LeaveHandAction(Count: 1) } };
        Assert.Equal(3, Assert.Single(SimEngine.Run(Hand(cards: 5), play).Branches).You.HandCount);
    }

    [Fact]
    public void DiscardingAFullHandThenDrawingHasTheRoomItLeft()
    {
        // Calculated Gamble in a full hand of 10: the other 9 go, then it draws 9 (with room for them): 9 in hand after.
        var play = new SimPlay
        {
            Name = "CalculatedGamble", HandCard = 7, HandDelta = -1 + 9, Draws = 9,
            Body = { new CardEventAction(CardEvent.Discarded, Count: 9), new LeaveHandAction(Count: 0) { Whole = true }, new CardEventAction(CardEvent.Drawn, Count: 9) },
        };
        var branch = Assert.Single(SimEngine.Run(Hand(cards: 10, pile: 20), play).Branches);
        Assert.Equal(9, branch.You.Drawn);
        Assert.Equal(9, branch.You.HandCount);
    }

    [Fact]
    public void ExhaustingTheWholeHandWithDarkEmbraceDrawsIntoTheRoomItLeft()
    {
        // Fiend Fire in a full hand of 10 with Dark Embrace and 6 cards to draw: its 9 others leave, each exhaust draws one (a dev
        // review: 2 were predicted, the hand still counted full), and the cards drawn stay in hand: 6.
        var state = Hand(cards: 10, pile: 6);
        state.You.Powers = PowerSet.Empty.With(PowerId.DarkEmbrace, amount: 1);
        var play = new SimPlay
        {
            Name = "FiendFire", HandCard = 7, HandDelta = -1,
            Body = { new LeaveHandAction(Count: 0) { Whole = true }, new CardEventAction(CardEvent.Exhausted, Count: 9) { Indirect = true } },
        };
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        Assert.Equal(6, branch.You.Drawn);
        Assert.Equal(6, branch.You.HandCount);
        Assert.Equal(6, branch.Fired.DataOf("DarkEmbracePower"));
    }

    [Fact]
    public void TakingTheWholeHandTakesItsBurnsAndEtherealCards()
    {
        // Storm of Steel with a Burn and an Ethereal card in hand: both are discarded, so neither acts at the end of the turn.
        var state = Hand(cards: 4);
        state.You.TurnEndCards = new() { new TurnEndCard("Burn", "Burn", Amount: 2) };
        state.You.Fading = 1;
        var play = new SimPlay { Name = "StormOfSteel", HandCard = 7, HandDelta = -1, Body = { new LeaveHandAction(Count: 0) { Whole = true } } };
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        Assert.Equal(0, branch.You.HandCount);
        Assert.Empty(branch.You.TurnEndCards);
        Assert.Equal(0, branch.You.Fading);
    }

    [Fact]
    public void ADiscardThatMayTakeASlyCardIsUnknown()
    {
        // A Sly card it discards plays itself (CardCmd.Discard): not simulated, so the play says why instead of a certainty.
        var play = new SimPlay { Name = "Survivor", HandCard = 7, HandDelta = -1, Body = { new NotModelledAction("a Sly card in your hand it may discard plays itself"), new LeaveHandAction(Count: 1) } };
        Assert.Equal("a Sly card in your hand it may discard plays itself", Assert.Single(SimEngine.Run(Hand(cards: 3), play).Branches).Unmodelled);
    }

    [Fact]
    public void NoMoreLeavesThanTheHandHolds()
    {
        var play = new SimPlay { Name = "Prepared", HandCard = 7, HandDelta = -1, Body = { new LeaveHandAction(Count: 3) } };
        Assert.Equal(0, Assert.Single(SimEngine.Run(Hand(cards: 2), play).Branches).You.HandCount);
    }
}
