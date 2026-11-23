using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using SpireQoL.Procs;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What a play does to your orbs, read from its program (decision #182: PlayFacts.OrbsOf over CardEffects.OrbChannels, OrbEvokes and
/// OrbSlotChanges) and worked out on the board: each channel's orb and its loop's rounds, the evokes, the slots.
/// </summary>
[Collection(GameFightCollection.Name)]
public class OrbPlanTests
{
    private static void Enemy(GameFight gameFight)
    {
        var enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
    }

    [Fact]
    public void ChannelsTakeTheirOrbAndTheirLoopsRounds()
    {
        var gameFight = new GameFight();
        Assert.Equal(new[] { (OrbKind.Frost, 2) }, PlayFacts.OrbsOf(gameFight.Add<Glacier>()).Channels);
        Assert.Equal(new[] { (OrbKind.Lightning, 1), (OrbKind.Frost, 1), (OrbKind.Dark, 1) }, PlayFacts.OrbsOf(gameFight.Add<Rainbow>()).Channels);
        // Chill: one Frost for each enemy you can hit.
        Enemy(gameFight);
        Enemy(gameFight);
        Assert.Equal(new[] { (OrbKind.Frost, 2) }, PlayFacts.OrbsOf(gameFight.Add<Chill>()).Channels);
        // Chaos: random orbs, its Repeat of them.
        var chaos = gameFight.Add<Chaos>();
        Assert.Equal(new[] { (OrbKind.Random, Sts2State.Var(chaos, "Repeat")) }, PlayFacts.OrbsOf(chaos).Channels);
        Assert.Null(PlayFacts.OrbsOf(chaos).ChannelKind);
    }

    [Fact]
    public void SpinnerChannelsOnlyWhenUpgraded()
    {
        var gameFight = new GameFight();
        Assert.Empty(PlayFacts.OrbsOf(gameFight.Add<Spinner>()).Channels);
        Assert.Equal(new[] { (OrbKind.Glass, 1) }, PlayFacts.OrbsOf(gameFight.Add<Spinner>(upgraded: true)).Channels);
    }

    [Fact]
    public void TempestChannelsItsXAndOneMoreUpgraded()
    {
        // Tempest.OnPlay: numOfOrbs = ResolveEnergyXValue(), one more upgraded (Metronome counted X before).
        var gameFight = new GameFight();
        gameFight.Me.PlayerCombatState!.Energy = 3;
        Assert.Equal(new[] { (OrbKind.Lightning, 3) }, PlayFacts.OrbsOf(gameFight.Add<Tempest>()).Channels);
        Assert.Equal(new[] { (OrbKind.Lightning, 4) }, PlayFacts.OrbsOf(gameFight.Add<Tempest>(upgraded: true)).Channels);
    }

    [Fact]
    public void EvokesAndSlotsAreReadFromTheCommands()
    {
        var gameFight = new GameFight();
        Assert.Equal(2, PlayFacts.OrbsOf(gameFight.Add<Dualcast>()).EvokesFront);
        var quadcast = gameFight.Add<Quadcast>();
        Assert.Equal(Sts2State.Var(quadcast, "Repeat"), PlayFacts.OrbsOf(quadcast).EvokesFront);
        var shatter = PlayFacts.OrbsOf(gameFight.Add<Shatter>());
        Assert.Equal((0, 2), (shatter.EvokesFront, shatter.EvokesEachOrb));
        var capacitor = gameFight.Add<Capacitor>();
        Assert.Equal(Sts2State.Var(capacitor, "Repeat"), PlayFacts.OrbsOf(capacitor).Slots);
        var bulkUp = gameFight.Add<BulkUp>();
        Assert.Equal(-Sts2State.Var(bulkUp, "OrbSlots"), PlayFacts.OrbsOf(bulkUp).Slots);
        // Multicast: its X, one more upgraded.
        gameFight.Me.PlayerCombatState!.Energy = 2;
        Assert.Equal(3, PlayFacts.OrbsOf(gameFight.Add<MultiCast>(upgraded: true)).EvokesFront);
    }
}
