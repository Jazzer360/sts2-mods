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

    /// <summary>Lunar Pastry (its stars: 1) with Black Hole at this amount.</summary>
    private static void Lunar(SimPlayer p, int blackHole)
    {
        p.Relics = p.Relics.With("LunarPastry", 1);
        p.Powers = p.Powers.With("BlackHolePower", blackHole);
    }

    [Fact]
    public void BlackHoleHitsEveryEnemyAtTheEndOfYourTurn()
    {
        var s = new SimState { Enemies = { Enemy(1, 4, 9), Enemy(2, 10, 3) } };
        Lunar(s.P, 4);
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
        Lunar(s.P, 4);
        s.P.ParryingShieldDamage = 6;
        s.P.ParryingShieldBlock = 0;
        Assert.True(SimEngine.Run(s).DeadInAll(2));
        // Parrying Shield first in your relics: its hit lands on either enemy at random.
        var t = new SimState { Enemies = { Enemy(1, 4, 9), Enemy(2, 10, 3) } };
        t.P.ParryingShieldDamage = 6;
        t.P.ParryingShieldBlock = 0;
        Lunar(t.P, 4);
        var o = SimEngine.Run(t);
        Assert.True(o.DeadInAny(2));
        Assert.False(o.DeadInAll(2));
    }

    [Fact]
    public void ATeammatesCountsToo()
    {
        var s = new SimState { Enemies = { Enemy(1, 4, 9) } };
        var mate = new SimPlayer { Hp = 30 };
        Lunar(mate, 4);
        s.Teammates.Add(mate);
        Assert.True(SimEngine.Run(s).DeadInAll(1));
    }

    [Fact]
    public void InsatiableDominionLandsAfterParryingShield()
    {
        // Its damage is an AfterSideTurnEndLate hit on you (its model): Parrying Shield still sees your block.
        var s = new SimState { Enemies = { Enemy(1, 6, 0) } };
        s.P.Block = 5;
        s.P.ParryingShieldBlock = 5;
        s.P.ParryingShieldDamage = 6;
        s.P.Powers = s.P.Powers.With("InsatiableDominionPower", 1);
        s.Vars["-1:InsatiableDominionPower:Damage"] = 7;
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.Equal(2, o.TakenMax);
    }
}
