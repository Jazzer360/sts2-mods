using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #92, rule 1: the play check counts every skipped check by reason in the combat summary. The reason keeps the shape of
/// the skip and drops what varies (card and action names), so the same gap adds up across plays.
/// </summary>
public class SkipReasonTests
{
    [Theory]
    [InlineData("state after the play: not checked (auto-played cards)", "state after the play: auto-played cards")]
    [InlineData("state after the play: not checked (too many branches)", "state after the play: too many branches")]
    [InlineData("not checked (BloodPotion ran during your choice): proc Juggernaut missing", "another action ran during your choice")]
    [InlineData("not checked: its tabs were computed before Strike finished (proc X)", "its tabs were computed before another play finished")]
    [InlineData("not checked (simulation pending): proc Y", "simulation pending")]
    [InlineData("not checked (aimed at a teammate; the simulator applies it to you): proc Z", "aimed at a teammate; the simulator applies it to you")]
    [InlineData("not checked (its simulation stopped at the memory cap): no tab: EnragePower fired", "its simulation stopped at the memory cap")]
    [InlineData("not checked (a branch of its simulation not modelled): damage to enemies: predicted 3, actual 6", "a branch of its simulation not modelled")]
    [InlineData("not checked (Unleash (a teammate's) ran during your choice): after play enemy HP: E predicted 42, actual 33", "another action ran during your choice")]
    [InlineData("budget: not checked (random auto-plays)", "budget: random auto-plays")]
    [InlineData("lethal: not checked (an enemy about to blow before the play): WaterfallGiant", "lethal: an enemy about to blow before the play")]
    public void ReasonsKeepTheirShape(string line, string reason) => Assert.Equal(reason, ProcAudit.SkipReason(line));

    /// <summary>
    /// Every excuse the play check makes goes through NotChecked, so it's counted (decision #92, rule 1) and grouped by its reason:
    /// the names and amounts in its detail never split the count.
    /// </summary>
    [Theory]
    [InlineData("no tab", "fired during random auto-plays", "Juggernaut", "no tab: fired during random auto-plays")]
    [InlineData("block", "random auto-plays", "predicted 4, actual 7", "block: random auto-plays")]
    [InlineData("unexplained draws", "after a random kill", "2 more than the card's own 1", "unexplained draws: after a random kill")]
    [InlineData("shuffle", "the fight ended", "didn't happen (draws: predicted 2 = own 2 + set off 0, actual 0)", "shuffle: the fight ended")]
    [InlineData("damage to enemies", "the fight ended: the game logs nothing from the killing hit on", "predicted 6-9, actual 3",
        "damage to enemies: the fight ended: the game logs nothing from the killing hit on")]
    public void NotCheckedLinesAreCountedByReason(string what, string why, string detail, string reason)
    {
        var line = ProcAudit.NotChecked(what, why, detail);
        Assert.Equal(reason, ProcAudit.SkipReason(line));
        Assert.Equal(reason, ProcAudit.SkipReason(ProcAudit.NotChecked(what, why, detail + " and more")));
    }

    /// <summary>
    /// Decision #108: only a skip no model could close is expected (the play ended the fight: the game stopped logging or cleaned up
    /// first). Everything else counts against the turn.
    /// </summary>
    [Theory]
    [InlineData("state after the play: not checked (the fight ended)", true)]
    [InlineData("block: not checked (the fight ended: the game logs nothing from the killing hit on): predicted 4, actual 0", true)]
    [InlineData("shuffle: not checked (the fight ended): didn't happen", true)]
    [InlineData("block: not checked (random auto-plays): predicted 4, actual 7", false)]
    [InlineData("state after the play: not checked (auto-played cards)", false)]
    [InlineData("lethal: not checked (the board after the play: its search stopped at a cap or met something not modelled)", false)]
    public void OnlyTheFightEndingIsExpected(string line, bool expected) => Assert.Equal(expected, ProcAudit.ExpectedSkip(ProcAudit.SkipReason(line)));

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
