using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Consuming Shadow read from its code (decision #291): AfterSideTurnEnd, `participants.Contains(base.Owner) && OrbQueue.Orbs.Count != 0`, then
/// OrbCmd.EvokeLast(base.Owner.Player) its amount of times (a counter loop): the last orb evoked and dequeued each time, nothing once none is left.
/// </summary>
public class ConsumingShadowTests
{
    private static SimState Board(int amount, params SimOrb[] orbs)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40 } } };
        state.You.Hp = 50;
        state.You.OrbCapacity = 3;
        state.You.Orbs = orbs.ToList();
        state.You.Powers = state.You.Powers.With(PowerId.ConsumingShadow, amount);
        return state;
    }

    private static SimOrb Frost() => new() { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 };

    [Fact]
    public void ItEvokesTheLastOrbItsAmountOfTimesWhileThereIsOne()
    {
        Assert.IsType<ReadModel>(SimModels.Get(PowerId.ConsumingShadow.Name));
        // Two Frost: each passive's 2 as the turn ends, then two evokes of 5 (amount 3: the third finds no orb).
        var branch = Assert.Single(SimEngine.Run(Board(amount: 3, Frost(), Frost())).Branches);
        Assert.Empty(branch.You.Orbs);
        Assert.Equal(2 + 2 + 5 + 5, branch.BlockAtEnemyTurn);
        // No orbs: nothing evoked, and nothing logged.
        var none = Assert.Single(SimEngine.Run(Board(amount: 2)).Branches);
        Assert.Equal(0, none.BlockAtEnemyTurn);
        Assert.False(none.Fired.Has(PowerId.ConsumingShadow.Name));
    }
}
