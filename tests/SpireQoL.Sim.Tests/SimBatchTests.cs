using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card's simulations together (SimBatch): complete the moment every search is, as the frame's wait sees it. A Task.WhenAll of the
/// jobs completed a moment later on a thread-pool continuation, so a card read right after the wait could count as still exploring
/// (the "tabs shown: simulation still exploring" skips on searches of 0.1 ms, found in the dev dashboard).
/// </summary>
public class SimBatchTests
{
    private static SimState Board()
    {
        var s = new SimState();
        s.Enemies.Add(new SimEnemy { Id = 0, Name = "E", Hp = 20, Hits = new() { 5 } });
        s.Enemies.Add(new SimEnemy { Id = 1, Name = "F", Hp = 12, Hits = new() { 3 } });
        return s;
    }

    private static SimPlay Strike(int target) => new() { HitTarget = SimPlay.Target.Single, TargetId = target, Hits = 1, HitDamage = { [0] = 6, [1] = 6 } };

    [Fact]
    public void CompleteTheMomentEveryJobIs()
    {
        // The frame's wait (SimWait.All) wakes the moment the jobs are done; the batch must agree right then, every time. Polled in a
        // tight loop on this thread so it wakes as fast (SpinWait.SpinUntil backs off into sleeps, long enough for any WhenAll to catch up).
        for (var i = 0; i < 200; i++)
        {
            var batch = new SimBatch(new[] { 0, 1 }.Select(t => SimJob.Start(SimSearch.Of(Board(), Strike(t)), "batch test")).ToArray());
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!batch.Jobs.All(j => j.Ready.IsCompleted)) Assert.True(clock.ElapsedMilliseconds < 10_000, "the searches never finished");
            Assert.True(batch.IsCompleted, $"pending right after its jobs were done (run {i})");
            Assert.Null(batch.Fault);
            Assert.All(batch.Latest, o => Assert.NotNull(o));
        }
    }

    [Fact]
    public void NotCompleteWhileAJobIsRunning()
    {
        var job = SimJob.Start(SimSearch.Of(Board(), Strike(0)), "batch test cancelled");
        job.Cancel(); // a cancelled job never completes (nobody reads it)
        var batch = new SimBatch(new[] { job });
        Assert.False(batch.IsCompleted);
    }
}
