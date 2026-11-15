using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Thunder (decompile v0.111.0): ThunderPower.AfterOrbEvoked deals its amount (unpowered) to a
/// Lightning orb's evoke targets that are still alive. Passives don't set it off.
/// </summary>
public class SimThunderTests
{
    private static SimState State(params SimEnemy[] enemies)
    {
        var s = new SimState();
        s.Enemies.AddRange(enemies);
        s.P.OrbCapacity = 3;
        s.P.Orbs.Add(new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 });
        s.P.Thunder = 6;
        return s;
    }

    private static SimPlay Dualcast()
    {
        var p = new SimPlay { Name = "Dualcast" };
        p.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = 2 });
        return p;
    }

    [Fact]
    public void EachLightningEvokeHitsItsTargetAgain()
    {
        // Dualcast on one enemy: two evokes of 8, each followed by Thunder 6.
        var s = State(new SimEnemy { Id = 1, Name = "E1", Hp = 40, Hits = { 5 } });
        SimEngine.ApplyPlayTo(s, Dualcast(), out var b, out _);
        Assert.Equal(40 - 2 * (8 + 6), b[0].Enemy(1)!.Hp);
    }

    [Fact]
    public void AnEvokeThatKillsLeavesThunderNothingToHit()
    {
        // Auto-report: the evoke killed the Stabbot (6 HP), Thunder flashed with no living target;
        // a second enemy isn't hit by it.
        var s = State(new SimEnemy { Id = 1, Name = "Stabbot", Hp = 6, Hits = { 5 } }, new SimEnemy { Id = 2, Name = "E2", Hp = 30, Hits = { 5 } });
        var one = new SimPlay { Name = "Dualcast" };
        one.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = 1 });
        var o = SimEngine.Run(s, one);
        // Branch on the Stabbot: it dies, E2 untouched. Branch on E2: 8 + 6.
        Assert.Contains(o.Branches, x => !x.Enemy(1)!.Alive && x.Enemy(2)!.Hp == 30);
        Assert.Contains(o.Branches, x => x.Enemy(1)!.Alive && x.Enemy(2)!.Hp == 30 - 14);
    }

    [Fact]
    public void DualcastOnTwoEnemiesHitsEachEvokesTargetOnce()
    {
        // Auto-report (two Toadpoles at 28 and 48, Thunder 8, one Lightning 3/8): Dualcast's evokes went to
        // different Toadpoles, each followed by its Thunder: 28 → 12 and 48 → 32. The play alone covers
        // Thunder; no extra hit lands anywhere (32 damage in total in every branch).
        var s = State(new SimEnemy { Id = 0, Name = "Toadpole", Hp = 28, Hits = { 3, 3, 3 } }, new SimEnemy { Id = 1, Name = "Toadpole", Hp = 48 });
        s.P.Thunder = 8;
        SimEngine.ApplyPlayTo(s, Dualcast(), out var b, out _);
        Assert.Contains(b, x => x.Enemy(0)!.Hp == 12 && x.Enemy(1)!.Hp == 32);
        Assert.Contains(b, x => !x.Enemy(0)!.Alive && x.Enemy(1)!.Hp == 48);
        Assert.Contains(b, x => x.Enemy(0)!.Hp == 28 && x.Enemy(1)!.Hp == 16);
        Assert.Equal(3, b.Count);
    }

    [Fact]
    public void FrostEvokesAndNoThunderDontHit()
    {
        var s = State(new SimEnemy { Id = 1, Name = "E1", Hp = 40, Hits = { 5 } });
        s.P.Orbs[0] = new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 };
        SimEngine.ApplyPlayTo(s, Dualcast(), out var b, out _);
        Assert.Equal(40, b[0].Enemy(1)!.Hp);

        s = State(new SimEnemy { Id = 1, Name = "E1", Hp = 40, Hits = { 5 } });
        s.P.Thunder = 0;
        SimEngine.ApplyPlayTo(s, Dualcast(), out b, out _);
        Assert.Equal(40 - 16, b[0].Enemy(1)!.Hp);
    }
}
