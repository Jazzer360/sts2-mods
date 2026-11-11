using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards that change your orb slots (decompile v0.111.0): OrbCmd.RemoveSlots (Bulk Up) drops the
/// orbs past the new capacity from the back without evoking them (OrbQueue.RemoveCapacity);
/// OrbCmd.AddSlots (Capacitor, Modded) adds slots up to 10.
/// </summary>
public class SimOrbSlotTests
{
    private static SimOrb Orb(OrbKind k, int passive, int evoke) => new() { Kind = k, Passive = passive, Evoke = evoke };

    private static SimState TwoSlots()
    {
        var s = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 79, Hits = { 24 } } } };
        s.P.OrbCapacity = 2;
        s.P.Orbs.Add(Orb(OrbKind.Glass, 3, 6));
        s.P.Orbs.Add(Orb(OrbKind.Lightning, 3, 8));
        return s;
    }

    [Fact]
    public void RemovingASlotDropsTheBackOrbWithoutEvokingIt()
    {
        // Auto-report: Bulk Up with [Glass, Lightning] in 2 slots left [Glass]; the enemy took nothing.
        var play = new SimPlay { Name = "BulkUp" };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Slots, Slots = -1 });
        SimEngine.ApplyPlayTo(TwoSlots(), play, out var b, out _);
        Assert.Equal(1, b[0].P.OrbCapacity);
        Assert.Equal(new[] { OrbKind.Glass }, b[0].P.Orbs.Select(o => o.Kind));
        Assert.Equal(79, b[0].Enemy(1)!.Hp);
    }

    [Fact]
    public void RemovingTheLastSlotLeavesNoOrbsAndChannelsFail()
    {
        // Capacity can't go below 0; with no slots, channelling does nothing (OrbQueue.TryEnqueue).
        var play = new SimPlay { Name = "BulkUp" };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Slots, Slots = -3 });
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Orb(OrbKind.Frost, 2, 5) });
        SimEngine.ApplyPlayTo(TwoSlots(), play, out var b, out _);
        Assert.Equal(0, b[0].P.OrbCapacity);
        Assert.Empty(b[0].P.Orbs);
        Assert.Equal(79, b[0].Enemy(1)!.Hp);
    }

    [Fact]
    public void AddingSlotsLetsAChannelFitWithoutEvokingUpToTen()
    {
        // Capacitor: +2 slots, so a channel into the (formerly full) slots doesn't evoke the front orb.
        var play = new SimPlay { Name = "Capacitor" };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Slots, Slots = 2 });
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Orb(OrbKind.Frost, 2, 5) });
        SimEngine.ApplyPlayTo(TwoSlots(), play, out var b, out _);
        Assert.Equal(4, b[0].P.OrbCapacity);
        Assert.Equal(3, b[0].P.Orbs.Count);
        Assert.Equal(79, b[0].Enemy(1)!.Hp);

        // OrbCmd.AddSlots stops at 10 slots.
        var s = TwoSlots();
        s.P.OrbCapacity = 9;
        var big = new SimPlay { Name = "Capacitor" };
        big.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Slots, Slots = 2 });
        SimEngine.ApplyPlayTo(s, big, out b, out _);
        Assert.Equal(10, b[0].P.OrbCapacity);
    }

    [Fact]
    public void ACharacterWithoutOrbSlotsGetsOneOnTheFirstChannel()
    {
        // OrbCmd.Channel: a character with no base orb slots (BaseOrbSlotCount 0) and no capacity gains
        // a slot first, so the orb is channeled; a Defect whose slots are all gone doesn't.
        var s = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 20, Hits = { 5 } } } };
        s.P.SlotOnFirstChannel = true;
        var play = new SimPlay { Name = "Zap" };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Orb(OrbKind.Lightning, 3, 8) });
        SimEngine.ApplyPlayTo(s, play, out var b, out _);
        Assert.Equal(1, b[0].P.OrbCapacity);
        Assert.Single(b[0].P.Orbs);

        s.P.SlotOnFirstChannel = false;
        SimEngine.ApplyPlayTo(s, play, out b, out _);
        Assert.Equal(0, b[0].P.OrbCapacity);
        Assert.Empty(b[0].P.Orbs);
    }
}
