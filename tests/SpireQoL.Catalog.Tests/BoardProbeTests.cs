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
    public void ReadingTheProbeMakesNothing()
    {
        // The probe runs every frame of a fight (the first feature to ask it pays: Keyword Tags). A dev review: each power it read made a closure
        // (SimReaders.Read's Covered lambda capturing its id reader), about 0.1 KB a frame on a board with a few powers.
        var gameFight = new GameFight();
        for (var i = 0; i < 8; i++) gameFight.Add<StrikeIronclad>();
        var toadpole = gameFight.Combat.CreateCreature(ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.Toadpole>().ToMutable(), MegaCrit.Sts2.Core.Combat.CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(toadpole);
        toadpole.Monster!.SetUpForCombat();
        ModelDb.Power<ThornsPower>().ToMutable().ApplyInternal(toadpole, amount: 2);
        ModelDb.Power<StrengthPower>().ToMutable().ApplyInternal(gameFight.Me.Creature, amount: 2);
        BoardProbe.Compute(gameFight.Me, gameFight.Combat);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) BoardProbe.Compute(gameFight.Me, gameFight.Combat);
        var perCall = (GC.GetAllocatedBytesForCurrentThread() - start) / 100;
        Assert.True(perCall == 0, $"{perCall} bytes per probe");
    }
}
