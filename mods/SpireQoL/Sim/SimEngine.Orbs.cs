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

/// <summary>An orb a hook is told of, or whose own program runs: whose it is, its slot (-1: out of the slots, as it's evoked or channelled) and its kind.</summary>
public readonly record struct OrbCtx(int Player, int Index, OrbKind Kind);

public static partial class SimEngine
{
    /// <summary>
    /// A newly channelled orb for a player (OrbCmd.Channel's new orb): its fields as its class starts them and its values through the game's
    /// pipeline (Focus, Infused Core), from its code (decision #214). A kind whose code isn't read (another mod's, or a class a game update made
    /// unreadable) is an orb that does nothing here; acting, it makes the simulation unknown.
    /// </summary>
    public static SimOrb FreshOrb(SimState state, OrbKind kind, int player)
    {
        if (SimModels.Orb(kind) is { } model) return model.NewOrb(state, player);
        if (kind != OrbKind.Unknown) state.Unmodelled ??= SimModels.OrbUnreadReason(kind);
        return new SimOrb { Kind = kind };
    }

    /// <summary>A listener of the state answers this hook (none: the engine has nothing to tell).</summary>
    private static bool AnyListens(SimState state, string hook)
    {
        foreach (var (model, _) in Listeners(state)) if (model.Overrides(hook)) return true;
        return false;
    }

    /// <summary>OrbModel.ModifyOrbValue: Hook.ModifyOrbValue on a value of <paramref name="orb"/>, every listener's term in turn (Focus, Infused Core).</summary>
    internal static decimal OrbValue(SimState state, OrbCtx orb, decimal value)
    {
        foreach (var (model, owner) in Listeners(state))
            if (model.Overrides(nameof(SimModel.ModifyOrbValue))) value = model.ModifyOrbValue(state, owner, orb, value);
        return value;
    }

    /// <summary>A player's orbs' values worked out again from their getters' code (something the pipeline reads changed: Focus).</summary>
    internal static void OrbValuesAgain(SimState state, int player)
    {
        if (state.Player(player) is not { Orbs.Count: > 0 }) return;
        var orbs = state.MutablePlayer(player).OwnOrbs();
        for (var index = 0; index < orbs.Count; index++) SimModels.Orb(orbs[index].Kind)?.ValuesAgain(state, player, index, orbs[index]);
    }

    /// <summary>An orb's name in Incoming Damage's breakdown: its kind, "(evoked)" for an evoke's, "all" for damage to every enemy (Glass (evoked, all)).</summary>
    private static string OrbLabel(OrbKind kind, bool evoke, bool all) =>
        kind + (evoke && all ? " (evoked, all)" : evoke ? " (evoked)" : all ? " (all)" : "");

