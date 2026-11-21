using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Mighty Wings (decompile v0.113.1): a powered attack hit of yours or Osty's whose DamageResult.TotalDamage (blocked plus
/// unblocked; unblocked stops at the HP the enemy had) is at least 30 gives 2 Strength. The simulator keeps the play's
/// biggest hit; good, so the tab needs it in every branch.
/// </summary>
public class SimBiggestHitTests
{
    private static SimEnemy Enemy(int id, int hp, int block = 0) => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = new() { 5 } };

    private static SimPlay Hit(int target, int damage)
        => new() { Name = "Bludgeon", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = 1 };

    [Fact]
    public void BlockCountsTowardTheHit()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, block: 12) } };
        Assert.Equal(32, SimEngine.Run(state, Hit(target: 1, damage: 32)).PlayBiggestHitMin);
    }

    [Fact]
    public void OverkillDoesnt()
    {
        // 32 into 20 HP and no block: 20 dealt, not enough.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 20) } };
        Assert.Equal(20, SimEngine.Run(state, Hit(target: 1, damage: 32)).PlayBiggestHitMin);
    }

    [Fact]
    public void RandomHitsNeedEveryBranch()
    {
        // 30 at a random enemy: 30 on the 50-HP one, 10 on the 10-HP one.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50), Enemy(id: 2, hp: 10) } };
        var play = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, Hits = 1, HitDamage = { [1] = 30, [2] = 30 } };
        Assert.Equal(10, SimEngine.Run(state, play).PlayBiggestHitMin);
    }
}
