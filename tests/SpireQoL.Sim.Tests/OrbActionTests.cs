using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>Orbs as actions (Sim/SimEngine.Orbs.cs): OrbCmd.Channel and evokes, values from the owner's state when channelled.</summary>
public class OrbActionTests
{
    private static SimState Defect(int slots = 3, int focus = 0)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.You.OrbCapacity = slots;
        state.You.Focus = focus;
        return state;
    }

    [Fact]
    public void ChannelledOrbsTakeYourFocusWhenChannelledAndFocusMovesTheOthers()
    {
        var after = Assert.Single(After(Defect(focus: 2), Play(Card("Zap", SimCardType.Skill), target: Who.None, plays: 1,
            new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Lightning },
            new FocusAction(Delta: 1),
            new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Frost })));
        Assert.Equal(new[] { (6, 11), (5, 8) }, after.You.Orbs.Select(orb => (orb.Passive, orb.Evoke)));
        Assert.Equal(3, after.You.Focus);
    }

    [Fact]
    public void ChannellingIntoFullSlotsEvokesTheFrontOrb()
    {
        var state = Defect(slots: 1);
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } };
        var after = Assert.Single(After(state, Play(Card("Zap", SimCardType.Skill), target: Who.None, plays: 1, new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Lightning })));
        Assert.Equal(5, after.You.Block);
        Assert.Equal(OrbKind.Lightning, Assert.Single(after.You.Orbs).Kind);
    }

    [Fact]
    public void APlaysChannelsAndItsEvokesDamageAreInItsLog()
    {
        // The play audit counts the game's OrbChanneledEntry and the damage with no card a play's evokes deal: the simulation logs each
        // channel of the play (SimKeys.PlayOrbChanneled) and what each evoke dealt the enemies, block and HP (SimKeys.OrbEvokeDamage).
        var state = Defect(slots: 1);
        state.Enemies[0].Block = 3;
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Dark, Passive = 6, Evoke = 10 } };
        var after = Assert.Single(After(state, Play(Card("Zap", SimCardType.Skill), target: Who.None, plays: 2, new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Frost })));
        Assert.Equal(2, after.Fired[SimKeys.PlayOrbChanneled]);
        // The Dark orb pushed out by the first channel deals 10 (3 into block); the Frost one pushed out by the second blocks.
        Assert.Equal(10, after.Fired.DataOf(SimKeys.OrbEvokeDamage));
        Assert.Equal(100 - (10 - 3), after.Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void StormChannelsLightningOnPowerCardsAndMetronomeCountsIt()
    {
        var state = Defect();
        state.You.Powers = state.You.Powers.With(PowerId.Storm, amount: 2);
        state.You.Relics = state.You.Relics.With(RelicId.Metronome, amount: 0, data: 0);
        state.You.Fields = state.You.Fields.With($"{RelicId.Metronome.Name}.OrbsChanneled", 6); // its own count, read from its code (as the adapter seeds it)
        state.Vars[$"{Who.You}:Metronome:OrbCount"] = 7;
        state.Vars[$"{Who.You}:Metronome:Damage"] = 30;
        var after = Assert.Single(After(state, Play(Card("Defragment", SimCardType.Power), target: Who.None, plays: 1)));
        Assert.Equal(2, after.You.Orbs.Count(orb => orb.Kind == OrbKind.Lightning));
        Assert.Equal(70, after.Enemy(id: 0)!.Hp); // the 7th channel sets Metronome off
        Assert.True(after.Fired.Has("StormPower"));
        // Not on a Skill.
        Assert.Empty(Assert.Single(After(state, Play(Card("Zap", SimCardType.Skill), target: Who.None, plays: 1))).You.Orbs);
    }

    [Fact]
    public void TrashToTreasureChannelsAnOrbPerStatusCreated()
    {
        var state = Defect();
        state.You.Powers = state.You.Powers.With(PowerId.TrashToTreasure, amount: 1);
        var branches = After(state, Play(Card("Purge", SimCardType.Skill), target: Who.None, plays: 1, new CardEventAction(CardEvent.Generated, Count: 2) { Card = new SimCard(CardId.Dazed.Name, SimCardType.Status) }));
        // Two random orbs, one after the other: every order of two of the five, each 1/25.
        Assert.Equal(25, branches.Count);
        Assert.All(branches, branch => Assert.Equal(2, branch.You.Orbs.Count(orb => SimEngine.RandomOrbs.Contains(orb.Kind))));
        Assert.All(branches, branch => Assert.Equal(1.0 / 25, branch.Weight, precision: 9));
    }

    [Fact]
    public void ARandomOrbIsABranchPerOrbItCanBe()
    {
        // Chaos (OrbModel.GetRandomOrb: Rng.NextItem over Lightning, Frost, Dark, Plasma, Glass): each orb 1/5, with its values as channelled.
        var branches = After(Defect(focus: 1), Play(Card("Chaos", SimCardType.Skill), target: Who.None, plays: 1, new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Random }));
        Assert.Equal(SimEngine.RandomOrbs.OrderBy(kind => kind), branches.Select(branch => Assert.Single(branch.You.Orbs).Kind).OrderBy(kind => kind));
        Assert.All(branches, branch => Assert.Equal(0.2, branch.Weight, precision: 9));
        var lightning = branches.Select(branch => branch.You.Orbs[0]).Single(orb => orb.Kind == OrbKind.Lightning);
        Assert.Equal((4, 9), (lightning.Passive, lightning.Evoke));
    }

    [Fact]
    public void AnOrbTheSimulatorDoesntKnowIsUnmodelledAtTheEndOfTheTurn()
    {
        var state = Defect();
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Unknown } };
        var outcome = SimEngine.Run(state);
        Assert.Contains(outcome.Branches, branch => branch.Unmodelled != null);
    }

    [Fact]
    public void ARandomOrbsPassiveActsAtTheEndOfTheTurnInItsShare()
    {
        // Lightning's passive hits the one enemy (3), Glass hits every enemy (4): the enemy takes damage in 2 of the 5 branches.
        var state = Defect();
        var outcome = SimEngine.Run(state, Play(Card("Chaos", SimCardType.Skill), target: Who.None, plays: 1, new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Random }));
        var hurt = outcome.Chance(branch => branch.Enemy(id: 0)!.Hp < 100);
        Assert.Equal(0.4, hurt.Low, precision: 9);
        Assert.Equal(0.4, hurt.High, precision: 9);
    }

    [Fact]
    public void InfusedCoreAddsToLightning()
    {
        var state = Defect();
        state.You.Relics = state.You.Relics.With(RelicId.InfusedCore, amount: 0);
        state.Vars[$"{Who.You}:InfusedCore:ExtraDamage"] = 1;
        var orb = Assert.Single(Assert.Single(After(state, Play(Card("Zap", SimCardType.Skill), target: Who.None, plays: 1, new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Lightning }))).You.Orbs);
        Assert.Equal((4, 9), (orb.Passive, orb.Evoke));
    }
}
