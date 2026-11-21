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
        var state = new SimState();
        for (var i = 0; i < 3; i++) state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 5 + i * 2, Block = i, Hits = new() { 6, 4 } });
        state.You.Orbs = Enumerable.Range(0, 5).Select(i => Lightning(2 + i % 2)).ToList();
        state.You.OrbCapacity = 5;
        state.You.Juggernaut = 2;
        state.You.Plating = 3;
        return state;
    }

    private static SimPlay Strike() => new() { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [0] = 4, [1] = 4, [2] = 4 }, BlockAmount = 3, BlockGains = 1 };

    private static SimOutcome Capped(SimState state, SimPlay? play, long cap)
        => SimEngine.Run(state, play == null ? null : SimScript.Of(play), new SimWork { OwnCap = cap });

    [Fact]
    public void AFightThatFitsFinishes()
    {
        var outcome = SimEngine.Run(Heavy(), Strike());
        Assert.False(outcome.Truncated);
        Assert.Equal(1.0, outcome.Branches.Sum(branch => branch.Weight), 9);
        Assert.True(outcome.PeakHeld >= outcome.Branches.Count);
    }

    [Fact]
    public void StoppedAtTheCapEveryShareOfTheRollsIsKept()
    {
        var whole = SimEngine.Run(Heavy());
        var outcome = Capped(Heavy(), null, 4);
        Assert.True(outcome.Truncated);
        Assert.True(whole.Branches.Count > 4, "the scenario must overflow the cap");
        Assert.Equal(1.0, outcome.Branches.Sum(branch => branch.Weight), 9);
    }

    [Fact]
    public void SettledDeathsStillCountAndTheExactChanceIsInTheRange()
    {
        // A death that happened stands (lasting): the capped chance is a range from the deaths already settled up to all of them,
        // and the exact chance is inside it.
        var exact = SimEngine.Run(Heavy());
        foreach (var cap in new[] { 3, 6, 12 })
        {
            var outcome = Capped(Heavy(), null, cap);
            Assert.True(outcome.Truncated);
            for (var id = 0; id < 3; id++)
            {
                var chance = outcome.DeadChanceRange(id);
                var p = exact.DeadChance(id);
                Assert.InRange(p, chance.Low - 1e-9, chance.High + 1e-9);
                Assert.False(outcome.DeadInAll(id)); // no guarantee from a search that stopped
            }
        }
        // Somewhere before the end, some death is settled while the search is still going: the range's floor counts it.
        Assert.Contains(Enumerable.Range(2, 400), cap => SimEngine.Run(Heavy(), null, new SimWork { CopyCap = cap }) is { Truncated: true } outcome && Enumerable.Range(0, 3).Any(id => outcome.DeadChanceRange(id).Low > 0));
    }

    [Fact]
    public void StoppedBeforeTheEnemyTurnYourDeathIsUnknown()
    {
        var state = Heavy();
        state.You.Hp = 3;
        var chance = Capped(state, null, 4).YouDieChance();
        Assert.Equal(0, chance.Low);
        Assert.Equal(1, chance.High);
    }

    [Fact]
    public void StoppedAfterThePlayItsOwnResultsAreExact()
    {
        // The play is over in every branch before the end of turn: its kills and log are measured there, cap or no cap.
        var whole = SimEngine.Run(Heavy(), Strike());
        var outcome = Capped(Heavy(), Strike(), 4);
        Assert.True(outcome.Truncated);
        Assert.Equal(whole.KilledByPlayInAll.OrderBy(x => x), outcome.KilledByPlayInAll.OrderBy(x => x));
        Assert.Equal(whole.KilledByPlayInAny.OrderBy(x => x), outcome.KilledByPlayInAny.OrderBy(x => x));
        Assert.Equal(whole.PlayBlockGainsMax, outcome.PlayBlockGainsMax);
        Assert.Equal(whole.PlayFiredEvery.Items.ToArray(), outcome.PlayFiredEvery.Items.ToArray());
    }

    [Fact]
    public void TheWorkCapStopsASearchPartWayThroughAStep()
    {
        var outcome = SimEngine.Run(Heavy(), null, new SimWork { CopyCap = 10 });
        Assert.True(outcome.Truncated);
        Assert.Equal("the work cap", outcome.CapHit);
        Assert.Equal(1.0, outcome.Branches.Sum(branch => branch.Weight), 9);
        // Its branches are nobody's now: copying them later never meets the cap.
        Assert.All(outcome.Branches, branch => branch.Clone());
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
        var chance = new SimChance(low, high, true);
        Assert.Equal(text, chance.Text());
        Assert.Equal(shortText, chance.ShortText());
    }
}
