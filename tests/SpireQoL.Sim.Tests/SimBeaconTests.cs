using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Beacon of Hope (decompile v0.113.1, BeaconOfHopePower.AfterBlockGained): half of each block its owner gains on the
/// players' side goes to every other player. A teammate's end-of-turn block (Plating, Orichalcum, Frost) shares too.
/// </summary>
public class SimBeaconTests
{
    [Fact]
    public void ATeammatesEndOfTurnBlockSharesHalfWithYou()
    {
        // Their Plating 6 at end of turn: 3 to you, before the enemy's 10.
        var s = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = new() { 10 } } } };
        s.Teammates.Add(new SimPlayer { Hp = 40, Plating = 6, BeaconOfHope = true });
        Assert.Equal(7, SimEngine.Run(s).TakenMax);
    }

    [Fact]
    public void ATeammatesFrostSharesToo()
    {
        var s = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = new() { 10 } } } };
        var t = new SimPlayer { Hp = 40, BeaconOfHope = true, OrbCapacity = 3 };
        t.Orbs.Add(new SimOrb { Kind = OrbKind.Frost, Passive = 4, Evoke = 5 });
        s.Teammates.Add(t);
        Assert.Equal(8, SimEngine.Run(s).TakenMax); // 4 Frost for them, 2 for you
    }
}
