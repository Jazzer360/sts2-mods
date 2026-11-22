using SpireQoL.Telemetry;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>The dev stream's allocation sampler (decision #176): the runtime's allocation ticks, by type, thread and quiet or not.</summary>
public class AllocationSamplerTests
{
    private sealed class SampledThing
    {
        public long Value;
    }

    [Fact]
    public void WhatThisThreadAllocatesIsSampledByType()
    {
        using var sampler = new AllocationSampler(AllocationSampler.CurrentOsThread());
        sampler.Cycle(seconds: 0); // the start of a burst: listening
        Assert.True(sampler.Sampling);
        sampler.SetQuiet(true);
        var kept = new List<SampledThing>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // The runtime reports a tick about every 100 KB, a little late: allocate until one names the type (a few MB at most).
        string? found = null;
        while (found == null && clock.ElapsedMilliseconds < 10_000)
        {
            for (var i = 0; i < 100_000; i++) kept.Add(new SampledThing { Value = i });
            kept.Clear();
            Thread.Sleep(50); // on this thread throughout: the ticks are told by thread
            found = sampler.Take().Quiet.Keys.FirstOrDefault(key => key.EndsWith(nameof(SampledThing), StringComparison.Ordinal));
        }
        Assert.NotNull(found);
        if (AllocationSampler.CurrentOsThread() != 0) Assert.StartsWith("game|", found);
    }

    [Fact]
    public void ItListensInBursts()
    {
        using var sampler = new AllocationSampler(gameThread: 0);
        sampler.Cycle(seconds: 1);
        Assert.True(sampler.Sampling);
        sampler.Cycle(seconds: AllocationSampler.OnSeconds + 1);
        Assert.False(sampler.Sampling);
        sampler.Cycle(seconds: AllocationSampler.PeriodSeconds + 0.5);
        Assert.True(sampler.Sampling);
    }

    private sealed class Finalized
    {
        public static int Done;
        // A finalizer the runtime reports on (FinalizeObject): what floods the listener with events.
        ~Finalized() => Interlocked.Increment(ref Done);
    }

    [Fact]
    public void ItsOwnGarbageIsLeftOut()
    {
        // Each burst's events (one per object finalized among them) make garbage on the thread that delivers them: never in the sample,
        // over several bursts (each a new session, on its own thread).
        // Threads are told apart by the operating system's number for them, which the sampler reads on Windows only (where the game and dev
        // mode run): elsewhere (the Linux build machine) there's nothing to leave out by.
        if (AllocationSampler.CurrentOsThread() == 0) return;
        using var sampler = new AllocationSampler(AllocationSampler.CurrentOsThread());
        var seen = new HashSet<string>();
        for (var burst = 0; burst < 3; burst++)
        {
            sampler.Cycle(seconds: burst * AllocationSampler.PeriodSeconds);
            for (var round = 0; round < 5; round++)
            {
                for (var i = 0; i < 20_000; i++) _ = new Finalized();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Thread.Sleep(300);
            var (quiet, active) = sampler.Take();
            foreach (var key in quiet.Keys.Concat(active.Keys)) seen.Add(key);
            sampler.Cycle(seconds: burst * AllocationSampler.PeriodSeconds + AllocationSampler.OnSeconds + 1);
            Thread.Sleep(100);
        }
        Assert.DoesNotContain(seen, key => key.Contains("EventWrittenEventArgs", StringComparison.Ordinal) || key.EndsWith("MoreEventInfo", StringComparison.Ordinal));
    }
}
