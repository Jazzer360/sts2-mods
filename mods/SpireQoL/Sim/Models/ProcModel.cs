namespace SpireQoL.Sim.Models;

/// <summary>
/// A power or relic that does something when a card is played, drawn, exhausted... (a proc): its tab comes from the play log
/// (docs/SIMULATOR.md "Tabs from the log"). Helpers for its amount, values, the play binding the game keeps, and logging.
/// </summary>
public abstract class ProcModel : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;

    protected const SimProps Unpowered = SimProps.Unpowered;

    protected static bool Mine(SimCardPlay play, int owner) => play.Owner == owner;

    /// <summary>Its amount on its owner (a power's stacks, a relic's counter).</summary>
    protected virtual int Amount(SimState state, int owner) => SimEngine.PowerOn(state, owner, ThisPower);
    protected virtual int Data(SimState state, int owner) => SimEngine.PowersOf(state, owner).DataOf(ThisPower);
    protected virtual void Set(SimState state, int owner, int amount, int data)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if (powers.Has(ThisPower)) SimEngine.SetPowers(state, owner, powers.With(ThisPower, amount, data));
    }

    /// <summary>One of its values (DynamicVars) as the adapter read it.</summary>
    protected int Var(SimState state, int owner, string var, int fallback) => SimEngine.Var(state, owner, Name, var, fallback);
    /// <summary>The props of one of its values (a BlockVar's or DamageVar's), Unpowered when unknown.</summary>
    protected SimProps PropsOf(SimState state, int owner, string var) => (SimProps)SimEngine.Var(state, owner, Name, var + ".Props", (int)Unpowered);

    /// <summary>Binds the play in progress with an amount (BeforeCardPlayed: the game's amountsForPlayedCards).</summary>
    protected void Bind(SimState state, int owner, int amount) => state.Bound = state.Bound.With(SimKeys.Owned(Name, owner), amount);
    /// <summary>The amount bound to the play, removed (AfterCardPlayed); null when it wasn't bound.</summary>
    protected int? TakeBound(SimState state, int owner)
    {
        var key = SimKeys.Owned(Name, owner);
        if (!state.Bound.Has(key)) return null;
        var bound = state.Bound[key];
        state.Bound = state.Bound.Without(key);
        return bound;
    }

    /// <summary>It fired (a trigger: no block or damage of its own).</summary>
    protected IEnumerable<SimState> Fire(SimState state, int owner, int amount = 0)
    {
        state.LogFired(Name, amount, owner);
        return One(state);
    }

    protected IEnumerable<SimState> Block(SimState state, int owner, decimal amount, SimProps props = Unpowered, int times = 1) =>
        SimEngine.ProcBlock(state, owner, Name, amount, props, times);
    protected IEnumerable<SimState> DamageAll(SimState state, int owner, decimal amount, SimProps props = Unpowered, int times = 1) =>
        SimEngine.ProcDamageAll(state, owner, Name, amount, props, times);
    protected IEnumerable<SimState> DamageRandom(SimState state, int owner, decimal amount, SimProps props = Unpowered, int times = 1) =>
        SimEngine.ProcDamageRandom(state, owner, Name, amount, props, times);
    /// <summary>Card events it causes (Dark Embrace's draws, Calamity's cards), after the branches it's given, each through every listener.</summary>
    /// <remarks>A draw or energy tab's amount is what the events gave: each card drawn (fewer into a full hand or from empty
    /// piles), each energy (decision #132).</remarks>
    protected IEnumerable<SimState> Cause(IEnumerable<SimState> states, CardEvent cardEvent, int count, int player)
    {
        var tally = SimEngine.TallyFor(Name, Tab, cardEvent, player);
        return states.SelectMany(branch => SimEngine.ProcCardEvent(branch, cardEvent, count, player, onEach: tally)).ToList();
    }

    /// <summary>Channels <paramref name="times"/> orbs of a kind for a player, one after another (OrbCmd.Channel).</summary>
    protected static IEnumerable<SimState> Channel(IEnumerable<SimState> states, int player, OrbKind kind, int times)
    {
        var otherStates = states;
        for (var i = 0; i < times; i++) otherStates = otherStates.SelectMany(branch => SimEngine.ChannelOrb(branch, SimEngine.FreshOrb(branch, kind, player), player)).ToList();
        return otherStates;
    }

    /// <summary>Any card created for combat (AfterCardGeneratedForCombat), statuses and Sadness included.</summary>
    protected static bool Generated(CardEvent cardEvent) => cardEvent is CardEvent.Generated or CardEvent.GeneratedStatus or CardEvent.GeneratedSadness;
}

/// <summary>A relic proc: its amount is its counter, its data a flag (armed, used this turn...), its values its DynamicVars.</summary>
public abstract class RelicProc : ProcModel
{
    protected override int Amount(SimState state, int owner) => SimEngine.RelicsOf(state, owner)[ThisRelic];
    protected override int Data(SimState state, int owner) => SimEngine.RelicsOf(state, owner).DataOf(ThisRelic);
    protected override void Set(SimState state, int owner, int amount, int data)
    {
        var relics = SimEngine.RelicsOf(state, owner);
        if (relics.Has(ThisRelic)) SimEngine.SetRelics(state, owner, relics.With(ThisRelic, amount, data));
    }
    /// <summary>Its Block value, as its BlockVar gives it (with its props).</summary>
    protected IEnumerable<SimState> RelicBlock(SimState state, int owner, int times = 1) =>
        Block(state, owner, Var(state, owner, "Block", 0), PropsOf(state, owner, "Block"), times);
    protected IEnumerable<SimState> RelicDamageAll(SimState state, int owner, int times = 1) =>
        DamageAll(state, owner, Var(state, owner, "Damage", 0), PropsOf(state, owner, "Damage"), times);
    protected IEnumerable<SimState> RelicDamageRandom(SimState state, int owner, int times = 1) =>
        DamageRandom(state, owner, Var(state, owner, "Damage", 0), PropsOf(state, owner, "Damage"), times);
    /// <summary>Armed (the adapter's Data 1: unused this combat or turn, as RelicCountersFeature.IsArmed reads it).</summary>
    protected bool Armed(SimState state, int owner) => (Data(state, owner) & 1) != 0;
    protected void Disarm(SimState state, int owner) => Set(state, owner, Amount(state, owner), Data(state, owner) & ~1);
}

/// <summary>
/// A counter relic (Kunai, Shuriken, Ornamental Fan...): every card of its kind its owner plays counts (AfterCardPlayed); the one
/// that brings the count to its Cards value fires it, and the count starts over. Amount: the count (the tray's number).
/// </summary>
public abstract class CounterRelic : RelicProc
{
    protected abstract bool Counts(SimCard card);
    protected abstract IEnumerable<SimState> OnComplete(SimState state, int owner);

    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || !Counts(play.Card)) return One(state);
        var cardsNeeded = Var(state, owner, "Cards", 0);
        if (cardsNeeded <= 0) return One(state);
        var count = Amount(state, owner) % cardsNeeded + 1;
        Set(state, owner, count % cardsNeeded, Data(state, owner));
        if (count < cardsNeeded) return One(state);
        return OnComplete(state, owner);
    }
}
