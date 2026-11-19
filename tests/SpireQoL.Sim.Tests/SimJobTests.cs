using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A search on a worker (decision #107): its result is the whole outcome, the same as searching it on the spot, whether it finished
/// before its background turn or after; cancelled, it never has a result.
/// </summary>
public class SimJobTests
{
    private static SimOrb Lightning(int passive) => new() { Kind = OrbKind.Lightning, Passive = passive, Evoke = 8 };

    /// <summary>Three enemies near death, five Lightning orbs and Juggernaut on block gains.</summary>
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
    public async Task ABackgroundJobEndsWithTheWholeOutcome()
    {
        var whole = SimExploration.Of(Heavy()).ExploreAll();
        // A first budget of one copy: it waits for a background turn at its second step, then finishes there.
        var job = SimJob.Start(SimExploration.Of(Heavy()), "test", firstBudget: 1);
        await job.Ready.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(job.Finished);
        Assert.False(job.Latest!.Truncated);
        Assert.Equal(Describe(whole), Describe(job.Latest));
    }
}
