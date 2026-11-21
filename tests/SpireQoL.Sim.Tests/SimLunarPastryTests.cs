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
    private static void Lunar(SimPlayer player, int blackHole)
    {
        player.Relics = player.Relics.With(RelicId.LunarPastry, amount: 1);
        player.Powers = player.Powers.With(PowerId.BlackHole, blackHole);
    }

    [Fact]
    public void BlackHoleHitsEveryEnemyAtTheEndOfYourTurn()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 4, hits: 9), Enemy(id: 2, hp: 10, hits: 3) } };
        Lunar(state.You, blackHole: 4);
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(3, outcome.TakenMax);
        Assert.All(outcome.Branches, branch => Assert.Equal(6, branch.Enemy(id: 2)!.HpAtTurnEnd));
    }

    [Fact]
    public void BeforeParryingShieldItLeavesFewerTargets()
    {
        // Lunar Pastry first: Black Hole kills E1, so Parrying Shield's 6 can only hit E2 (10 - 4 - 6 = 0).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 4, hits: 9), Enemy(id: 2, hp: 10, hits: 3) } };
        Lunar(state.You, blackHole: 4);
        state.You.ParryingShieldDamage = 6;
        state.You.ParryingShieldBlock = 0;
        Assert.True(SimEngine.Run(state).DeadInAll(enemyId: 2));
        // Parrying Shield first in your relics: its hit lands on either enemy at random.
        var branch = new SimState { Enemies = { Enemy(id: 1, hp: 4, hits: 9), Enemy(id: 2, hp: 10, hits: 3) } };
        branch.You.ParryingShieldDamage = 6;
        branch.You.ParryingShieldBlock = 0;
        Lunar(branch.You, blackHole: 4);
        var outcome = SimEngine.Run(branch);
        Assert.True(outcome.DeadInAny(enemyId: 2));
        Assert.False(outcome.DeadInAll(enemyId: 2));
    }

    [Fact]
    public void ATeammatesCountsToo()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 4, hits: 9) } };
        var mate = new SimPlayer { Hp = 30 };
        Lunar(mate, blackHole: 4);
        state.Teammates.Add(mate);
        Assert.True(SimEngine.Run(state).DeadInAll(enemyId: 1));
    }

    [Fact]
    public void InsatiableDominionLandsAfterParryingShield()
    {
        // Its damage is an AfterSideTurnEndLate hit on you (its model): Parrying Shield still sees your block.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 6, hits: 0) } };
        state.You.Block = 5;
        state.You.ParryingShieldBlock = 5;
        state.You.ParryingShieldDamage = 6;
        state.You.Powers = state.You.Powers.With(PowerId.InsatiableDominion, amount: 1);
        state.Vars["-1:InsatiableDominionPower:Damage"] = 7;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(2, outcome.TakenMax);
    }
}
