using SpireQoL.Telemetry;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The session's counts (decision #108): how each turn is classified from the audits' events, what a summary holds, and that a
/// summary survives the file it's kept in until it's sent.
/// </summary>
public class SessionStatsTests
{
    private static readonly SessionInfo Info = new("1.2.3", "0.9", false);

    private static SessionStats Fight(params DevEvent[] events)
    {
        var s = new SessionStats();
        s.Post(new CombatStarted("Jaw Worm", 1, "Ironclad"));
        foreach (var e in events) s.Post(e);
        return s;
    }

    [Fact]
    public void ATurnCheckedAndMatchedIsClean()
    {
        var s = Fight(new TurnStarted(1), new CheckDone("play", 1, true, "Strike"), new CheckDone("forecast", 1, true, null), new TurnStarted(2));
        var o = s.Summary("menu", Info);
        Assert.Equal(2, o.Turns);
        Assert.Equal(2, o.Clean); // turn 2 is the fight's last so far: its plays decide (it has none)
        Assert.Equal((1, 1), (o.Forecast.Checked, o.Forecast.Matched));
        Assert.Equal((1, 1), (o.Plays.Checked, o.Plays.Matched));
    }

    /// <summary>
    /// The audits outside the turns (intent rolls, potions, reminders, rest) are counted on their own: a mismatch or a skip there is in
    /// the summary but never changes how a fight's turn is classified, and never mixes into the plays' counts.
    /// </summary>
    [Fact]
    public void AuditsOutsideTheTurnsAreCountedApart()
    {
        var s = Fight(new TurnStarted(1), new CheckDone(Audits.Play, 1, true, "Strike"), new CheckDone(Audits.Forecast, 1, true, null),
            new CheckDone(Audits.Intent, 0, false, "JawWorm"), new CheckDone(Audits.Intent, 0, true, "JawWorm"),
            new CheckSkipped(Audits.Intent, 0, "the enemy's move rules couldn't be read", "Doormaker"),
            new CheckDone(Audits.Potion, 0, true, "FirePotion"), new TurnStarted(2));
        var o = s.Summary("menu", Info);
        Assert.Equal(2, o.Clean);
        Assert.Equal((1, 1, 0), (o.Plays.Checked, o.Plays.Matched, o.Plays.Skipped));
        Assert.Equal((2, 1, 1), (o.Others[Audits.Intent].Checked, o.Others[Audits.Intent].Matched, o.Others[Audits.Intent].Skipped));
        Assert.Equal(1, o.Others[Audits.Intent].Mismatches["JawWorm"]);
        Assert.Equal(1, o.Others[Audits.Intent].SkipReasons["the enemy's move rules couldn't be read"]);
        Assert.Equal((1, 1), (o.Others[Audits.Potion].Checked, o.Others[Audits.Potion].Matched));
        var back = SessionSummary.FromJson(o.ToJson())!;
        Assert.Equal(2, back.Others[Audits.Intent].Checked);
    }

    [Fact]
    public void TheWorstThingThatHappenedInATurnDecidesIt()
    {
        var s = Fight(
            new TurnStarted(1), new CheckDone("play", 1, false, "proc"), new CheckDone("forecast", 1, true, null),
            new TurnStarted(2), new CheckSkipped("play", 2, "simulation pending", "Bash"),
            new SearchDone("the turn forecast", SearchDone.ContextOf(1, 2), 10, 1000, 100, 5, false, "the memory cap", false),
            new CheckDone("forecast", 2, true, null),
            new TurnStarted(3), new CheckSkipped("forecast", 3, "an extra turn (no enemy turn to compare)", null),
            new TurnStarted(4), new CheckDone("forecast", 4, true, null),
            new TurnStarted(5));
        var o = s.Summary("menu", Info);
        Assert.Equal(5, o.Turns);
        Assert.Equal(1, o.Mismatched); // turn 1
        Assert.Equal(1, o.Capped);     // turn 2: the cap outranks its skip
        Assert.Equal(1, o.Unchecked);  // turn 3
        Assert.Equal(2, o.Clean);      // turns 4 and 5
        Assert.Equal(1, o.Plays.Mismatches["proc"]);
        Assert.Equal(1, o.Searches.Caps["the memory cap"]);
    }

