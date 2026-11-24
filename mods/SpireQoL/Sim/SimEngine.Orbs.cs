namespace SpireQoL.Sim;

/// <summary>
/// Orbs as actions (OrbCmd): channel (into full slots the front orb is evoked first), evoke the front orb, evoke every orb twice,
/// gain or lose slots, and Focus changes. A channelled orb's values come from its owner's state when it's channelled (its Focus,
/// Infused Core), and every channel goes through AfterOrbChanneled (Metronome). Models channel through <see cref="SimEngine.ChannelOrb"/>
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
    /// <summary>
    /// Channel: its count follows a count of the fight's log as the simulation goes, read once as the action starts (Voltaic: the Lightning
    /// channeled this fight, stored before its loop: decision #191).
    /// </summary>
    public LoggedAmount? TimesLogged { get; init; }
    /// <summary>EvokeFront: the orb stays after its last evoke (OrbCmd.EvokeNext with dequeue false: Dualcast's first, Shatter's).</summary>
    public bool Keeps { get; init; }
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
    public static SimOrb FreshOrb(SimState state, OrbKind kind, int player)
    {
        var simPlayer = state.Player(player);
        var focus = simPlayer?.Focus ?? 0;
        var lightningBonus = simPlayer != null && simPlayer.Relics.Has(RelicId.InfusedCore) ? Var(state, player, "InfusedCore", "ExtraDamage", fallback: 0) : 0;
        int F(int baseValue) => Math.Max(0, baseValue + focus);
        return kind switch
        {
            OrbKind.Lightning => new SimOrb { Kind = kind, Passive = F(baseValue: 3) + lightningBonus, Evoke = F(baseValue: 8) + lightningBonus },
            OrbKind.Frost => new SimOrb { Kind = kind, Passive = F(baseValue: 2), Evoke = F(baseValue: 5) },
            OrbKind.Dark => new SimOrb { Kind = kind, Passive = F(baseValue: 6), Evoke = 6 },
            OrbKind.Glass => new SimOrb { Kind = kind, Passive = F(baseValue: 4), Evoke = F(baseValue: 4) * 2, GlassBase = 4, GlassBonus = focus },
            OrbKind.Plasma => new SimOrb { Kind = kind, Passive = 1, Evoke = 2 }, // energy; Focus doesn't change it (PlasmaOrb)
            _ => new SimOrb { Kind = kind },
        };
    }

    /// <summary>
    /// OrbCmd.Channel for a player: with no slots yet a character that gets one on its first channel does; full slots evoke the
    /// front orb first (its owner's Thunder, its Frost block to them); then the orb goes in and AfterOrbChanneled runs.
    /// </summary>
    public static IEnumerable<SimState> ChannelOrb(SimState state, SimOrb orb, int player)
    {
        if (!state.TakesPart(player)) return new[] { state };
        var simPlayer = state.MutablePlayer(player);
        if (simPlayer.OrbCapacity == 0 && simPlayer.SlotOnFirstChannel) simPlayer.OrbCapacity = 1;
        IEnumerable<SimState> states = new[] { state };
        if (simPlayer.OrbCapacity > 0 && simPlayer.Orbs.Count >= simPlayer.OrbCapacity)
        {
            var front = simPlayer.Orbs[0];
            simPlayer.OwnOrbs().RemoveAt(0);
            states = Evoke(state, front, player);
        }
        return Then(states.ToList(), branch =>
        {
            var nextPlayer = branch.MutablePlayer(player);
            if (nextPlayer.OrbCapacity <= 0) return new[] { branch };
            nextPlayer.OwnOrbs().Add(orb.Clone());
            // During a play: one more orb channeled, for the play audit (it counts the game's OrbChanneledEntry, every player's).
            if (branch.CurrentPlay != null && !branch.EndOfTurnNow) branch.LogFired(SimKeys.PlayOrbChanneled);
            // OrbChanneledEntry, before AfterOrbChanneled (decision #191: Voltaic counts your Lightning).
            if (branch.History.Count > 0) Record(branch, new HistoryEvent(HistoryKind.OrbChanneled, player) { Orb = orb.Kind });
            return Hook(branch, nameof(SimModel.AfterOrbChanneled), (model, owner, copy) => model.AfterOrbChanneled(copy, owner, player));
        });
    }

    /// <summary>The orbs a random orb can be (OrbModel._validOrbs), each equally likely (Rng.NextItem).</summary>
    public static readonly OrbKind[] RandomOrbs = { OrbKind.Lightning, OrbKind.Frost, OrbKind.Dark, OrbKind.Plasma, OrbKind.Glass };

    /// <summary>
    /// OrbCmd.Channel of an orb of this kind, with its values as channelled; a random orb (OrbKind.Random: OrbModel.GetRandomOrb) is a
    /// branch per orb it can be, each its share. Identical branches merge in the step that runs it.
    /// </summary>
    public static IEnumerable<SimState> ChannelKind(SimState state, OrbKind kind, int player)
    {
        if (kind != OrbKind.Random) return ChannelOrb(state, FreshOrb(state, kind, player), player);
        var branches = new List<SimState>();
        for (var i = 0; i < RandomOrbs.Length; i++)
        {
            var branch = i == RandomOrbs.Length - 1 ? state : state.Clone();
            branch.Weight = state.Weight / RandomOrbs.Length;
            branches.AddRange(ChannelOrb(branch, FreshOrb(branch, RandomOrbs[i], player), player));
        }
        return branches;
    }

    private static void RunOrb(BranchSet branches, OrbAction orbAction)
    {
        switch (orbAction.Kind)
        {
            // A count that follows the log, read once as the action starts (its loop's bound is stored before it goes round): each branch its own.
            case OrbAction.Op.Channel when orbAction.TimesLogged is { } logged:
                branches.Each(branch =>
                {
                    var times = orbAction.Times + (int)logged.More(branch, branch.CurrentPlay?.Owner ?? Who.You);
                    IEnumerable<SimState> states = new[] { branch };
                    for (var time = 0; time < times; time++)
                        states = Then(states, copy => orbAction.Values is { } values ? ChannelOrb(copy, values, orbAction.To) : ChannelKind(copy, orbAction.Orb, orbAction.To));
                    return states;
                });
                break;
            case OrbAction.Op.Channel:
                for (var time = 0; time < orbAction.Times; time++)
                    branches.Each(branch => orbAction.Values is { } values ? ChannelOrb(branch, values, orbAction.To) : ChannelKind(branch, orbAction.Orb, orbAction.To));
                break;
            case OrbAction.Op.EvokeFront:
                for (var time = 0; time < orbAction.Times; time++)
                {
                    var last = time == orbAction.Times - 1;
                    branches.Each(branch =>
                    {
                        if (branch.You.Orbs.Count == 0) return new[] { branch };
                        var orb = branch.You.Orbs[0];
                        if (last && !orbAction.Keeps) branch.You.OwnOrbs().RemoveAt(0);
                        return Evoke(branch, orb);
                    });
                }
                break;
            case OrbAction.Op.EvokeAllTwice:
                // Front orb first, each evoked twice (then gone). One step per evoke, so identical branches merge and the branch
                // cap holds (one step for all of them was 4^16 copies with 4 enemies and 8 Lightning orbs).
                for (var i = branches.States.Max(branch => branch.You.Orbs.Count); i > 0; i--)
                {
                    branches.Each(branch => branch.You.Orbs.Count == 0 ? new[] { branch } : Evoke(branch, branch.You.Orbs[0]));
                    branches.Each(branch =>
                    {
                        if (branch.You.Orbs.Count == 0) return new[] { branch };
                        var orb = branch.You.Orbs[0];
                        branch.You.OwnOrbs().RemoveAt(0);
                        return Evoke(branch, orb);
                    });
                }
                break;
            case OrbAction.Op.Slots:
                branches.Do(branch =>
                {
                    branch.You.OrbCapacity = Math.Clamp(branch.You.OrbCapacity + orbAction.Slots, 0, Math.Max(MaxOrbSlots, branch.You.OrbCapacity));
                    while (branch.You.Orbs.Count > branch.You.OrbCapacity) branch.You.OwnOrbs().RemoveAt(branch.You.Orbs.Count - 1);
                });
                break;
        }
    }

}
