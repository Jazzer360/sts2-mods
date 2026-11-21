using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #92, rule 1: the play check counts every skipped check by reason in the combat summary. A skip is typed (CheckSkip):
/// its reason is what it's counted under, its detail (card and action names, amounts) only in its log line.
/// </summary>
public class SkipReasonTests
{
    /// <summary>
    /// Every excuse the play check makes is a CheckSkip (ModelLines.Skip), so it's counted (decision #92, rule 1) and grouped by its
    /// reason: the names and amounts in its detail never split the count.
    /// </summary>
    [Theory]
    [InlineData("no tab", "fired during random auto-plays", "Juggernaut", "no tab: fired during random auto-plays", "no tab: not checked (fired during random auto-plays): Juggernaut")]
    [InlineData("block", "random auto-plays", "predicted 4, actual 7", "block: random auto-plays", "block: not checked (random auto-plays): predicted 4, actual 7")]
    [InlineData("state after the play", "auto-played cards", null, "state after the play: auto-played cards", "state after the play: not checked (auto-played cards)")]
    [InlineData("", "another action ran during your choice", "BloodPotion: proc Juggernaut missing", "another action ran during your choice",
        "not checked (another action ran during your choice): BloodPotion: proc Juggernaut missing")]
    [InlineData("damage to enemies", "the fight ended: the game logs nothing from the killing hit on", "predicted 6-9, actual 3",
        "damage to enemies: the fight ended: the game logs nothing from the killing hit on",
        "damage to enemies: not checked (the fight ended: the game logs nothing from the killing hit on): predicted 6-9, actual 3")]
    public void SkipsAreCountedByReasonAndLoggedWithTheirDetail(string what, string why, string? detail, string reason, string line)
    {
        var skip = new CheckSkip(what, why, detail);
        Assert.Equal(reason, skip.Reason);
        Assert.Equal(line, skip.Line);
        Assert.Equal(reason, (skip with { Detail = detail + " and more" }).Reason);
    }

    [Fact]
    public void ModelLinesKeepEachSkipAndItsLine()
    {
        var lines = new ModelLines();
        lines.Add("a note");
        lines.Skip("budget", "random auto-plays");
        Assert.Equal(new[] { "a note", "budget: not checked (random auto-plays)" }, lines);
        Assert.Equal("budget: random auto-plays", Assert.Single(lines.Skips).Reason);
    }

    /// <summary>
    /// Decision #108: only a skip no model could close is expected (the play ended the fight: the game stopped logging or cleaned up
    /// first; or it ended your turn). Everything else counts against the turn.
    /// </summary>
    [Theory]
    [InlineData("state after the play", CheckSkip.FightEnded, true)]
    [InlineData("block", CheckSkip.FightEnded + ": the game logs nothing from the killing hit on", true)]
    [InlineData("state after the play", CheckSkip.TurnEnded, true)]
    [InlineData("block", "random auto-plays", false)]
    [InlineData("lethal", "the board after the play: its search stopped at a cap or met something not modelled", false)]
    public void OnlyTheFightOrTurnEndingIsExpected(string what, string why, bool expected) => Assert.Equal(expected, new CheckSkip(what, why).Expected);

    /// <summary>Decisions #92, #107: when a card's search stopped at a cap, the issues about the simulator's tabs aren't reported; the others are.</summary>
    [Theory]
    [InlineData("no tab: EnragePower fired", true)]
    [InlineData("didn't fire: Kunai", true)]
    [InlineData("block: predicted 4, actual 0", true)]
    [InlineData("damage to enemies: predicted 3, actual 6", true)]
    [InlineData("damage to you: predicted 0, actual 2", true)]
    [InlineData("after play enemy HP: Cultist predicted 10, actual 4", false)]
    [InlineData("draws: predicted 2, actual 1", false)]
    [InlineData("lethal: E1 predicted to die before acting in every outcome of this play, but after it: 50%", true)]
    public void TabIssuesDependOnTheSimulation(string issue, bool fromSim) => Assert.Equal(fromSim, ProcAudit.SimDependent(issue));
}