    [Fact]
    public void ATurnWhoseEnemyTurnWasNeverCheckedIsAGapUnlessTheFightEndedThere()
    {
        // Turn 1's enemy turn came (turn 2 followed) but no check said anything: unchecked, with that as the reason.
        var s = Fight(new TurnStarted(1), new TurnStarted(2), new CheckDone("play", 2, true, "Strike"), new CombatEnded(),
            new CombatStarted("Cultist", 1, "Ironclad"), new TurnStarted(1));
        var o = s.Summary("menu", Info);
        Assert.Equal(3, o.Turns);
        Assert.Equal(1, o.Unchecked);
        Assert.Equal(2, o.Clean); // the first fight's last turn (it won the fight), and the second fight's turn so far
        Assert.Equal(1, o.Forecast.SkipReasons["no check came for this turn's enemy turn"]);
        Assert.Equal(2, o.Combats);
    }

    [Fact]
    public void AChecksThatComesAfterItsFightEndedCountsInItsLastTurn()
    {
        // The killing play's check comes a frame after the fight is over (no turn number to read then: 0).
        var s = Fight(new TurnStarted(1), new CheckDone("forecast", 1, true, null), new TurnStarted(2), new CombatEnded(),
            new CheckDone("play", 0, false, "damage"));
        var o = s.Summary("won", Info);
        Assert.Equal(2, o.Turns);
        Assert.Equal(1, o.Mismatched);
        Assert.Equal(1, o.Clean);
    }

    [Fact]
    public void AnExpectedSkipIsCountedButLeavesTheTurnClean()
    {
        // The play that won the fight: its state after it can never be checked (CheckSkipped.Expected).
        var s = Fight(new TurnStarted(1), new CheckDone("forecast", 1, true, null), new TurnStarted(2),
            new CheckSkipped("play", 2, "state after the play: the fight ended", "Strike", Expected: true), new CheckDone("play", 2, true, "Strike"),
            new CombatEnded());
        var o = s.Summary("won", Info);
        Assert.Equal(2, o.Clean);
        Assert.Equal(0, o.Unchecked);
        Assert.Equal(1, o.Plays.Skipped);
        Assert.Equal(1, o.Plays.SkipReasons["state after the play: the fight ended (expected)"]);
        // Any other skip still makes the turn unchecked.
        s.Post(new CombatStarted("Cultist", 1, "Ironclad"));
        s.Post(new TurnStarted(1));
        s.Post(new CheckSkipped("play", 1, "state after the play: the fight ended", "Strike"));
        Assert.Equal(1, s.Summary("menu", Info).Unchecked);
    }

    [Fact]
    public void ASearchFromAnotherFightDoesntMarkThisOnesTurn()
    {
        var s = Fight(new TurnStarted(1), new CheckDone("forecast", 1, true, null),
            new SearchDone("old", SearchDone.ContextOf(0, 1), 1, 1, 1, 1, true, "the work cap", false), new TurnStarted(2));
        var o = s.Summary("menu", Info);
        Assert.Equal(0, o.Capped);
        Assert.Equal(1, o.Searches.Caps["the work cap"]); // still counted among the searches
    }

    [Fact]
    public void SearchesWaitsAndFramesAreSummed()
    {
        var s = Fight(
            new SearchDone("a", 0, 5, 100, 10, 2, false, null, false),
            new SearchDone("b", 0, 50, 30_000, 900, 40, true, null, false),
            new SearchDone("c", 0, 3, 70, 5, 1, true, null, true),
            new FrameWait(8, true), new FrameWait(1, false), new Unsettled(120), new ReportSent("proc", "abc"));
        foreach (var ms in new[] { 1.0, 2, 3, 45 }) s.Frame(ms, 16);
        s.Frame(1, 120); // a stall of the game's own
        var o = s.Summary("menu", Info);
        Assert.Equal(3, o.Searches.Count);
        Assert.Equal(1, o.Searches.Background);
        Assert.Equal(1, o.Searches.Cancelled);
        Assert.Equal(70, o.Searches.CancelledCopies);
        Assert.Equal(30_170, o.Searches.Copies);
        Assert.Equal(900, o.Searches.MaxHeld);
        Assert.Equal("b", o.Searches.Slowest);
        Assert.Equal(40, o.Searches.Ms.Max);
        Assert.Equal(2, o.Waits.Ms.Count);
        Assert.Equal(1, o.Waits.Missed);
        Assert.Equal(120, o.Unsettled.Max);
        Assert.Equal(5, o.Frames.ModMs.Count);
        Assert.Equal(1, o.Frames.Slow);
        Assert.Equal(1, o.Frames.GameStalls);
        Assert.Equal(1, o.Reports["proc"]);
    }

