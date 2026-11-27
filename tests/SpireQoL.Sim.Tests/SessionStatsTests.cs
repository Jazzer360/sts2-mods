using SpireQoL.Telemetry;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The session's counts (decision #108): how each turn is classified from the audits' events, what a summary holds, and that a
/// summary survives the file it's kept in until it's sent.
/// </summary>
public class SessionStatsTests
{
    private static readonly SessionInfo Info = new("1.2.3", "0.9", OtherMods: false);

    private static SessionStats Fight(params DevEvent[] events)
    {
        var sessionStats = new SessionStats();
        sessionStats.Post(new CombatStarted("Jaw Worm", Players: 1, "Ironclad"));
        foreach (var devEvent in events) sessionStats.Post(devEvent);
        return sessionStats;
    }

    [Fact]
    public void ATurnCheckedAndMatchedIsClean()
    {
        var sessionStats = Fight(new TurnStarted(Turn: 1), new CheckDone("play", Turn: 1, Matched: true, "Strike"), new CheckDone("forecast", Turn: 1, Matched: true, Detail: null), new TurnStarted(Turn: 2));
        var sessionSummary = sessionStats.Summary("menu", Info);
        Assert.Equal(2, sessionSummary.Turns);
        Assert.Equal(2, sessionSummary.Clean); // turn 2 is the fight's last so far: its plays decide (it has none)
        Assert.Equal((1, 1), (sessionSummary.Forecast.Checked, sessionSummary.Forecast.Matched));
        Assert.Equal((1, 1), (sessionSummary.Plays.Checked, sessionSummary.Plays.Matched));
    }

    /// <summary>
    /// The audits outside the turns (intent rolls, potions, reminders, rest) are counted on their own: a mismatch or a skip there is in
    /// the summary but never changes how a fight's turn is classified, and never mixes into the plays' counts.
    /// </summary>
    [Fact]
    public void AuditsOutsideTheTurnsAreCountedApart()
    {
        var sessionStats = Fight(new TurnStarted(Turn: 1), new CheckDone(Audits.Play, Turn: 1, Matched: true, "Strike"), new CheckDone(Audits.Forecast, Turn: 1, Matched: true, Detail: null),
            new CheckDone(Audits.Intent, Turn: 0, Matched: false, "JawWorm"), new CheckDone(Audits.Intent, Turn: 0, Matched: true, "JawWorm"),
            new CheckSkipped(Audits.Intent, Turn: 0, "the enemy's move rules couldn't be read", "Doormaker"),
            new CheckDone(Audits.Potion, Turn: 0, Matched: true, "FirePotion"), new TurnStarted(Turn: 2));
        var sessionSummary = sessionStats.Summary("menu", Info);
        Assert.Equal(2, sessionSummary.Clean);
        Assert.Equal((1, 1, 0), (sessionSummary.Plays.Checked, sessionSummary.Plays.Matched, sessionSummary.Plays.Skipped));
        Assert.Equal((2, 1, 1), (sessionSummary.Others[Audits.Intent].Checked, sessionSummary.Others[Audits.Intent].Matched, sessionSummary.Others[Audits.Intent].Skipped));
        Assert.Equal(1, sessionSummary.Others[Audits.Intent].Mismatches["JawWorm"]);
        Assert.Equal(1, sessionSummary.Others[Audits.Intent].SkipReasons["the enemy's move rules couldn't be read"]);
        Assert.Equal((1, 1), (sessionSummary.Others[Audits.Potion].Checked, sessionSummary.Others[Audits.Potion].Matched));
        var back = SessionSummary.FromJson(sessionSummary.ToJson())!;
        Assert.Equal(2, back.Others[Audits.Intent].Checked);
    }