    /// <summary>
    /// An orb's own CreatureCmd.Damage (its Passive's, its Evoke's: OrbModel's owner deals it, Unpowered) on its targets: a roll over the
    /// hittable enemies (a branch each), every one, or the one with the least HP. Its passive's in Incoming Damage's breakdown, an evoke's
    /// there at your end of turn and in the play audit's damage with no card during a play; an evoke's targets kept for AfterOrbEvoked.
    /// </summary>
    internal static IEnumerable<SimState> OrbDamage(SimState state, OrbCtx orb, bool evoke, HookTarget target, int amount, SimProps props)
    {
        var player = orb.Player;
        var listed = evoke ? state.Log && state.EndOfTurnNow && player == Who.You : state.Log;
        var label = OrbLabel(orb.Kind, evoke, all: target == HookTarget.AllEnemies);
        var inPlay = evoke && state.CurrentPlay != null && !state.EndOfTurnNow;
        var dealtBefore = state.AttackDealt;
        void Dealt(SimState branch)
        {
            if (inPlay && branch.AttackDealt > dealtBefore) branch.LogFired(SimKeys.OrbEvokeDamage, branch.AttackDealt - dealtBefore);
        }
        switch (target)
        {
            case HookTarget.RandomEnemy:
                if (listed && state.Targetable.Any()) state.EndOfTurnDamage.Add((label, amount));
                return HitRandom(state, _ => amount, then: (branch, enemy) =>
                {
                    Dealt(branch);
                    if (evoke && branch.OrbTargets != null) branch.OrbTargets = new[] { enemy.Id };
                }, dealer: player, props: props);
            case HookTarget.AllEnemies:
            {
                var targets = state.Targetable.Select(enemy => enemy.Id).ToArray();
                if (listed && targets.Length > 0) state.EndOfTurnDamage.Add((label, amount));
                foreach (var id in targets) if (state.Enemy(id) is { } enemy) Damage(state, enemy, amount, player, props);
                Dealt(state);
                if (evoke && state.OrbTargets != null) state.OrbTargets = targets;
                return new[] { state };
            }
            default:
            {
                var weakest = state.Targetable.OrderBy(enemy => enemy.Hp).FirstOrDefault();
                if (listed && weakest != null) state.EndOfTurnDamage.Add((label, amount));
                if (weakest != null) Damage(state, weakest, amount, player, props);
                Dealt(state);
                if (evoke && state.OrbTargets != null) state.OrbTargets = weakest != null ? new[] { weakest.Id } : Array.Empty<int>();
                return new[] { state };
            }
        }
    }

