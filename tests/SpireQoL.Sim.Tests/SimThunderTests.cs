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
        var state = new SimState();
        state.Enemies.AddRange(enemies);
        state.You.OrbCapacity = 3;
        state.You.Orbs.Add(new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 });
        state.You.Thunder = 6;
        return state;
    }

    private static SimPlay Dualcast()
    {
        var play = new SimPlay { Name = "Dualcast" };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = 2 });
        return play;
    }

    [Fact]
    public void EachLightningEvokeHitsItsTargetAgain()
    {
        // Dualcast on one enemy: two evokes of 8, each followed by Thunder 6.
        var state = State(new SimEnemy { Id = 1, Name = "E1", Hp = 40, Hits = { 5 } });
        SimEngine.ApplyPlayTo(state, Dualcast(), out var states, out _);
        Assert.Equal(40 - 2 * (8 + 6), states[0].Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void AnEvokeThatKillsLeavesThunderNothingToHit()
    {
        // Auto-report: the evoke killed the Stabbot (6 HP), Thunder flashed with no living target;
        // a second enemy isn't hit by it.
        var state = State(new SimEnemy { Id = 1, Name = "Stabbot", Hp = 6, Hits = { 5 } }, new SimEnemy { Id = 2, Name = "E2", Hp = 30, Hits = { 5 } });
        var one = new SimPlay { Name = "Dualcast" };
        one.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = 1 });
        var outcome = SimEngine.Run(state, one);
        // Branch on the Stabbot: it dies, E2 untouched. Branch on E2: 8 + 6.
        Assert.Contains(outcome.Branches, branch => !branch.Enemy(id: 1)!.Alive && branch.Enemy(id: 2)!.Hp == 30);
        Assert.Contains(outcome.Branches, branch => branch.Enemy(id: 1)!.Alive && branch.Enemy(id: 2)!.Hp == 30 - 14);
    }

    [Fact]
    public void DualcastOnTwoEnemiesHitsEachEvokesTargetOnce()
    {
        // Auto-report (two Toadpoles at 28 and 48, Thunder 8, one Lightning 3/8): Dualcast's evokes went to
        // different Toadpoles, each followed by its Thunder: 28 → 12 and 48 → 32. The play alone covers
        // Thunder; no extra hit lands anywhere (32 damage in total in every branch).
        var state = State(new SimEnemy { Id = 0, Name = "Toadpole", Hp = 28, Hits = { 3, 3, 3 } }, new SimEnemy { Id = 1, Name = "Toadpole", Hp = 48 });
        state.You.Thunder = 8;
        SimEngine.ApplyPlayTo(state, Dualcast(), out var states, out _);
        Assert.Contains(states, branch => branch.Enemy(id: 0)!.Hp == 12 && branch.Enemy(id: 1)!.Hp == 32);
        Assert.Contains(states, branch => !branch.Enemy(id: 0)!.Alive && branch.Enemy(id: 1)!.Hp == 48);
        Assert.Contains(states, branch => branch.Enemy(id: 0)!.Hp == 28 && branch.Enemy(id: 1)!.Hp == 16);
        Assert.Equal(3, states.Count);
    }

    [Fact]
    public void FrostEvokesAndNoThunderDontHit()
    {
        var state = State(new SimEnemy { Id = 1, Name = "E1", Hp = 40, Hits = { 5 } });
        state.You.Orbs[0] = new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 };
        SimEngine.ApplyPlayTo(state, Dualcast(), out var states, out _);
        Assert.Equal(40, states[0].Enemy(id: 1)!.Hp);

        state = State(new SimEnemy { Id = 1, Name = "E1", Hp = 40, Hits = { 5 } });
        state.You.Thunder = 0;
        SimEngine.ApplyPlayTo(state, Dualcast(), out states, out _);
        Assert.Equal(40 - 16, states[0].Enemy(id: 1)!.Hp);
    }
}