    [Fact]
    public void TheWorstThingThatHappenedInATurnDecidesIt()
    {
        var sessionStats = Fight(
            new TurnStarted(Turn: 1), new CheckDone("play", Turn: 1, Matched: false, "proc"), new CheckDone("forecast", Turn: 1, Matched: true, Detail: null),
            new TurnStarted(Turn: 2), new CheckSkipped("play", Turn: 2, "simulation pending", "Bash"),
            new SearchDone("the turn forecast", SearchDone.ContextOf(combat: 1, turn: 2), Steps: 10, Copies: 1000, PeakHeld: 100, Ms: 5, Background: false, "the memory cap", Cancelled: false),
            new CheckDone("forecast", Turn: 2, Matched: true, Detail: null),
            new TurnStarted(Turn: 3), new CheckSkipped("forecast", Turn: 3, "an extra turn (no enemy turn to compare)", Detail: null),
            new TurnStarted(Turn: 4), new CheckDone("forecast", Turn: 4, Matched: true, Detail: null),
            new TurnStarted(Turn: 5));
        var sessionSummary = sessionStats.Summary("menu", Info);
        Assert.Equal(5, sessionSummary.Turns);
        Assert.Equal(1, sessionSummary.Mismatched); // turn 1
        Assert.Equal(1, sessionSummary.Capped);     // turn 2: the cap outranks its skip
        Assert.Equal(1, sessionSummary.Unchecked);  // turn 3
        Assert.Equal(2, sessionSummary.Clean);      // turns 4 and 5
        Assert.Equal(1, sessionSummary.Plays.Mismatches["proc"]);
        Assert.Equal(1, sessionSummary.Searches.Caps["the memory cap"]);
    }

    [Fact]
    public void ATurnWhoseEnemyTurnWasNeverCheckedIsAGapUnlessTheFightEndedThere()
    {
        // Turn 1's enemy turn came (turn 2 followed) but no check said anything: unchecked, with that as the reason.
        var sessionStats = Fight(new TurnStarted(Turn: 1), new TurnStarted(Turn: 2), new CheckDone("play", Turn: 2, Matched: true, "Strike"), new CombatEnded(),
            new CombatStarted("Cultist", Players: 1, "Ironclad"), new TurnStarted(Turn: 1));
        var sessionSummary = sessionStats.Summary("menu", Info);
        Assert.Equal(3, sessionSummary.Turns);
        Assert.Equal(1, sessionSummary.Unchecked);
        Assert.Equal(2, sessionSummary.Clean); // the first fight's last turn (it won the fight), and the second fight's turn so far
        Assert.Equal(1, sessionSummary.Forecast.SkipReasons["no check came for this turn's enemy turn"]);
        Assert.Equal(2, sessionSummary.Combats);
    }

    [Fact]
    public void AChecksThatComesAfterItsFightEndedCountsInItsLastTurn()
    {
        // The killing play's check comes a frame after the fight is over (no turn number to read then: 0).
        var sessionStats = Fight(new TurnStarted(Turn: 1), new CheckDone("forecast", Turn: 1, Matched: true, Detail: null), new TurnStarted(Turn: 2), new CombatEnded(),
            new CheckDone("play", Turn: 0, Matched: false, "damage"));
        var sessionSummary = sessionStats.Summary("won", Info);
        Assert.Equal(2, sessionSummary.Turns);
        Assert.Equal(1, sessionSummary.Mismatched);
        Assert.Equal(1, sessionSummary.Clean);
    }

    [Fact]
    public void AnExpectedSkipIsCountedButLeavesTheTurnClean()
    {
        // The play that won the fight: its state after it can never be checked (CheckSkipped.Expected).
        var sessionStats = Fight(new TurnStarted(Turn: 1), new CheckDone("forecast", Turn: 1, Matched: true, Detail: null), new TurnStarted(Turn: 2),
            new CheckSkipped("play", Turn: 2, "state after the play: the fight ended", "Strike", Expected: true), new CheckDone("play", Turn: 2, Matched: true, "Strike"),
            new CombatEnded());
        var sessionSummary = sessionStats.Summary("won", Info);
        Assert.Equal(2, sessionSummary.Clean);
        Assert.Equal(0, sessionSummary.Unchecked);
        Assert.Equal(1, sessionSummary.Plays.Skipped);
        Assert.Equal(1, sessionSummary.Plays.SkipReasons["state after the play: the fight ended (expected)"]);
        // Any other skip still makes the turn unchecked.
        sessionStats.Post(new CombatStarted("Cultist", Players: 1, "Ironclad"));
        sessionStats.Post(new TurnStarted(Turn: 1));
        sessionStats.Post(new CheckSkipped("play", Turn: 1, "state after the play: the fight ended", "Strike"));
        Assert.Equal(1, sessionStats.Summary("menu", Info).Unchecked);
    }

