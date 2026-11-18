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
    public void ReasonsKeepTheirShape(string line, string reason) => Assert.Equal(reason, ProcAudit.SkipReason(line));
}
