using SpireQoL.Sim;
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
        => Assert.Equal(1, SimEngine.PlaysStartingImpaled(false, yourArrows: 1, counter: 1, hitsPerPlay: 1, plays: 2));

    [Fact]
    public void HitsLeftOnTheCounterKeepItImpaled()
        => Assert.Equal(2, SimEngine.PlaysStartingImpaled(false, 1, counter: 2, hitsPerPlay: 1, plays: 2));

    [Fact]
    public void ASecondArrowKeepsItImpaledAfterTheFirstComesLoose()
        // The first hit loosens one arrow (counter back to 3); the second arrow is still in.
        => Assert.Equal(2, SimEngine.PlaysStartingImpaled(false, yourArrows: 2, counter: 1, hitsPerPlay: 1, plays: 2));

    [Fact]
    public void ATeammatesArrowKeepsItImpaled()
        => Assert.Equal(3, SimEngine.PlaysStartingImpaled(true, 0, 3, 1, 3));

    [Fact]
    public void NotImpaledMeansNoPlayDraws()
        => Assert.Equal(0, SimEngine.PlaysStartingImpaled(false, 0, 3, 1, 2));

    [Fact]
    public void SeveralHitsAPlayCountDownFaster()
        // Counter 3, three hits a play: the arrow comes loose on the first play's last hit.
        => Assert.Equal(1, SimEngine.PlaysStartingImpaled(false, 1, 3, hitsPerPlay: 3, plays: 2));
}