    [Fact]
    public void ASearchFromAnotherFightDoesntMarkThisOnesTurn()
    {
        var sessionStats = Fight(new TurnStarted(Turn: 1), new CheckDone("forecast", Turn: 1, Matched: true, Detail: null),
            new SearchDone("old", SearchDone.ContextOf(combat: 0, turn: 1), Steps: 1, Copies: 1, PeakHeld: 1, Ms: 1, Background: true, "the work cap", Cancelled: false), new TurnStarted(Turn: 2));
        var sessionSummary = sessionStats.Summary("menu", Info);
        Assert.Equal(0, sessionSummary.Capped);
        Assert.Equal(1, sessionSummary.Searches.Caps["the work cap"]); // still counted among the searches
    }

    [Fact]
    public void SearchesWaitsAndFramesAreSummed()
    {
        var sessionStats = Fight(
            new SearchDone("a", Context: 0, Steps: 5, Copies: 100, PeakHeld: 10, Ms: 2, Background: false, Cap: null, Cancelled: false),
            new SearchDone("b", Context: 0, Steps: 50, Copies: 30_000, PeakHeld: 900, Ms: 40, Background: true, Cap: null, Cancelled: false),
            new SearchDone("c", Context: 0, Steps: 3, Copies: 70, PeakHeld: 5, Ms: 1, Background: true, Cap: null, Cancelled: true),
            new FrameWait(Ms: 8, Missed: true), new FrameWait(Ms: 1, Missed: false), new Unsettled(Ms: 120), new ReportSent("proc", "abc"));
        foreach (var milliseconds in new[] { 1.0, 2, 3, 45 }) sessionStats.Frame(milliseconds, gapMs: 16);
        sessionStats.Frame(modMs: 1, gapMs: 120); // a stall of the game's own
        var sessionSummary = sessionStats.Summary("menu", Info);
        Assert.Equal(3, sessionSummary.Searches.Count);
        Assert.Equal(1, sessionSummary.Searches.Background);
        Assert.Equal(1, sessionSummary.Searches.Cancelled);
        Assert.Equal(70, sessionSummary.Searches.CancelledCopies);
        Assert.Equal(30_170, sessionSummary.Searches.Copies);
        Assert.Equal(900, sessionSummary.Searches.MaxHeld);
        Assert.Equal("b", sessionSummary.Searches.Slowest);
        Assert.Equal(40, sessionSummary.Searches.Ms.Max);
        Assert.Equal(2, sessionSummary.Waits.Ms.Count);
        Assert.Equal(1, sessionSummary.Waits.Missed);
        Assert.Equal(120, sessionSummary.Unsettled.Max);
        Assert.Equal(5, sessionSummary.Frames.ModMs.Count);
        Assert.Equal(1, sessionSummary.Frames.Slow);
        Assert.Equal(1, sessionSummary.Frames.GameStalls);
        Assert.Equal(1, sessionSummary.Reports["proc"]);
    }

    [Fact]
    public void FightFramesAreCountedAgainstTheBudgetOfAFrameAt165Fps()
    {
        // Decision #407: a fight frame where the mod took over 6 ms is one a player at 165 fps loses. Counted with the feature that took most of it
        // (the game's hooks aside: outside the frame's time), and the longest run of them in a row with the feature behind most of its frames.
        var sessionStats = new SessionStats();
        sessionStats.Frame(modMs: 20, gapMs: 16); // before any fight: not counted
        sessionStats.Post(new CombatStarted("Jaw Worm", Players: 1, "Ironclad"));
        var forecast = new[] { ("turn-forecast", 7.0), ("hook:AfterCardPlayed", 30.0) };
        var tabs = new[] { ("card-tabs", 8.0), ("turn-forecast", 1.0) };
        sessionStats.Frame(modMs: 7, gapMs: 15, forecast);
        sessionStats.Frame(modMs: 2, gapMs: 15, forecast); // under the budget: the run ends
        sessionStats.Frame(modMs: 9, gapMs: 15, tabs);
        sessionStats.Frame(modMs: 7, gapMs: 15, forecast);
        sessionStats.Frame(modMs: 10, gapMs: 15, tabs);
        sessionStats.Frame(modMs: 6, gapMs: 15, tabs); // at the budget: not over
        sessionStats.Post(new CombatEnded());
        sessionStats.Frame(modMs: 20, gapMs: 16); // after it: not counted
        var fights = sessionStats.Summary("menu", Info).FightFrames;
        Assert.Equal(6, fights.Frames);
        Assert.Equal(4, fights.OverBudget);
        Assert.Equal(3, fights.LongestRun);
        Assert.Equal("card-tabs", fights.LongestRunBy);
        Assert.Equal(2, fights.OverBudgetBy["card-tabs"]);
        Assert.Equal(2, fights.OverBudgetBy["turn-forecast"]);
        Assert.False(fights.OverBudgetBy.ContainsKey("hook:AfterCardPlayed"));
        Assert.Equal(67, fights.Fps); // 6 frames 15 ms apart
        // Kept through the file the summary waits in.
        var kept = SessionSummary.FromJson(sessionStats.Summary("menu", Info).ToJson())!.FightFrames;
        Assert.Equal((4, 3, "card-tabs"), (kept.OverBudget, kept.LongestRun, kept.LongestRunBy));
    }

