using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Combat;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #130: the board probe is the same while nothing a prediction reads changes, and different when something does, on
/// the game's own objects (GameFight). It reads plain fields only: reading it twice allocates nothing new for the cards.
/// </summary>
[Collection(GameFightCollection.Name)]
public class BoardProbeTests
{
    [Fact]
    public void TheProbeChangesWithTheBoardAndOnlyThen()
    {
        var f = new GameFight();
        var strike = f.Add<StrikeIronclad>();
        f.Add<DefendIronclad>(PileType.Draw);
        int Probe() => BoardProbe.Compute(f.Me, f.Combat);

        var before = Probe();
        Assert.Equal(before, Probe());

        // A card drawn into your hand, a card upgraded, a settings change: each a different probe.
        var bash = f.Add<Bash>();
        var drawn = Probe();
        Assert.NotEqual(before, drawn);
        bash.UpgradeInternal();
        bash.FinalizeUpgradeInternal();
        var upgraded = Probe();
        Assert.NotEqual(drawn, upgraded);
        QolSettings.Revision++;
        Assert.NotEqual(upgraded, Probe());

        // A card's cost set for the turn (a local modifier) changes it too.
        var costed = Probe();
        strike.EnergyCost.SetThisTurn(0);
        Assert.NotEqual(costed, Probe());
    }

    [Fact]
    public void ReadingTheProbeAllocatesLittle()
    {
        var f = new GameFight();
        for (var i = 0; i < 8; i++) f.Add<StrikeIronclad>();
        BoardProbe.Compute(f.Me, f.Combat);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) BoardProbe.Compute(f.Me, f.Combat);
        var perCall = (GC.GetAllocatedBytesForCurrentThread() - start) / 100;
        Assert.True(perCall < 1024, $"{perCall} bytes per probe");
    }
}
