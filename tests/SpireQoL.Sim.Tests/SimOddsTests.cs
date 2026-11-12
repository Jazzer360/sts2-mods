using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Chances from the simulator's branches: each random target is equally likely (the game picks with
/// Rng.CombatTargets.NextItem over the hittable enemies), so a branch's weight is its share of the
/// rolls, and identical branches merged together add their weights.
/// </summary>
public class SimOddsTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimState WithLightning(params SimEnemy[] enemies)
    {
        var s = new SimState();
        s.Enemies.AddRange(enemies);
        s.P.OrbCapacity = 3;
        s.P.Orbs.Add(new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 });
        return s;
    }

    private static SimPlay Evoke(int times)
    {
        var p = new SimPlay { Name = "Dualcast" };
        p.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = times });
        return p;
    }

    [Fact]
    public void ARandomHitOnTwoEnemiesKillsEachHalfTheTime()
    {
        var o = SimEngine.Run(WithLightning(Enemy(1, 8, 5), Enemy(2, 8, 5)), Evoke(1));
        Assert.Equal(0.5, o.DeadChance(1), 6);
        Assert.Equal(0.5, o.DeadChance(2), 6);
        Assert.Equal(1.0, o.Branches.Sum(b => b.Weight), 6);
    }

    [Fact]
    public void MergedBranchesKeepTheirWeight()
    {
        // Two evokes on two 16-HP enemies: four rolls. Both on E1 (1/4) kills it; both on E2 (1/4)
        // kills E2; one each (2/4, the two orders merge into one branch) kills neither.
        var o = SimEngine.Run(WithLightning(Enemy(1, 16, 5), Enemy(2, 16, 5)), Evoke(2));
        Assert.Equal(3, o.Branches.Count);
        Assert.Equal(0.25, o.DeadChance(1), 6);
        Assert.Equal(0.25, o.DeadChance(2), 6);
        Assert.Equal(1.0, o.Branches.Sum(b => b.Weight), 6);
    }

    [Fact]
    public void CertainAndImpossibleDeaths()
    {
        var o = SimEngine.Run(WithLightning(Enemy(1, 8, 5)), Evoke(1));
        Assert.Equal(1.0, o.DeadChance(1), 6);
        Assert.True(o.DeadInAll(1));
        o = SimEngine.Run(WithLightning(Enemy(1, 30, 5)), Evoke(1));
        Assert.Equal(0.0, o.DeadChance(1), 6);
    }

    [Fact]
    public void RandomPoisonSplitsTheSameWay()
    {
        // Bouncing Flask style: 3 poison on a random enemy, twice; the enemy at 3 HP dies to its tick
        // at the start of its turn when at least one lands on it (3 of 4 rolls).
        var s = new SimState { Enemies = { Enemy(1, 3, 5), Enemy(2, 50, 5) } };
        var p = new SimPlay { Name = "BouncingFlask", RandomPoison = 3, RandomPoisonTimes = 2 };
        var o = SimEngine.Run(s, p);
        Assert.Equal(0.75, o.DeadChance(1), 6);
    }
}