    [Fact]
    public void TakingTheSummaryChangesNothing()
    {
        var sessionStats = Fight(new TurnStarted(Turn: 1), new CheckDone("forecast", Turn: 1, Matched: true, Detail: null), new TurnStarted(Turn: 2));
        var summary = sessionStats.Summary("unseen", Info).ToJson();
        var sessionSummary = sessionStats.Summary("unseen", Info);
        Assert.Equal(2, sessionSummary.Turns);
        Assert.Equal(SessionSummary.FromJson(summary)!.Clean, sessionSummary.Clean);
    }

    [Fact]
    public void ReasonListsKeepTheMostFrequent()
    {
        var sessionStats = Fight(new TurnStarted(Turn: 1));
        for (var i = 0; i < SessionStats.MaxReasons + 5; i++)
            for (var repeat = 0; repeat <= i; repeat++) sessionStats.Post(new CheckSkipped("play", Turn: 1, $"reason {i}", Detail: null));
        var sessionSummary = sessionStats.Summary("menu", Info);
        Assert.Equal(SessionStats.MaxReasons, sessionSummary.Plays.SkipReasons.Count);
        Assert.Contains($"reason {SessionStats.MaxReasons + 4}", sessionSummary.Plays.SkipReasons.Keys);
        Assert.DoesNotContain("reason 0", sessionSummary.Plays.SkipReasons.Keys);
    }

    [Fact]
    public void TheSummaryRoundTripsThroughItsFile()
    {
        var sessionStats = Fight(new TurnStarted(Turn: 1), new CheckSkipped("forecast", Turn: 1, "a branch the forecast didn't model", Detail: null));
        var json = sessionStats.Summary("unseen", Info).ToJson();
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
    public void PercentilesAreTheirBucketsUpperEdgeNeverAboveTheLargest(double[] values, double percentile, double expected)
    {
        var histogram = new Histogram();
        foreach (var value in values) histogram.Add(value);
        Assert.Equal(expected, histogram.Percentile(percentile), 6);
    }

    [Fact]
    public void EventKindsAreKebabCase()
    {
        Assert.Equal("search-done", new SearchDone("x", Context: 0, Steps: 0, Copies: 0, PeakHeld: 0, Ms: 0, Background: false, Cap: null, Cancelled: false).Kind);
        Assert.Equal("combat-ended", new CombatEnded().Kind);
    }

    [Fact]
    public async Task ASearchPostsWhatItDidWithTheTurnItStartedOn()
    {
        var seen = new List<SearchDone>();
        void Take(DevEvent devEvent) { if (devEvent is SearchDone searchDone && searchDone.Label == "telemetry test") lock (seen) seen.Add(searchDone); }
        DevEvents.Posted += Take;
        try
        {
            SimJob.Context = SearchDone.ContextOf(combat: 7, turn: 3);
            var state = new SimState();
            state.Enemies.Add(new SimEnemy { Id = 0, Name = "E", Hp = 20, Hits = new() { 5 } });
            var job = SimJob.Start(SimSearch.Of(state, play: null), "telemetry test");
            SimJob.Context = 0;
            await job.Ready.WaitAsync(TimeSpan.FromSeconds(10));
            SpinWait.SpinUntil(() => { lock (seen) return seen.Count > 0; }, 5_000);
            var searchDone = Assert.Single(seen);
            Assert.Equal(7, searchDone.Combat);
            Assert.Equal(3, searchDone.Turn);
            Assert.False(searchDone.Cancelled);
            Assert.Null(searchDone.Cap);
            Assert.True(searchDone.Steps > 0);
        }
        finally
        {
            DevEvents.Posted -= Take;
        }
    }
}
