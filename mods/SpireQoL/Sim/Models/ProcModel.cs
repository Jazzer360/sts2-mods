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
    /// <summary>The Data bit that says it was used this turn (a power's: PowerUse; a relic's: RelicUse).</summary>
    protected virtual int UsedThisTurnBit => (int)PowerUse.UsedThisTurn;
    /// <summary>Used this turn (once a turn).</summary>
    protected bool UsedThisTurn(SimState state, int owner) => (Data(state, owner) & UsedThisTurnBit) != 0;
    protected void MarkUsedThisTurn(SimState state, int owner) => Set(state, owner, Amount(state, owner), Data(state, owner) | UsedThisTurnBit);
    protected virtual void Set(SimState state, int owner, int amount, int data)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if (powers.Has(ThisPower)) SimEngine.SetPowers(state, owner, powers.With(ThisPower, amount, data));
    }

    /// <summary>One of its values (DynamicVars) as the adapter read it.</summary>
    protected int Var(SimState state, int owner, string var, int fallback) => SimEngine.Var(state, owner, Name, var, fallback);
    /// <summary>One of its values summed over its copies (an instanced power whose copies each act on their own value).</summary>
    protected int VarTotal(SimState state, int owner, string var, int fallback) => SimEngine.VarTotal(state, owner, Name, var, fallback);
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
    protected IEnumerable<SimState> Cause(IEnumerable<SimState> states, CardEvent cardEvent, int count, int player, SimCard? card = null)
    {
        var tally = SimEngine.TallyFor(Name, Tab, cardEvent, player);
        return states.SelectMany(branch => SimEngine.ProcCardEvent(branch, cardEvent, count, player, onEach: tally, card: card)).ToList();
    }

    /// <summary>
    /// Cards made into a player's hand (CardPileCmd.AddGeneratedCardToCombat to PileType.Hand): they join the hand
    /// (SimEngine.AddToHand), then what reacts to cards made runs (AfterCardGeneratedForCombat), about <paramref name="card"/>.
    /// </summary>
    protected IEnumerable<SimState> MakeInHand(IEnumerable<SimState> states, SimCard? card, int count, int player, bool ethereal = false) =>
        Cause(states.Select(branch => { SimEngine.AddToHand(branch, player, count, ethereal); return branch; }).ToList(), CardEvent.Generated, count, player, card);

    /// <summary>Channels <paramref name="times"/> orbs of a kind for a player, one after another (OrbCmd.Channel); a random one is a branch per orb.</summary>
    protected static IEnumerable<SimState> Channel(IEnumerable<SimState> states, int player, OrbKind kind, int times)
    {
        var otherStates = states;
        for (var i = 0; i < times; i++) otherStates = otherStates.SelectMany(branch => SimEngine.ChannelKind(branch, kind, player)).ToList();
        return otherStates;
    }
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
        Block(state, owner, Var(state, owner, "Block", fallback: 0), PropsOf(state, owner, "Block"), times);
    protected IEnumerable<SimState> RelicDamageAll(SimState state, int owner, int times = 1) =>
        DamageAll(state, owner, Var(state, owner, "Damage", fallback: 0), PropsOf(state, owner, "Damage"), times);
    protected IEnumerable<SimState> RelicDamageRandom(SimState state, int owner, int times = 1) =>
        DamageRandom(state, owner, Var(state, owner, "Damage", fallback: 0), PropsOf(state, owner, "Damage"), times);
    protected override int UsedThisTurnBit => (int)RelicUse.UsedThisTurn;
    /// <summary>Armed (RelicUse.Armed: unused this combat or turn, as RelicCountersFeature.IsArmed reads it).</summary>
    protected bool Armed(SimState state, int owner) => ((RelicUse)Data(state, owner)).HasFlag(RelicUse.Armed);
    protected void Disarm(SimState state, int owner) => Set(state, owner, Amount(state, owner), (int)((RelicUse)Data(state, owner) & ~RelicUse.Armed));
}
