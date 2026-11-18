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
    [InlineData("not checked (its simulation's outcomes not all explored): no tab: EnragePower fired", "its simulation's outcomes not all explored")]
    [InlineData("not checked (a branch of its simulation not modelled): damage to enemies: predicted 3, actual 6", "a branch of its simulation not modelled")]
    public void ReasonsKeepTheirShape(string line, string reason) => Assert.Equal(reason, ProcAudit.SkipReason(line));

    /// <summary>Decisions #87, #92: with outcomes not all explored, the issues about the simulator's tabs aren't reported; the others are.</summary>
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
