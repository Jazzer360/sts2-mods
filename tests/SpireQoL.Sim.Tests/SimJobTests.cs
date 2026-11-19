using System.Diagnostics;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A simulation explored on a worker (decision #87), publishing its snapshots on a clock (decision #104): whatever the publishing
/// does, the last snapshot is the whole outcome, the same as exploring it all at once.
/// </summary>
public class SimJobTests
{
    private static SimOrb Lightning(int passive) => new() { Kind = OrbKind.Lightning, Passive = passive, Evoke = 8 };

    /// <summary>Three enemies near death, five Lightning orbs and Juggernaut on block gains: needs slicing at a small slice size.</summary>
    private static SimState Heavy()
    {
        var s = new SimState();
        for (var i = 0; i < 3; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 5 + i * 2, Block = i, Hits = new() { 6, 4 } });
        s.P.Orbs = Enumerable.Range(0, 5).Select(i => Lightning(2 + i % 2)).ToList();
        s.P.OrbCapacity = 5;
        s.P.Juggernaut = 2;
        s.P.Plating = 3;
        return s;
    }

    private static string Describe(SimOutcome o)
        => string.Join("\n", o.Branches.Select(b => (b.Key(), b.Weight)).OrderBy(x => x.Item1).Select(x => $"{x.Item1}:{x.Weight:F9}"));

    [Fact]
    public void ABackgroundJobEndsWithTheWholeOutcome()
    {
        var whole = SimExploration.Of(Heavy(), null, 4).ExploreAll();
        // A first pass of one copy: everything after it is explored in the background, snapshots on the clock.
        var job = SimJob.Start(SimExploration.Of(Heavy(), null, 4), "test", firstBudget: 1);

        Assert.True(job.Ready.Wait(TimeSpan.FromSeconds(30)));
        Assert.True(job.Latest!.Truncated, "the first pass must leave work for the background");
        var clock = Stopwatch.StartNew();
        while (!job.Finished && clock.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(5);

        Assert.True(job.Finished);
        // The final snapshot is published as it finishes, not at the next tick of the clock.
        SimOutcome? latest = null;
        for (var i = 0; i < 100 && (latest = job.Latest) is { Truncated: true }; i++) Thread.Sleep(5);
        Assert.NotNull(latest);
        Assert.False(latest!.Truncated);
        Assert.Equal(Describe(whole), Describe(latest));
    }
}
