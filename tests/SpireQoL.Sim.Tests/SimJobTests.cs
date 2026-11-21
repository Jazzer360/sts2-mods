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
        var state = new SimState();
        for (var i = 0; i < 3; i++) state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 5 + i * 2, Block = i, Hits = new() { 6, 4 } });
        state.You.Orbs = Enumerable.Range(0, 5).Select(i => Lightning(2 + i % 2)).ToList();
        state.You.OrbCapacity = 5;
        state.You.Juggernaut = 2;
        state.You.Plating = 3;
        return state;
    }

    private static string Describe(SimOutcome outcome)
        => string.Join("\n", outcome.Branches.Select(branch => (branch.Key(), branch.Weight)).OrderBy(weight => weight.Item1).Select(weight => $"{weight.Item1}:{weight.Weight:F9}"));

    [Fact]
    public async Task ABackgroundJobEndsWithTheWholeOutcome()
    {
        var whole = SimSearch.Of(Heavy()).SearchAll();
        // A first budget of one copy: it waits for a background turn at its second step, then finishes there.
        var job = SimJob.Start(SimSearch.Of(Heavy()), "test", firstBudget: 1);
        await job.Ready.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(job.Finished);
        Assert.False(job.Latest!.Truncated);
        Assert.Equal(Describe(whole), Describe(job.Latest));
    }
}
