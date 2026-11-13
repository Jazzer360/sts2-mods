using SpireQoL.Sim;
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
        var s = new SimState { Enemies = { E(1, 30, 48) } };
        SimEngine.ApplyPlayTo(s, Judgement(1, 30), out var branches, out _);
        Assert.All(branches, b => Assert.False(b.Enemy(1)!.Alive));
        Assert.Equal(0, SimEngine.Run(branches[0]).Worst.Taken); // it never attacks
    }

    [Fact]
    public void LeavesATargetAboveTheThreshold()
    {
        var s = new SimState { Enemies = { E(1, 31, 48) } };
        SimEngine.ApplyPlayTo(s, Judgement(1, 30), out var branches, out _);
        Assert.All(branches, b => Assert.Equal(31, b.Enemy(1)!.Hp));
    }

    [Fact]
    public void OnlyTheTargetDies()
    {
        var s = new SimState { Enemies = { E(1, 10, 5), E(2, 10, 5) } };
        SimEngine.ApplyPlayTo(s, Judgement(2, 30), out var branches, out _);
        Assert.All(branches, b => { Assert.True(b.Enemy(1)!.Alive); Assert.False(b.Enemy(2)!.Alive); });
    }
}
