using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A choice inside a play is your later decision (decision #167): the play's prediction takes the option that changes nothing the
/// simulation follows when there is one, and is unknown only when every option is one it follows. Here: cards a play takes from your
/// hand (LeaveHandAction: Survivor's discard, Burning Pact's exhaust).
/// </summary>
public class SimChoiceTests
{
    private static SimState Hand(int cards, int burns)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.HandCount = cards;
        state.You.Hp = 50;
        for (var i = 0; i < burns; i++) state.You.TurnEndCards.Add(new TurnEndCard("Burn", "Burn", Amount: 2));
        return state;
    }

    private static SimPlay Discards(int count) => new() { Name = "Survivor", HandCard = 7, HandDelta = -1, Body = { new LeaveHandAction(count) } };

    [Fact]
    public void ADiscardYouChooseKeepsTheCardsTheSimulationFollows()
    {
        // Survivor (played from a hand of 4: a Burn and 2 plain cards left) discards a plain card: the Burn still burns.
        var branch = Assert.Single(SimEngine.Run(Hand(cards: 4, burns: 1), Discards(count: 1)).Branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(2, branch.You.Taken);
    }

    [Fact]
    public void ADiscardWithOnlyCardsTheSimulationFollowsLeftIsUnknown()
    {
        // Survivor with only the Burn left beside it: the discard may take it.
        Assert.NotNull(Assert.Single(SimEngine.Run(Hand(cards: 2, burns: 1), Discards(count: 1)).Branches).Unmodelled);
    }
}
