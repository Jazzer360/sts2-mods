using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Lunar Pastry (decompile v0.113.1): AfterSideTurnEnd gives you stars, and Black Hole (AfterStarsGained) deals its amount
/// to every hittable enemy (unpowered). Relics run in your relic order, so it lands before or after Parrying Shield's
/// random hit.
/// </summary>
public class SimLunarPastryTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    [Fact]
    public void BlackHoleHitsEveryEnemyAtTheEndOfYourTurn()
    {
        var s = new SimState { Enemies = { Enemy(1, 4, 9), Enemy(2, 10, 3) } };
        s.P.LunarBlackHole = 4;
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.Equal(3, o.TakenMax);
        Assert.All(o.Branches, b => Assert.Equal(6, b.Enemy(2)!.HpAtTurnEnd));
    }

    [Fact]
    public void BeforeParryingShieldItLeavesFewerTargets()
    {
        // Lunar Pastry first: Black Hole kills E1, so Parrying Shield's 6 can only hit E2 (10 - 4 - 6 = 0).
        var s = new SimState { Enemies = { Enemy(1, 4, 9), Enemy(2, 10, 3) } };
        s.P.LunarBlackHole = 4;
        s.P.ParryingShieldDamage = 6;
        s.P.ParryingShieldBlock = 0;
        Assert.True(SimEngine.Run(s).DeadInAll(2));
        // Parrying Shield first: its hit lands on either enemy at random.
        s.P.LunarBeforeParrying = false;
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAny(2));
        Assert.False(o.DeadInAll(2));
    }

    [Fact]
    public void ATeammatesCountsToo()
    {
        var s = new SimState { Enemies = { Enemy(1, 4, 9) } };
        s.Teammates.Add(new SimPlayer { Hp = 30, LunarBlackHole = 4 });
        Assert.True(SimEngine.Run(s).DeadInAll(1));
    }

    [Fact]
    public void InsatiableDominionLandsAfterParryingShield()
    {
        // Its damage is an AfterSideTurnEndLate hit on you (read as a late debuff): Parrying Shield still sees your block.
        var s = new SimState { Enemies = { Enemy(1, 6, 0) } };
        s.P.Block = 5;
        s.P.ParryingShieldBlock = 5;
        s.P.ParryingShieldDamage = 6;
        s.P.DebuffLateBlockable.Add(("Insatiable Dominion", 7));
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.Equal(2, o.TakenMax);
    }
}
