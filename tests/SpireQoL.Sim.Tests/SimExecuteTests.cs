using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Judgement (v0.113.0): if the target is alive at or below its Threshold, CreatureCmd.SetCurrentHp sets it to 0.
/// </summary>
public class SimExecuteTests
{
    private static SimEnemy E(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimPlay Judgement(int target, int threshold) => new() { Name = "Judgement", TargetId = target, ExecuteAtOrBelow = threshold };

    [Fact]
    public void KillsTheTargetAtOrBelowTheThreshold()
    {
        var state = new SimState { Enemies = { E(1, 30, 48) } };
        SimEngine.ApplyPlayTo(state, Judgement(1, 30), out var branches, out _);
        Assert.All(branches, branch => Assert.False(branch.Enemy(1)!.Alive));
        Assert.Equal(0, SimEngine.Run(branches[0]).Worst.Taken); // it never attacks
    }

    [Fact]
    public void LeavesATargetAboveTheThreshold()
    {
        var state = new SimState { Enemies = { E(1, 31, 48) } };
        SimEngine.ApplyPlayTo(state, Judgement(1, 30), out var branches, out _);
        Assert.All(branches, branch => Assert.Equal(31, branch.Enemy(1)!.Hp));
    }

    [Fact]
    public void OnlyTheTargetDies()
    {
        var state = new SimState { Enemies = { E(1, 10, 5), E(2, 10, 5) } };
        SimEngine.ApplyPlayTo(state, Judgement(2, 30), out var branches, out _);
        Assert.All(branches, branch => { Assert.True(branch.Enemy(1)!.Alive); Assert.False(branch.Enemy(2)!.Alive); });
    }

    [Fact]
    public void ItsKillIsWhatTheLethalTabReads()
    {
        // The lethal tab: the target dead before it acts in every branch, and not without the card.
        var state = new SimState { Enemies = { E(1, 21, 48) } };
        Assert.False(SimEngine.Run(state).DeadInAll(1));
        var outcome = SimEngine.Run(state, Judgement(1, 30));
        Assert.True(outcome.DeadInAll(1));
        Assert.True(outcome.PlayKillsInAll);
        Assert.False(SimEngine.Run(state, Judgement(1, 20)).DeadInAll(1));
    }

    [Fact]
    public void TimbersKillHitsEveryEnemy()
    {
        // Timber: its hit kills the target, then a second attack hits every enemy (30 each).
        var state = new SimState { Enemies = { E(1, 8, 5), E(2, 36, 5), E(3, 13, 5) } };
        var timber = new SimPlay { Name = "Timber", HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1,
            HitDamage = { [1] = 20, [2] = 20, [3] = 20 }, OnKillHitAll = { [1] = 30, [2] = 30, [3] = 30 } };
        SimEngine.ApplyPlayTo(state, timber, out var branches, out _);
        Assert.All(branches, branch =>
        {
            Assert.False(branch.Enemy(1)!.Alive);
            Assert.Equal(6, branch.Enemy(2)!.Hp);
            Assert.False(branch.Enemy(3)!.Alive);
        });
    }

    [Fact]
    public void TimberWithoutAKillHitsOnlyItsTarget()
    {
        var state = new SimState { Enemies = { E(1, 50, 5), E(2, 36, 5) } };
        var timber = new SimPlay { Name = "Timber", HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1,
            HitDamage = { [1] = 20, [2] = 20 }, OnKillHitAll = { [1] = 30, [2] = 30 } };
        SimEngine.ApplyPlayTo(state, timber, out var branches, out _);
        Assert.All(branches, branch => { Assert.Equal(30, branch.Enemy(1)!.Hp); Assert.Equal(36, branch.Enemy(2)!.Hp); });
    }

    [Fact]
    public void CaptureSpiritsDamageIgnoresBlockAndSpendsSlippery()
    {
        // Capture Spirit: 3 unblockable damage to its target. Slippery takes it down to 1 HP and loses a charge, so the
        // crossed Slippery tab shows.
        var state = new SimState { Enemies = { E(1, 20, 5) } };
        state.Enemies[0].Block = 10;
        state.Enemies[0].Slippery = 2;
        var play = new SimPlay { Name = "CaptureSpirit", TargetId = 1, TargetDamage = { [1] = 3 } };
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        Assert.All(branches, branch =>
        {
            Assert.Equal(10, branch.Enemy(1)!.Block);
            Assert.Equal(19, branch.Enemy(1)!.Hp);
            Assert.Equal(1, branch.Enemy(1)!.Slippery);
        });
        Assert.True(SimEngine.Run(state, play).SlipperyInAny(1));
    }

    [Fact]
    public void CaptureSpiritCanKill()
    {
        var state = new SimState { Enemies = { E(1, 3, 20) } };
        Assert.True(SimEngine.Run(state, new SimPlay { Name = "CaptureSpirit", TargetId = 1, TargetDamage = { [1] = 3 } }).DeadInAll(1));
    }
}
