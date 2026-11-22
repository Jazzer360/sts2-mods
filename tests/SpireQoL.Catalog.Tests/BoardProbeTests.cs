using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
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
        var gameFight = new GameFight();
        var strike = gameFight.Add<StrikeIronclad>();
        gameFight.Add<DefendIronclad>(PileType.Draw);
        int Probe() => BoardProbe.Compute(gameFight.Me, gameFight.Combat);

        var before = Probe();
        Assert.Equal(before, Probe());

        // A card drawn into your hand, a card upgraded, a settings change: each a different probe.
        var bash = gameFight.Add<Bash>();
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
    public void APowersShownCountChangesTheProbe()
    {
        // Withering Presence counts your cards down in its CardsLeft variable, its Amount unchanged (a report: its tab stayed on
        // every card after the count started over, the probe not seeing it).
        var gameFight = new GameFight();
        var power = ModelDb.Power<WitheringPresencePower>().ToMutable();
        power.ApplyInternal(gameFight.Me.Creature, amount: 1);
        int Probe() => BoardProbe.Compute(gameFight.Me, gameFight.Combat);
        var before = Probe();
        power.DynamicVars["CardsLeft"].BaseValue = 3;
        Assert.NotEqual(before, Probe());
    }

    [Fact]
    public void APowersReadFlagChangesTheProbe()
    {
        // A dev review: Skittish's block this turn (its internal data, Amount unchanged) changed the turn forecast's start state while
        // the probe stayed the same. The probe hashes what the simulator reads from each power (SimReaders.Read).
        var gameFight = new GameFight();
        var skittish = (PowerModel)ModelDb.Power<SkittishPower>().ToMutable();
        skittish.ApplyInternal(gameFight.Me.Creature, amount: 5);
        int Probe() => BoardProbe.Compute(gameFight.Me, gameFight.Combat);
        var before = Probe();
        AccessTools.PropertySetter(typeof(SkittishPower), nameof(SkittishPower.HasGainedBlockThisTurn)).Invoke(skittish, new object[] { true });
        Assert.NotEqual(before, Probe());
    }

    [Fact]
    public void ReadingTheProbeAllocatesLittle()
    {
        var gameFight = new GameFight();
        for (var i = 0; i < 8; i++) gameFight.Add<StrikeIronclad>();
        BoardProbe.Compute(gameFight.Me, gameFight.Combat);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) BoardProbe.Compute(gameFight.Me, gameFight.Combat);
        var perCall = (GC.GetAllocatedBytesForCurrentThread() - start) / 100;
        Assert.True(perCall < 1024, $"{perCall} bytes per probe");
    }
}