    [Fact]
    public void TakingTheSummaryChangesNothing()
    {
        var s = Fight(new TurnStarted(1), new CheckDone("forecast", 1, true, null), new TurnStarted(2));
        var a = s.Summary("unseen", Info).ToJson();
        var b = s.Summary("unseen", Info);
        Assert.Equal(2, b.Turns);
        Assert.Equal(SessionSummary.FromJson(a)!.Clean, b.Clean);
    }

    [Fact]
    public void ReasonListsKeepTheMostFrequent()
    {
        var s = Fight(new TurnStarted(1));
        for (var i = 0; i < SessionStats.MaxReasons + 5; i++)
            for (var n = 0; n <= i; n++) s.Post(new CheckSkipped("play", 1, $"reason {i}", null));
        var o = s.Summary("menu", Info);
        Assert.Equal(SessionStats.MaxReasons, o.Plays.SkipReasons.Count);
        Assert.Contains($"reason {SessionStats.MaxReasons + 4}", o.Plays.SkipReasons.Keys);
        Assert.DoesNotContain("reason 0", o.Plays.SkipReasons.Keys);
    }

    [Fact]
    public void TheSummaryRoundTripsThroughItsFile()
    {
        var s = Fight(new TurnStarted(1), new CheckSkipped("forecast", 1, "a branch the forecast didn't model", null));
        var json = s.Summary("unseen", Info).ToJson();
        Assert.Contains("\"v\":1", json);
        Assert.Contains("\"skipReasons\":{\"a branch the forecast didn't model\":1}", json);
        var back = SessionSummary.FromJson(json)!;
        Assert.Equal(SessionSummary.Format, back.V);
        Assert.Equal("1.2.3", back.Mod);
        Assert.Equal(1, back.Unchecked);
        Assert.Null(SessionSummary.FromJson("{not json"));
    }

    [Theory]
    [InlineData(new[] { 1.0, 2, 3, 4, 100 }, 0.5, 3.1)]
    [InlineData(new[] { 0.05 }, 0.95, 0.05)]
    [InlineData(new[] { 25.0, 25.5 }, 1.0, 25.5)]
    [InlineData(new double[0], 0.95, 0)]
    public void PercentilesAreTheirBucketsUpperEdgeNeverAboveTheLargest(double[] values, double p, double expected)
    {
        var h = new Histogram();
        foreach (var v in values) h.Add(v);
        Assert.Equal(expected, h.Percentile(p), 6);
    }

    [Fact]
    public void EventKindsAreKebabCase()
    {
        Assert.Equal("search-done", new SearchDone("x", 0, 0, 0, 0, 0, false, null, false).Kind);
        Assert.Equal("combat-ended", new CombatEnded().Kind);
    }

    [Fact]
    public async Task ASearchPostsWhatItDidWithTheTurnItStartedOn()
    {
        var seen = new List<SearchDone>();
        void Take(DevEvent e) { if (e is SearchDone d && d.Label == "telemetry test") lock (seen) seen.Add(d); }
        DevEvents.Posted += Take;
        try
        {
            SimJob.Context = SearchDone.ContextOf(7, 3);
            var s = new SimState();
            s.Enemies.Add(new SimEnemy { Id = 0, Name = "E", Hp = 20, Hits = new() { 5 } });
            var job = SimJob.Start(SimSearch.Of(s, null), "telemetry test");
            SimJob.Context = 0;
            await job.Ready.WaitAsync(TimeSpan.FromSeconds(10));
            SpinWait.SpinUntil(() => { lock (seen) return seen.Count > 0; }, 5_000);
            var d = Assert.Single(seen);
            Assert.Equal(7, d.Combat);
            Assert.Equal(3, d.Turn);
            Assert.False(d.Cancelled);
            Assert.Null(d.Cap);
            Assert.True(d.Steps > 0);
        }
        finally
        {
            DevEvents.Posted -= Take;
        }
    }
}
