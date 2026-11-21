using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Which plays of a replayed card start with its target Impaled (decompile v0.113.1): Ki Hop draws only when its target is
/// Impaled as each play starts (KiHop.OnPlay: wasImpaled before the hit), and each of your powered hits counts your Impaled
/// down (ImpaledPower.AfterDamageReceived: at 0 an arrow comes loose, the counter starts again at 3, the power goes with the
/// last arrow).
/// </summary>
public class SimImpaledPlaysTests
{
    [Fact]
    public void TheFirstPlaysHitLoosensTheLastArrow()
        // Issue #331: Ki Hop+ replayed by Glam at an enemy with one arrow and 1 hit left: only the first play draws.
        => Assert.Equal(1, SimEngine.PlaysStartingImpaled(teammateArrow: false, yourArrows: 1, counter: 1, hitsPerPlay: 1, plays: 2));

    [Fact]
    public void HitsLeftOnTheCounterKeepItImpaled()
        => Assert.Equal(2, SimEngine.PlaysStartingImpaled(teammateArrow: false, yourArrows: 1, counter: 2, hitsPerPlay: 1, plays: 2));

    [Fact]
    public void ASecondArrowKeepsItImpaledAfterTheFirstComesLoose()
        // The first hit loosens one arrow (counter back to 3); the second arrow is still in.
        => Assert.Equal(2, SimEngine.PlaysStartingImpaled(teammateArrow: false, yourArrows: 2, counter: 1, hitsPerPlay: 1, plays: 2));

    [Fact]
    public void ATeammatesArrowKeepsItImpaled()
        => Assert.Equal(3, SimEngine.PlaysStartingImpaled(teammateArrow: true, yourArrows: 0, counter: 3, hitsPerPlay: 1, plays: 3));

    [Fact]
    public void NotImpaledMeansNoPlayDraws()
        => Assert.Equal(0, SimEngine.PlaysStartingImpaled(teammateArrow: false, yourArrows: 0, counter: 3, hitsPerPlay: 1, plays: 2));

    [Fact]
    public void SeveralHitsAPlayCountDownFaster()
        // Counter 3, three hits a play: the arrow comes loose on the first play's last hit.
        => Assert.Equal(1, SimEngine.PlaysStartingImpaled(teammateArrow: false, yourArrows: 1, counter: 3, hitsPerPlay: 3, plays: 2));
}
