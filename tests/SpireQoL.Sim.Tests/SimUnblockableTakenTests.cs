using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Incoming Damage shows HP loss no block stops (Regret, Beckon, Bad Luck: CardsActInHand; Demise) on its own line, under what
/// gets through your block: SimState.UnblockableTaken is that part of Taken, after the HP-loss modifiers.
/// </summary>
public class SimUnblockableTakenTests
{
    private static SimEnemy E(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    [Fact]
    public void RegretIsCountedApartFromTheHitsThroughYourBlock()
    {
        // Regret with 3 cards in hand (3, unblockable) and a hit of 10 against 6 block: 4 through, 7 in all.
        var s = new SimState { Enemies = { E(1, 30, 10) } };
        s.P.Block = 6;
        s.P.HandCount = 3;
        s.P.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", 0));
        var w = SimEngine.Run(s).Worst;
        Assert.Equal(7, w.Taken);
        Assert.Equal(3, w.UnblockableTaken);
    }

    [Fact]
    public void BlockDoesntTouchIt()
    {
        // Beckon's 6 with 20 block and nothing else coming: all 6 lost, all of it unblockable.
        var s = new SimState { Enemies = { E(1, 30) } };
        s.P.Block = 20;
        s.P.TurnEndCards.Add(new TurnEndCard("Beckon", "Beckon", 6));
        var w = SimEngine.Run(s).Worst;
        Assert.Equal(6, w.Taken);
        Assert.Equal(6, w.UnblockableTaken);
    }

    [Fact]
    public void ItsWhatYouLoseAfterTungstenRod()
    {
        // Bad Luck's 13 through Tungsten Rod: 12 lost.
        var s = new SimState { Enemies = { E(1, 30) } };
        s.P.TungstenRod = 1;
        s.P.TurnEndCards.Add(new TurnEndCard("BadLuck", "Bad Luck", 13));
        var w = SimEngine.Run(s).Worst;
        Assert.Equal(12, w.UnblockableTaken);
        Assert.Equal(12, w.Taken);
    }

    [Fact]
    public void BurnIsBlockable()
    {
        var s = new SimState { Enemies = { E(1, 30) } };
        s.P.TurnEndCards.Add(new TurnEndCard("Burn", "Burn", 2));
        var w = SimEngine.Run(s).Worst;
        Assert.Equal(2, w.Taken);
        Assert.Equal(0, w.UnblockableTaken);
    }
}
