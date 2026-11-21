using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// One search per simulation (decision #107): every step advances every branch, merging identical ones, so it finishes with every
/// outcome or stops at the memory cap (SimWork). Stopped, the outcome is Truncated: the branches where it stopped, every share of
/// the rolls kept; what's settled in them (an enemy's death) still counts, everything else is unknown.
/// </summary>
public class SimCapTests
{
    private static SimOrb Lightning(int passive = 3) => new() { Kind = OrbKind.Lightning, Passive = passive, Evoke = 8 };

    /// <summary>Three enemies near death, five Lightning orbs and Juggernaut on block gains: hundreds of roll sequences.</summary>
    private static SimState Heavy()
    {
        var s = new SimState();
        for (var i = 0; i < 3; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 5 + i * 2, Block = i, Hits = new() { 6, 4 } });
        s.You.Orbs = Enumerable.Range(0, 5).Select(i => Lightning(2 + i % 2)).ToList();
        s.You.OrbCapacity = 5;
        s.You.Juggernaut = 2;
        s.You.Plating = 3;
        return s;
    }

    private static SimPlay Strike() => new() { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [0] = 4, [1] = 4, [2] = 4 }, BlockAmount = 3, BlockGains = 1 };

    private static SimOutcome Capped(SimState s, SimPlay? play, long cap)
        => SimEngine.Run(s, play == null ? null : SimScript.Of(play), new SimWork { OwnCap = cap });

    [Fact]
    public void AFightThatFitsFinishes()
    {
        var o = SimEngine.Run(Heavy(), Strike());
        Assert.False(o.Truncated);
        Assert.Equal(1.0, o.Branches.Sum(b => b.Weight), 9);
        Assert.True(o.PeakHeld >= o.Branches.Count);
    }

    [Fact]
    public void StoppedAtTheCapEveryShareOfTheRollsIsKept()
    {
        var whole = SimEngine.Run(Heavy());
        var o = Capped(Heavy(), null, 4);
        Assert.True(o.Truncated);
        Assert.True(whole.Branches.Count > 4, "the scenario must overflow the cap");
        Assert.Equal(1.0, o.Branches.Sum(b => b.Weight), 9);
    }

    [Fact]
    public void SettledDeathsStillCountAndTheExactChanceIsInTheRange()
    {
        // A death that happened stands (lasting): the capped chance is a range from the deaths already settled up to all of them,
        // and the exact chance is inside it.
        var exact = SimEngine.Run(Heavy());
        foreach (var cap in new[] { 3, 6, 12 })
        {
            var o = Capped(Heavy(), null, cap);
            Assert.True(o.Truncated);
            for (var id = 0; id < 3; id++)
            {
                var r = o.DeadChanceRange(id);
                var p = exact.DeadChance(id);
                Assert.InRange(p, r.Low - 1e-9, r.High + 1e-9);
                Assert.False(o.DeadInAll(id)); // no guarantee from a search that stopped
            }
        }
        // Somewhere before the end, some death is settled while the search is still going: the range's floor counts it.
        Assert.Contains(Enumerable.Range(2, 400), cap => SimEngine.Run(Heavy(), null, new SimWork { CopyCap = cap }) is { Truncated: true } o && Enumerable.Range(0, 3).Any(id => o.DeadChanceRange(id).Low > 0));
    }

    [Fact]
    public void StoppedBeforeTheEnemyTurnYourDeathIsUnknown()
    {
        var s = Heavy();
        s.You.Hp = 3;
        var c = Capped(s, null, 4).YouDieChance();
        Assert.Equal(0, c.Low);
        Assert.Equal(1, c.High);
    }

    [Fact]
    public void StoppedAfterThePlayItsOwnResultsAreExact()
    {
        // The play is over in every branch before the end of turn: its kills and log are measured there, cap or no cap.
        var whole = SimEngine.Run(Heavy(), Strike());
        var o = Capped(Heavy(), Strike(), 4);
        Assert.True(o.Truncated);
        Assert.Equal(whole.KilledByPlayInAll.OrderBy(x => x), o.KilledByPlayInAll.OrderBy(x => x));
        Assert.Equal(whole.KilledByPlayInAny.OrderBy(x => x), o.KilledByPlayInAny.OrderBy(x => x));
        Assert.Equal(whole.PlayBlockGainsMax, o.PlayBlockGainsMax);
        Assert.Equal(whole.PlayFiredEvery.Items.ToArray(), o.PlayFiredEvery.Items.ToArray());
    }

    [Fact]
    public void TheWorkCapStopsASearchPartWayThroughAStep()
    {
        var o = SimEngine.Run(Heavy(), null, new SimWork { CopyCap = 10 });
        Assert.True(o.Truncated);
        Assert.Equal("the work cap", o.CapHit);
        Assert.Equal(1.0, o.Branches.Sum(b => b.Weight), 9);
        // Its branches are nobody's now: copying them later never meets the cap.
        Assert.All(o.Branches, b => b.Clone());
    }

    [Fact]
    public void ACancelledSearchStopsAtItsNextStep()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => SimEngine.Run(Heavy(), null, new SimWork { Cancel = cts.Token }));
    }

    [Fact]
    public void TheSearchFreesWhatItHeld()
    {
        // The shared count goes back down when a search is over, capped or not (other tests may be running: only this one's part).
        var work = new SimWork();
        SimEngine.Run(Heavy(), null, work);
        Assert.True(work.PeakHeld > 0);
        Assert.Equal(0, work.Held);
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
