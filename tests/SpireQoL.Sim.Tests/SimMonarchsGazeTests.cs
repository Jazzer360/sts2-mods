using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Monarch's Gaze (decompile v0.113.1): each of your powered attack hits takes Strength from the enemy it hits
/// (MonarchsGazePower.AfterDamageGiven). On a random-target attack, which enemy loses how much is a branch: the adapter
/// gives each enemy's hits after 1, 2... of the card's hits (SimPlay.GazeHits), and the simulator picks by the hits it took.
/// </summary>
public class SimMonarchsGazeTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    [Fact]
    public void EachBranchWeakensTheEnemiesItHit()
    {
        // Two random hits on two enemies attacking for 10; each hit takes 2 off that enemy's attack.
        var s = new SimState { Enemies = { Enemy(1, 50, 10), Enemy(2, 50, 10) } };
        var p = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, Hits = 2, HitDamage = { [1] = 3, [2] = 3 } };
        p.GazeHits[1] = new() { new() { 8 }, new() { 6 } };
        p.GazeHits[2] = new() { new() { 8 }, new() { 6 } };
        var o = SimEngine.Run(s, p);
        // Both hits on one enemy: 6 + 10; one each: 8 + 8. Either way 16.
        Assert.Equal(16, o.TakenMax);
        Assert.Equal(16, o.TakenMin);
        Assert.Contains(o.Branches, b => b.Enemy(1)!.Hits[0] == 6 && b.Enemy(2)!.Hits[0] == 10);
        Assert.Contains(o.Branches, b => b.Enemy(1)!.Hits[0] == 8 && b.Enemy(2)!.Hits[0] == 8);
    }

    [Fact]
    public void AnEnemyNotHitKeepsItsAttack()
    {
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        var p = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, Hits = 3, HitDamage = { [1] = 3 } };
        p.GazeHits[1] = new() { new() { 8 }, new() { 6 }, new() { 4 } };
        Assert.Equal(4, SimEngine.Run(s, p).TakenMax); // one enemy: all three hits land on it
    }
}
