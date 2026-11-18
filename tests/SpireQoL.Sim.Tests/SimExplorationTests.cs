using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Exploring in slices (decision #87): a step past the slice size keeps one slice per run, and the exploration runs every
/// combination of slices. Together they must give exactly what one run with no branch limit gives: the same branches (after
/// merging), the same shares of the rolls, and the same play results. A slice size bigger than any step never slices, so it's
/// that unlimited run.
/// </summary>
public class SimExplorationTests
{
    private static SimOrb Lightning(int passive = 3) => new() { Kind = OrbKind.Lightning, Passive = passive, Evoke = 8 };

    /// <summary>Three enemies near death, five Lightning orbs and Juggernaut on block gains: hundreds of roll sequences.</summary>
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

    private static SimPlay Strike() => new() { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [0] = 4, [1] = 4, [2] = 4 }, BlockAmount = 3, BlockGains = 1 };

    private static string Describe(SimOutcome o)
        => string.Join("\n", o.Branches.Select(b => (b.Key(), b.Weight)).OrderBy(x => x.Item1).Select(x => $"{x.Item1}:{x.Weight:F9}"));

    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(true, 1)]
    [InlineData(false, 40)]
    public void SlicesAddUpToOneUnlimitedRun(bool withPlay, int slice)
    {
        var play = withPlay ? Strike() : null;
        var unlimited = SimExploration.Of(Heavy(), play, int.MaxValue).ExploreAll();
        var ex = SimExploration.Of(Heavy(), play, slice);
        var sliced = ex.ExploreAll();

        Assert.False(unlimited.Truncated);
        Assert.False(sliced.Truncated);
        Assert.True(unlimited.Branches.Count > slice, "the scenario must overflow the slice size");
        Assert.True(ex.Runs > 1);
        Assert.Equal(1.0, sliced.Branches.Sum(b => b.Weight), 9);
        Assert.Equal(Describe(unlimited), Describe(sliced));
        for (var id = 0; id < 3; id++)
        {
            Assert.Equal(unlimited.DeadChance(id), sliced.DeadChance(id), 9);
            Assert.Equal(unlimited.DeadInAll(id), sliced.DeadInAll(id));
        }
        Assert.Equal(unlimited.TakenMax, sliced.TakenMax);
        Assert.Equal(unlimited.TakenMin, sliced.TakenMin);
        Assert.Equal(unlimited.KilledByPlayInAll.OrderBy(x => x), sliced.KilledByPlayInAll.OrderBy(x => x));
        Assert.Equal(unlimited.KilledByPlayInAny.OrderBy(x => x), sliced.KilledByPlayInAny.OrderBy(x => x));
        Assert.Equal(unlimited.PlayKillsInEveryBranch, sliced.PlayKillsInEveryBranch);
        Assert.Equal(unlimited.PlayBlockGainsMax, sliced.PlayBlockGainsMax);
    }

    [Fact]
    public void ASmallBudgetGoesOnWhereItStopped()
    {
        var all = SimExploration.Of(Heavy(), Strike(), 8).ExploreAll();
        var ex = SimExploration.Of(Heavy(), Strike(), 8);
        var calls = 0;
        while (!ex.Explore(1)) calls++; // one run per call
        Assert.True(calls > 1);
        Assert.Equal(Describe(all), Describe(ex.Snapshot()));
    }

    [Fact]
    public void APartialSnapshotIsTruncatedWithTheSafeBound()
    {
        var ex = SimExploration.Of(Heavy(), null, 8);
        ex.Explore(1);
        Assert.False(ex.Finished);
        var partial = ex.Snapshot();
        Assert.True(partial.Truncated);
        Assert.NotNull(partial.SafeWorst);
        Assert.True(ex.ExploredWeight is > 0 and < 1);
        // Never less than the whole: the safe run bounds damage to you.
        Assert.True(partial.TakenMax >= ex.ExploreAll().TakenMax);
    }

    [Fact]
    public void APublishedSnapshotNeverChanges()
    {
        var ex = SimExploration.Of(Heavy(), Strike(), 8);
        ex.Explore(1);
        var early = ex.Snapshot();
        var before = Describe(early);
        ex.ExploreAll();
        Assert.Equal(before, Describe(early));
    }

    [Fact]
    public void AFightThatFitsIsOneRun()
    {
        var s = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 10, Hits = new() { 5 } } } };
        var ex = SimExploration.Of(s);
        Assert.Equal(SimEngine.Run(s).TakenMax, ex.ExploreAll().TakenMax);
        Assert.Equal(1, ex.Runs);
        Assert.True(ex.Done);
    }

    [Fact]
    public void TheCursorWalksEveryCombination()
    {
        // Overflows met in order with 3 and then 2 slices: (0,0) (0,1) (1,0) (1,1) (2,0) (2,1).
        var seen = new List<string>();
        List<int>? path = new();
        while (path != null)
        {
            var c = new SliceCursor(path, 1);
            var a = c.Take(Enumerable.Range(0, 3).Select(_ => new SimState()).ToList());
            var b = c.Take(Enumerable.Range(0, 2).Select(_ => new SimState()).ToList());
            Assert.Single(a);
            Assert.Single(b);
            seen.Add(string.Join(",", Enumerable.Range(0, 2).Select(d => d < path.Count ? path[d] : 0)));
            path = c.Next();
        }
        Assert.Equal(new[] { "0,0", "0,1", "1,0", "1,1", "2,0", "2,1" }, seen);
    }

    [Fact]
    public void ChancesNarrowToTheExactOne()
    {
        // While exploring, each chance is a range that holds the exact one; complete, it's exact (decision #87, stage 2).
        var exact = SimExploration.Of(Heavy(), null, int.MaxValue).ExploreAll();
        var ex = SimExploration.Of(Heavy(), null, 8);
        ex.Explore(1);
        var partial = ex.Snapshot();
        for (var id = 0; id < 3; id++)
        {
            var p = exact.DeadChance(id);
            var r = partial.DeadChanceRange(id);
            Assert.False(r.Exact);
            Assert.InRange(p, r.Low - 1e-9, r.High + 1e-9);
            var done = ex.ExploreAll().DeadChanceRange(id);
            Assert.True(done.Exact);
            Assert.Equal(p, done.Low, 9);
            ex = SimExploration.Of(Heavy(), null, 8);
            ex.Explore(1);
        }
    }

    [Fact]
    public void YouCantDieInWhatsUnexploredWhenTheSafeRunLives()
    {
        // The safe run (random hits on nobody) bounds damage to you: when it doesn't kill you, nothing unexplored does.
        var ex = SimExploration.Of(Heavy(), null, 8);
        ex.Explore(1);
        var partial = ex.Snapshot();
        Assert.False(partial.SafeWorst!.Died);
        Assert.Equal(0, partial.YouDieChance().High);
    }

    [Fact]
    public void YouMightDieWhileUnexplored()
    {
        // Low HP: the safe run kills you, so the unexplored outcomes might: the skull shows with a range.
        var s = Heavy();
        s.P.Hp = 3;
        var ex = SimExploration.Of(s, null, 8);
        ex.Explore(1);
        var c = ex.Snapshot().YouDieChance();
        Assert.True(c.Possible);
        Assert.True(c.High > c.Low);
    }

    [Theory]
    [InlineData(0.4, 0.4, "40%", "40%")]
    [InlineData(0.4, 0.55, "40–55%", "40%?")]
    [InlineData(0.004, 0.004, "<1%", "<1%")]
    [InlineData(0.996, 0.996, "99%", "99%")]
    public void ChanceText(double low, double high, string text, string shortText)
    {
        var c = new SimChance(low, high, true);
        Assert.Equal(text, c.Text());
        Assert.Equal(shortText, c.ShortText());
    }
}