    /// <summary>
    /// An orb's own CreatureCmd.GainBlock (Frost's: its owner's, or another player's with Hibernate), Unpowered: in Incoming Damage's breakdown for
    /// yours (an evoke's at your end of turn), and an evoke's during a play for the play audit.
    /// </summary>
    internal static IEnumerable<SimState> OrbBlock(SimState state, OrbCtx orb, bool evoke, int gainer, int amount)
    {
        if (!Who.IsPlayer(gainer)) return new[] { state };
        if (evoke && state.CurrentPlay != null && !state.EndOfTurnNow && amount > 0) state.LogFired(SimKeys.OrbEvokeBlock, amount, gainer);
        var listed = !evoke || state.Log && state.EndOfTurnNow;
        return GainBlock(state, amount, listed && gainer == Who.You ? OrbLabel(orb.Kind, evoke, all: false) : null, gainer);
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

    /// <summary>
    /// OrbCmd.Evoke of an orb out of its slots (OrbCmd removes it first): its Evoke's code, then Hook.AfterOrbEvoked with what it acted on (Thunder
    /// hits the living ones again). An orb whose code isn't read does nothing (another mod's: the audit matches any orb).
    /// </summary>
    /// <param name="player">Whose orb it is: their Thunder hits again, and a teammate's Frost blocks for them, not you.</param>
    private static IEnumerable<SimState> Evoke(SimState state, SimOrb orb, int player = Who.You)
    {
        if (SimModels.Orb(orb.Kind) is not { } model)
        {
            // Another mod's orb does nothing here (the audit matches any orb); one of the game's whose code isn't read makes it unknown (decision #92).
            if (orb.Kind != OrbKind.Unknown) state.Unmodelled ??= SimModels.OrbUnreadReason(orb.Kind);
            return new[] { state };
        }
        var ctx = new OrbCtx(player, Index: -1, orb.Kind);
        if (!AnyListens(state, nameof(SimModel.AfterOrbEvoked)))
            return model.RunOrb(HookName.Evoke, state, player, index: -1, orb);
        state.OrbTargets = Array.Empty<int>();
        return Then(model.RunOrb(HookName.Evoke, state, player, index: -1, orb).ToList(), branch =>
        {
            var told = Hook(branch, nameof(SimModel.AfterOrbEvoked), (listener, owner, copy) => listener.AfterOrbEvoked(copy, owner, ctx)).ToList();
            foreach (var after in told) after.OrbTargets = null;
            return told;
        });
    }

    /// <summary>
    /// One player's orb passives at the turn's end (OrbQueue.BeforeTurnEnd: each orb in order, front first, whose class's BeforeTurnEndOrbTrigger
    /// calls OrbModel.TriggerPassive), or as their next turn starts in a projection (OrbQueue.AfterTurnStart, AfterTurnStartOrbTrigger: Plasma's
    /// energy, decision #306), as <paramref name="at"/> says: Hook.ModifyOrbPassiveTriggerCount for it (Gold-Plated Cables on the front one), the listeners that changed it
    /// told (AfterModifyingOrbPassiveTriggerCount), then its Passive's code that many times. Yours, or a teammate's (their Frost blocks for them).
    /// </summary>
    private static void OrbPassives(BranchSet branches, int player, OrbTrigger at = OrbTrigger.TurnEnd)
    {
        var maxOrbs = branches.States.Max(branch => branch.Player(player)!.Orbs.Count);
        for (var i = 0; i < maxOrbs; i++)
        {
            var index = i;
            ReadModel? ModelAt(SimState state) => state.Player(player) is { } simPlayer && index < simPlayer.Orbs.Count ? SimModels.Orb(simPlayer.Orbs[index].Kind) : null;
            bool Acts(SimState state)
            {
                if (state.Player(player) is not { } simPlayer || index >= simPlayer.Orbs.Count) return false;
                if (ModelAt(state) is { } model) return model.PassiveAt == at;
                // An orb whose code isn't read (another mod's): what its passive does isn't modelled.
                state.Unmodelled ??= simPlayer.Orbs[index].Kind == OrbKind.Unknown ? (at == OrbTrigger.TurnEnd ? "an orb the simulator doesn't know acts at the end of the turn" : "an orb the simulator doesn't know acts as your turn starts") : SimModels.OrbUnreadReason(simPlayer.Orbs[index].Kind);
                return false;
            }
            OrbCtx Ctx(SimState state) => new(player, index, state.Player(player)!.Orbs[index].Kind);
            int Triggers(SimState state)
            {
                if (!Acts(state)) return 0;
                var ctx = Ctx(state);
                var count = 1;
                foreach (var (model, owner) in Listeners(state))
                    if (model.Overrides(nameof(SimModel.ModifyOrbPassiveTriggerCounts))) count = model.ModifyOrbPassiveTriggerCounts(state, owner, ctx, count);
                return count;
            }
            // TriggerPassive's count, then AfterModifyingOrbPassiveTriggerCount for each listener whose term changed it (none listening: nothing to tell).
            if (branches.States.Any(branch => AnyListens(branch, nameof(SimModel.ModifyOrbPassiveTriggerCounts))))
                branches.Do(branch =>
            {
                if (!Acts(branch)) return;
                var ctx = Ctx(branch);
                var count = 1;
                foreach (var (model, owner) in Listeners(branch))
                {
                    if (!model.Overrides(nameof(SimModel.ModifyOrbPassiveTriggerCounts))) continue;
                    var before = count;
                    count = model.ModifyOrbPassiveTriggerCounts(branch, owner, ctx, count);
                    if (count != before) model.AfterModifyingOrbPassiveTriggerCount(branch, owner, ctx);
                }
            });
            var maxTriggers = branches.States.Max(Triggers);
            for (var triggerIndex = 0; triggerIndex < maxTriggers; triggerIndex++)
            {
                var trigger = triggerIndex;
                branches.Each(branch => trigger < Triggers(branch) && ModelAt(branch) is { } model
                    ? model.RunOrb(HookName.Passive, branch, player, index, evoked: null) : new[] { branch });
            }
        }
    }

    /// <summary>
    /// Your Focus changes (FocusAction: a card's FocusPower or temporary Focus): FocusPower's amount, and every value of your orbs worked out again
    /// through the pipeline that reads it (FocusPower.ModifyOrbValue, read from its code).
    /// </summary>
    private static void ChangeFocus(SimState state, int delta)
    {
        state.You.Focus += delta;
        OrbValuesAgain(state, Who.You);
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
