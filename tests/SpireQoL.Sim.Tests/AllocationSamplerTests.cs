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
}
