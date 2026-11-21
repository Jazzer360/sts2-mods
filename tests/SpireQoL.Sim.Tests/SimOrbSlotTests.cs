using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards that change your orb slots (decompile v0.111.0): OrbCmd.RemoveSlots (Bulk Up) drops the
/// orbs past the new capacity from the back without evoking them (OrbQueue.RemoveCapacity);
/// OrbCmd.AddSlots (Capacitor, Modded) adds slots up to 10.
/// </summary>
public class SimOrbSlotTests
{
    private static SimOrb Orb(OrbKind orbKind, int passive, int evoke) => new() { Kind = orbKind, Passive = passive, Evoke = evoke };

    private static SimState TwoSlots()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 79, Hits = { 24 } } } };
        state.You.OrbCapacity = 2;
        state.You.Orbs.Add(Orb(OrbKind.Glass, passive: 3, evoke: 6));
        state.You.Orbs.Add(Orb(OrbKind.Lightning, passive: 3, evoke: 8));
        return state;
    }

    [Fact]
    public void RemovingASlotDropsTheBackOrbWithoutEvokingIt()
    {
        // Auto-report: Bulk Up with [Glass, Lightning] in 2 slots left [Glass]; the enemy took nothing.
        var play = new SimPlay { Name = "BulkUp" };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Slots, Slots = -1 });
        SimEngine.ApplyPlayTo(TwoSlots(), play, out var states, out _);
        Assert.Equal(1, states[0].You.OrbCapacity);
        Assert.Equal(new[] { OrbKind.Glass }, states[0].You.Orbs.Select(orb => orb.Kind));
        Assert.Equal(79, states[0].Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void RemovingTheLastSlotLeavesNoOrbsAndChannelsFail()
    {
        // Capacity can't go below 0; with no slots, channelling does nothing (OrbQueue.TryEnqueue).
        var play = new SimPlay { Name = "BulkUp" };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Slots, Slots = -3 });
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Orb(OrbKind.Frost, passive: 2, evoke: 5) });
        SimEngine.ApplyPlayTo(TwoSlots(), play, out var states, out _);
        Assert.Equal(0, states[0].You.OrbCapacity);
        Assert.Empty(states[0].You.Orbs);
        Assert.Equal(79, states[0].Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void AddingSlotsLetsAChannelFitWithoutEvokingUpToTen()
    {
        // Capacitor: +2 slots, so a channel into the (formerly full) slots doesn't evoke the front orb.
        var play = new SimPlay { Name = "Capacitor" };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Slots, Slots = 2 });
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Orb(OrbKind.Frost, passive: 2, evoke: 5) });
        SimEngine.ApplyPlayTo(TwoSlots(), play, out var states, out _);
        Assert.Equal(4, states[0].You.OrbCapacity);
        Assert.Equal(3, states[0].You.Orbs.Count);
        Assert.Equal(79, states[0].Enemy(id: 1)!.Hp);

        // OrbCmd.AddSlots stops at 10 slots.
        var state = TwoSlots();
        state.You.OrbCapacity = 9;
        var big = new SimPlay { Name = "Capacitor" };
        big.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Slots, Slots = 2 });
        SimEngine.ApplyPlayTo(state, big, out states, out _);
        Assert.Equal(10, states[0].You.OrbCapacity);
    }

    [Fact]
    public void ACharacterWithoutOrbSlotsGetsOneOnTheFirstChannel()
    {
        // OrbCmd.Channel: a character with no base orb slots (BaseOrbSlotCount 0) and no capacity gains
        // a slot first, so the orb is channeled; a Defect whose slots are all gone doesn't.
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 20, Hits = { 5 } } } };
        state.You.SlotOnFirstChannel = true;
        var play = new SimPlay { Name = "Zap" };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Orb(OrbKind.Lightning, passive: 3, evoke: 8) });
        SimEngine.ApplyPlayTo(state, play, out var states, out _);
        Assert.Equal(1, states[0].You.OrbCapacity);
        Assert.Single(states[0].You.Orbs);

        state.You.SlotOnFirstChannel = false;
        SimEngine.ApplyPlayTo(state, play, out states, out _);
        Assert.Equal(0, states[0].You.OrbCapacity);
        Assert.Empty(states[0].You.Orbs);
    }
}
