using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>Orbs as actions (Sim/SimEngine.Orbs.cs): OrbCmd.Channel and evokes, values from the owner's state when channelled.</summary>
public class OrbActionTests
{
    private static SimState Defect(int slots = 3, int focus = 0)
    {
        var state = new SimState { Enemies = { Enemy(0, 100) } };
        state.You.OrbCapacity = slots;
        state.You.Focus = focus;
        return state;
    }

    [Fact]
    public void ChannelledOrbsTakeYourFocusWhenChannelledAndFocusMovesTheOthers()
    {
        var after = Assert.Single(After(Defect(focus: 2), Play(Card("Zap", SimCardType.Skill), -1, 1,
            new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Lightning },
            new FocusAction(1),
            new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Frost })));
        Assert.Equal(new[] { (6, 11), (5, 8) }, after.You.Orbs.Select(orb => (orb.Passive, orb.Evoke)));
        Assert.Equal(3, after.You.Focus);
    }

    [Fact]
    public void ChannellingIntoFullSlotsEvokesTheFrontOrb()
    {
        var state = Defect(slots: 1);
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } };
        var after = Assert.Single(After(state, Play(Card("Zap", SimCardType.Skill), -1, 1, new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Lightning })));
        Assert.Equal(5, after.You.Block);
        Assert.Equal(OrbKind.Lightning, Assert.Single(after.You.Orbs).Kind);
    }

    [Fact]
    public void StormChannelsLightningOnPowerCardsAndMetronomeCountsIt()
    {
        var state = Defect();
        state.You.Powers = state.You.Powers.With(PowerId.Storm, 2);
        state.You.Relics = state.You.Relics.With(RelicId.Metronome, 6, 1);
        state.Vars[$"{Who.You}:Metronome:OrbCount"] = 7;
        state.Vars[$"{Who.You}:Metronome:Damage"] = 30;
        var after = Assert.Single(After(state, Play(Card("Defragment", SimCardType.Power), -1, 1)));
        Assert.Equal(2, after.You.Orbs.Count(orb => orb.Kind == OrbKind.Lightning));
        Assert.Equal(70, after.Enemy(0)!.Hp); // the 7th channel sets Metronome off
        Assert.True(after.Fired.Has("StormPower"));
        // Not on a Skill.
        Assert.Empty(Assert.Single(After(state, Play(Card("Zap", SimCardType.Skill), -1, 1))).You.Orbs);
    }

    [Fact]
    public void TrashToTreasureChannelsAnOrbPerStatusCreated()
    {
        var state = Defect();
        state.You.Powers = state.You.Powers.With(PowerId.TrashToTreasure, 1);
        var after = Assert.Single(After(state, Play(Card("Purge", SimCardType.Skill), -1, 1, new CardEventAction(CardEvent.GeneratedStatus, 2))));
        Assert.Equal(2, after.You.Orbs.Count(orb => orb.Kind == OrbKind.Unknown));
    }

    [Fact]
    public void InfusedCoreAddsToLightning()
    {
        var state = Defect();
        state.You.Relics = state.You.Relics.With(RelicId.InfusedCore, 0);
        state.Vars[$"{Who.You}:InfusedCore:ExtraDamage"] = 1;
        var orb = Assert.Single(Assert.Single(After(state, Play(Card("Zap", SimCardType.Skill), -1, 1, new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Lightning }))).You.Orbs);
        Assert.Equal((4, 9), (orb.Passive, orb.Evoke));
    }
}
