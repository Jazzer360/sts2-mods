namespace SpireQoL.Sim;

/// <summary>
/// Orbs as actions (OrbCmd): channel (into full slots the front orb is evoked first), evoke the front orb, evoke every orb twice,
/// gain or lose slots, and Focus changes. A channelled orb's values come from its owner's state when it's channelled (its Focus,
/// Infused Core), and every channel goes through AfterOrbChanneled (Metronome). Models channel through <see cref="ChannelOrb"/>
/// (Storm, Trash to Treasure).
/// </summary>
public sealed record OrbAction(OrbAction.Op Kind) : SimAction
{
    public enum Op { Channel, EvokeFront, EvokeAllTwice, Slots }

    /// <summary>Channel: the orb's kind (values from its owner's Focus when it's channelled).</summary>
    public OrbKind Orb { get; init; }
    /// <summary>Channel: fixed values instead (a play built as a summary).</summary>
    public SimOrb? Values { get; init; }
    /// <summary>Channel and EvokeFront: how many times.</summary>
    public int Times { get; init; } = 1;
    /// <summary>Slots: gained (OrbCmd.AddSlots, up to 10) or lost (OrbCmd.RemoveSlots: orbs past the new capacity drop from the back).</summary>
    public int Slots { get; init; }
    /// <summary>Whose orbs (Channel): you, or a targeted teammate (Ignition).</summary>
    public int To { get; init; } = Who.You;
}

/// <summary>Your Focus changes by this much (FocusPower or a temporary Focus power): your orbs' values move with it.</summary>
public sealed record FocusAction(int Delta) : SimAction;

public static partial class SimEngine
{
    /// <summary>
    /// A newly channelled orb's values for a player: the base values plus their Focus (FocusPower.ModifyOrbValue, not below 0),
    /// plus Infused Core's ExtraDamage on Lightning (InfusedCore.ModifyOrbValue). Unknown: a random orb (Trash to Treasure).
    /// </summary>
    public static SimOrb FreshOrb(SimState s, OrbKind kind, int player)
    {
        var p = s.Player(player);
        var focus = p?.Focus ?? 0;
        var lightningBonus = p != null && p.Relics.Has(RelicId.InfusedCore) ? Var(s, player, "InfusedCore", "ExtraDamage", 0) : 0;
        int F(int v) => Math.Max(0, v + focus);
        return kind switch
        {
            OrbKind.Lightning => new SimOrb { Kind = kind, Passive = F(3) + lightningBonus, Evoke = F(8) + lightningBonus },
            OrbKind.Frost => new SimOrb { Kind = kind, Passive = F(2), Evoke = F(5) },
            OrbKind.Dark => new SimOrb { Kind = kind, Passive = F(6), Evoke = 6 },
            OrbKind.Glass => new SimOrb { Kind = kind, Passive = F(4), Evoke = F(4) * 2, GlassBase = 4, GlassBonus = focus },
            OrbKind.Plasma => new SimOrb { Kind = kind, Passive = 1, Evoke = 2 }, // energy; Focus doesn't change it (PlasmaOrb)
            _ => new SimOrb { Kind = kind },
        };
    }

    /// <summary>
    /// OrbCmd.Channel for a player: with no slots yet a character that gets one on its first channel does; full slots evoke the
    /// front orb first (its owner's Thunder, its Frost block to them); then the orb goes in and AfterOrbChanneled runs.
    /// </summary>
    public static IEnumerable<SimState> ChannelOrb(SimState s, SimOrb orb, int player)
    {
        if (!s.TakesPart(player)) return new[] { s };
        var p = s.MutablePlayer(player);
        if (p.OrbCapacity == 0 && p.SlotOnFirstChannel) p.OrbCapacity = 1;
        IEnumerable<SimState> r = new[] { s };
        if (p.OrbCapacity > 0 && p.Orbs.Count >= p.OrbCapacity)
        {
            var front = p.Orbs[0];
            p.OwnOrbs().RemoveAt(0);
            r = Evoke(s, front, player);
        }
        return Then(r.ToList(), x =>
        {
            var px = x.MutablePlayer(player);
            if (px.OrbCapacity <= 0) return new[] { x };
            px.OwnOrbs().Add(orb.Clone());
            return Hook(x, nameof(SimModel.AfterOrbChanneled), (m, o, y) => m.AfterOrbChanneled(y, o, player));
        });
    }

    private static void RunOrb(BranchSet b, OrbAction a)
    {
        switch (a.Kind)
        {
            case OrbAction.Op.Channel:
                for (var t = 0; t < a.Times; t++)
                    b.Each(s => ChannelOrb(s, a.Values ?? FreshOrb(s, a.Orb, a.To), a.To));
                break;
            case OrbAction.Op.EvokeFront:
                for (var t = 0; t < a.Times; t++)
                {
                    var last = t == a.Times - 1;
                    b.Each(s =>
                    {
                        if (s.P.Orbs.Count == 0) return new[] { s };
                        var orb = s.P.Orbs[0];
                        if (last) s.P.OwnOrbs().RemoveAt(0);
                        return Evoke(s, orb);
                    });
                }
                break;
            case OrbAction.Op.EvokeAllTwice:
                // Front orb first, each evoked twice (then gone). One step per evoke, so identical branches merge and the branch
                // cap holds (one step for all of them was 4^16 copies with 4 enemies and 8 Lightning orbs).
                for (var i = b.States.Max(s => s.P.Orbs.Count); i > 0; i--)
                {
                    b.Each(s => s.P.Orbs.Count == 0 ? new[] { s } : Evoke(s, s.P.Orbs[0]));
                    b.Each(s =>
                    {
                        if (s.P.Orbs.Count == 0) return new[] { s };
                        var orb = s.P.Orbs[0];
                        s.P.OwnOrbs().RemoveAt(0);
                        return Evoke(s, orb);
                    });
                }
                break;
            case OrbAction.Op.Slots:
                b.Do(s =>
                {
                    s.P.OrbCapacity = Math.Clamp(s.P.OrbCapacity + a.Slots, 0, Math.Max(MaxOrbSlots, s.P.OrbCapacity));
                    while (s.P.Orbs.Count > s.P.OrbCapacity) s.P.OwnOrbs().RemoveAt(s.P.Orbs.Count - 1);
                });
                break;
        }
    }

}
