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
        var state = new SimState { Enemies = { E(id: 1, hp: 30, hits: 48) } };
        SimEngine.ApplyPlayTo(state, Judgement(target: 1, threshold: 30), out var branches, out _);
        Assert.All(branches, branch => Assert.False(branch.Enemy(id: 1)!.Alive));
        Assert.Equal(0, SimEngine.Run(branches[0]).Worst.Taken); // it never attacks
    }

    [Fact]
    public void LeavesATargetAboveTheThreshold()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 31, hits: 48) } };
        SimEngine.ApplyPlayTo(state, Judgement(target: 1, threshold: 30), out var branches, out _);
        Assert.All(branches, branch => Assert.Equal(31, branch.Enemy(id: 1)!.Hp));
    }

    [Fact]
    public void OnlyTheTargetDies()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 10, hits: 5), E(id: 2, hp: 10, hits: 5) } };
        SimEngine.ApplyPlayTo(state, Judgement(target: 2, threshold: 30), out var branches, out _);
        Assert.All(branches, branch => { Assert.True(branch.Enemy(id: 1)!.Alive); Assert.False(branch.Enemy(id: 2)!.Alive); });
    }

    [Fact]
    public void ItsKillIsWhatTheLethalTabReads()
    {
        // The lethal tab: the target dead before it acts in every branch, and not without the card.
        var state = new SimState { Enemies = { E(id: 1, hp: 21, hits: 48) } };
        Assert.False(SimEngine.Run(state).DeadInAll(enemyId: 1));
        var outcome = SimEngine.Run(state, Judgement(target: 1, threshold: 30));
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.True(outcome.PlayKillsInAll);
        Assert.False(SimEngine.Run(state, Judgement(target: 1, threshold: 20)).DeadInAll(enemyId: 1));
    }

    /// <summary>
    /// Timber: a hit of 20 on its target, then, when it killed (decision #190: its attack's results), a second attack of 30 on every enemy.
    /// </summary>
    private static SimPlay Timber(int target, int enemies)
    {
        Dictionary<int, decimal> Each(decimal damage) => Enumerable.Range(1, enemies).ToDictionary(id => id, _ => damage);
        return new SimPlay
        {
            Name = "Timber", TargetId = target, IsAttack = true,
            Body =
            {
                new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = Each(damage: 20m), Fixed = true }),
                new WhenAction(new AttackKilledCondition(), new SimAction[]
                    { new AttackAction(Hits: 1, SimPlay.Target.All, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = Each(damage: 30m), Fixed = true }) }),
            },
        };
    }

    [Fact]
    public void TimbersKillHitsEveryEnemy()
    {
        // Timber: its hit kills the target, then a second attack hits every enemy (30 each).
        var state = new SimState { Enemies = { E(id: 1, hp: 8, hits: 5), E(id: 2, hp: 36, hits: 5), E(id: 3, hp: 13, hits: 5) } };
        var timber = Timber(target: 1, enemies: 3);
        SimEngine.ApplyPlayTo(state, timber, out var branches, out _);
        Assert.All(branches, branch =>
        {
            Assert.False(branch.Enemy(id: 1)!.Alive);
            Assert.Equal(6, branch.Enemy(id: 2)!.Hp);
            Assert.False(branch.Enemy(id: 3)!.Alive);
        });
    }

    [Fact]
    public void TimberWithoutAKillHitsOnlyItsTarget()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 50, hits: 5), E(id: 2, hp: 36, hits: 5) } };
        var timber = Timber(target: 1, enemies: 2);
        SimEngine.ApplyPlayTo(state, timber, out var branches, out _);
        Assert.All(branches, branch => { Assert.Equal(30, branch.Enemy(id: 1)!.Hp); Assert.Equal(36, branch.Enemy(id: 2)!.Hp); });
    }

    [Fact]
    public void CaptureSpiritsDamageIgnoresBlockAndSpendsSlippery()
    {
        // Capture Spirit: 3 unblockable damage to its target. Slippery takes it down to 1 HP and loses a charge, so the
        // crossed Slippery tab shows.
        var state = new SimState { Enemies = { E(id: 1, hp: 20, hits: 5) } };
        state.Enemies[0].Block = 10;
        state.Enemies[0].Slippery = 2;
        var play = new SimPlay { Name = "CaptureSpirit", TargetId = 1, TargetDamage = { [1] = 3 } };
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        Assert.All(branches, branch =>
        {
            Assert.Equal(10, branch.Enemy(id: 1)!.Block);
            Assert.Equal(19, branch.Enemy(id: 1)!.Hp);
            Assert.Equal(1, branch.Enemy(id: 1)!.Slippery);
        });
        Assert.True(SimEngine.Run(state, play).ActedInAny(PowerId.Slippery.Name, owner: 1));
    }

    [Fact]
    public void CaptureSpiritCanKill()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 3, hits: 20) } };
        Assert.True(SimEngine.Run(state, new SimPlay { Name = "CaptureSpirit", TargetId = 1, TargetDamage = { [1] = 3 } }).DeadInAll(enemyId: 1));
    }
}
