using SpireQoL.Telemetry;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The dev stream's steady watch (decision #177): on a frame where nothing the mod reads changed, every feature should allocate nothing,
/// and any that does is counted, with how often.
/// </summary>
public class SteadyWatchTests
{
    private const double Frame = 1 / 60.0;
    private static readonly string[] DevOnly = { "dev-dashboard", "dev-checks" };

    /// <summary>Runs frames of one key from <paramref name="start"/>, each with what <paramref name="kilobytes"/> gives for its index.</summary>
    private static double Run(SteadyWatch watch, int key, double start, int frames, Func<int, (string Id, double Kb)[]> kilobytes, bool devCheckFirst = false)
    {
        var now = start;
        for (var i = 0; i < frames; i++, now += Frame)
            watch.Frame(key, now, Frame, where: "fight", devCheck: devCheckFirst && i == 0, featureKb: kilobytes(i));
        return now;
    }

    [Fact]
    public void NothingCountsUntilTheKeyHasHeldForASecond()
    {
        var watch = new SteadyWatch(DevOnly);
        Run(watch, key: 1, start: 0, frames: 50, kilobytes: _ => new[] { ("card-tabs", 2.0) }); // under a second
        Assert.False(watch.Steady);
        var (sample, _) = watch.Take();
        Assert.Empty(sample.Garbage);
        Assert.Empty(sample.Steady);
    }

    [Fact]
    public void AFeatureAllocatingOnSteadyFramesIsCountedWithItsPeriod()
    {
        var watch = new SteadyWatch(DevOnly);
        // Two seconds of one key: the second one is steady. Every 15th frame the fade looks at the card.
        Run(watch, key: 1, start: 0, frames: 120, kilobytes: frame => frame % 15 == 0 ? new[] { ("ethereal-fade", 8.0), ("card-tabs", 0.0) } : new[] { ("card-tabs", 0.0) });
        Assert.True(watch.Steady);
        var (sample, firstSeen) = watch.Take();
        var garbage = Assert.Single(sample.Garbage);
        Assert.Equal("ethereal-fade", garbage.Id);
        Assert.Equal("fight", garbage.Where);
        Assert.Equal(15, garbage.Period);
        Assert.Equal(8.0, garbage.MaxKb);
        Assert.Equal(garbage.Frames * 8.0, garbage.Kb);
        Assert.Single(firstSeen);
        Assert.True(sample.Steady["fight"].Frames > 50);
    }

    [Fact]
    public void AKeyChangeStartsTheSettleAgain()
    {
        var watch = new SteadyWatch(DevOnly);
        var now = Run(watch, key: 1, start: 0, frames: 90, kilobytes: _ => Array.Empty<(string, double)>());
        Assert.True(watch.Steady);
        // The board changed: the work it sets off in the next second isn't garbage while steady.
        Run(watch, key: 2, start: now, frames: 50, kilobytes: _ => new[] { ("proc-indicators", 40.0) });
        Assert.False(watch.Steady);
        Assert.Empty(watch.Take().Sample.Garbage);
    }

    [Fact]
    public void DevModesOwnWorkIsLeftOut()
    {
        var watch = new SteadyWatch(DevOnly);
        Run(watch, key: 1, start: 0, frames: 120, kilobytes: _ => new[] { ("dev-dashboard", 3.0), ("dev-checks", 50.0) });
        Assert.Empty(watch.Take().Sample.Garbage);
    }

    [Fact]
    public void TheFramesJustAfterADevCheckArentSteady()
    {
        var watch = new SteadyWatch(DevOnly);
        var now = Run(watch, key: 1, start: 0, frames: 90, kilobytes: _ => Array.Empty<(string, double)>());
        // A check's search finishing a frame later, under its feature's name.
        Run(watch, key: 1, start: now, frames: 3, kilobytes: frame => frame == 1 ? new[] { ("proc-indicators", 12.0) } : Array.Empty<(string, double)>(), devCheckFirst: true);
        Assert.Empty(watch.Take().Sample.Garbage);
    }

    [Fact]
    public void EachIsFirstSeenOnceALaunch()
    {
        var watch = new SteadyWatch(DevOnly);
        var now = Run(watch, key: 1, start: 0, frames: 120, kilobytes: _ => new[] { ("card-tabs", 0.1) });
        Assert.Single(watch.Take().FirstSeen);
        Run(watch, key: 1, start: now, frames: 60, kilobytes: _ => new[] { ("card-tabs", 0.1) });
        var (sample, firstSeen) = watch.Take();
        Assert.Empty(firstSeen);
        Assert.Equal(1, Assert.Single(sample.Garbage).Period);
    }

    [Fact]
    public void ItAllocatesNothingPerFrameOnceAFeatureHasBeenSeen()
    {
        var watch = new SteadyWatch(DevOnly);
        var entries = new[] { ("card-tabs", 0.1), ("ethereal-fade", 0.0) };
        var now = Run(watch, key: 1, start: 0, frames: 120, kilobytes: _ => entries);
        // A garbage-making frame shows in every stretch; the runtime's own one-off work on this thread (a method recompiled hot,
        // mid-stretch) shows in one. So a stretch with nothing allocated, of three, passes.
        var allocated = new List<long>();
        for (var stretch = 0; stretch < 3 && !allocated.Contains(0); stretch++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++, now += Frame) watch.Frame(key: 1, now, Frame, where: "fight", devCheck: false, featureKb: entries);
            allocated.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Contains(0, allocated);
    }

    [Fact]
    public void TheLogLineSaysHowOftenAndHowMuch()
    {
        var line = SteadyWatch.Describe(new SteadyGarbage(Where: "fight", Id: "ethereal-fade", Frames: 4, Kb: 32, MaxKb: 8.5, Period: 15), steadyFrames: 60);
        Assert.Equal("[steady] ethereal-fade made garbage while nothing changed (fight): on 4 of 60 steady frames (15 apart most often), 8.00 KB each on average, 8.50 KB at most", line);
    }
}
