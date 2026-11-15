namespace SpireQoL.Sim.Models;

/// <summary>
/// A power or relic that does something when a card is played, drawn, exhausted... (a proc): its tab comes from the play log
/// (docs/SIMULATOR_V2.md "Tabs from the log"). Helpers for its amount, values, the play binding the game keeps, and logging.
/// </summary>
public abstract class ProcModel : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;

    protected const SimProps Unpowered = SimProps.Unpowered;

    protected static bool Mine(SimCardPlay play, int owner) => play.Owner == owner;

    /// <summary>Its amount on its owner (a power's stacks, a relic's counter).</summary>
    protected virtual int Amount(SimState s, int owner) => SimEngine.PowerOn(s, owner, Name);
    protected virtual int Data(SimState s, int owner) => SimEngine.PowersOf(s, owner).DataOf(Name);
    protected virtual void Set(SimState s, int owner, int amount, int data)
    {
        var p = SimEngine.PowersOf(s, owner);
        if (p.Has(Name)) SimEngine.SetPowers(s, owner, p.With(Name, amount, data));
    }

    /// <summary>One of its values (DynamicVars) as the adapter read it.</summary>
    protected int Var(SimState s, int owner, string var, int fallback) => SimEngine.Var(s, owner, Name, var, fallback);
    /// <summary>The props of one of its values (a BlockVar's or DamageVar's), Unpowered when unknown.</summary>
    protected SimProps PropsOf(SimState s, int owner, string var) => (SimProps)SimEngine.Var(s, owner, Name, var + ".Props", (int)Unpowered);

    /// <summary>Binds the play in progress with an amount (BeforeCardPlayed: the game's amountsForPlayedCards).</summary>
    protected void Bind(SimState s, int owner, int amount) => s.Bound = s.Bound.With($"{Name}@{owner}", amount);
    /// <summary>The amount bound to the play, removed (AfterCardPlayed); null when it wasn't bound.</summary>
    protected int? TakeBound(SimState s, int owner)
    {
        var key = $"{Name}@{owner}";
        if (!s.Bound.Has(key)) return null;
        var v = s.Bound[key];
        s.Bound = s.Bound.Without(key);
        return v;
    }

    /// <summary>It fired (a trigger: no block or damage of its own).</summary>
    protected IEnumerable<SimState> Fire(SimState s, int owner, int amount = 0)
    {
        s.LogFired(Name, amount, owner);
        return One(s);
    }

    protected IEnumerable<SimState> Block(SimState s, int owner, decimal amount, SimProps props = Unpowered, int times = 1) =>
        SimEngine.ProcBlock(s, owner, Name, amount, props, times);
    protected IEnumerable<SimState> DamageAll(SimState s, int owner, decimal amount, SimProps props = Unpowered, int times = 1) =>
        SimEngine.ProcDamageAll(s, owner, Name, amount, props, times);
    protected IEnumerable<SimState> DamageRandom(SimState s, int owner, decimal amount, SimProps props = Unpowered, int times = 1) =>
        SimEngine.ProcDamageRandom(s, owner, Name, amount, props, times);
    /// <summary>Cards it makes happen (Dark Embrace's draw, Calamity's attacks), through every listener.</summary>
    protected static IEnumerable<SimState> Then(IEnumerable<SimState> states, CardEvent ev, int count, int player) =>
        states.SelectMany(x => SimEngine.ProcCardEvent(x, ev, count, player)).ToList();

    /// <summary>Any card created for combat (AfterCardGeneratedForCombat), statuses and Sadness included.</summary>
    protected static bool Generated(CardEvent ev) => ev is CardEvent.Generated or CardEvent.GeneratedStatus or CardEvent.GeneratedSadness;
}

/// <summary>A relic proc: its amount is its counter, its data a flag (armed, used this turn...), its values its DynamicVars.</summary>
public abstract class RelicProc : ProcModel
{
    protected override int Amount(SimState s, int owner) => SimEngine.RelicsOf(s, owner)[Name];
    protected override int Data(SimState s, int owner) => SimEngine.RelicsOf(s, owner).DataOf(Name);
    protected override void Set(SimState s, int owner, int amount, int data)
    {
        var r = SimEngine.RelicsOf(s, owner);
        if (r.Has(Name)) SimEngine.SetRelics(s, owner, r.With(Name, amount, data));
    }
    /// <summary>Its Block value, as its BlockVar gives it (with its props).</summary>
    protected IEnumerable<SimState> RelicBlock(SimState s, int owner, int times = 1) =>
        Block(s, owner, Var(s, owner, "Block", 0), PropsOf(s, owner, "Block"), times);
    protected IEnumerable<SimState> RelicDamageAll(SimState s, int owner, int times = 1) =>
        DamageAll(s, owner, Var(s, owner, "Damage", 0), PropsOf(s, owner, "Damage"), times);
    protected IEnumerable<SimState> RelicDamageRandom(SimState s, int owner, int times = 1) =>
        DamageRandom(s, owner, Var(s, owner, "Damage", 0), PropsOf(s, owner, "Damage"), times);
    /// <summary>Armed (the adapter's Data 1: unused this combat or turn, as RelicCountersFeature.IsArmed reads it).</summary>
    protected bool Armed(SimState s, int owner) => (Data(s, owner) & 1) != 0;
    protected void Disarm(SimState s, int owner) => Set(s, owner, Amount(s, owner), Data(s, owner) & ~1);
}

/// <summary>
/// A counter relic (Kunai, Shuriken, Ornamental Fan...): every card of its kind its owner plays counts (AfterCardPlayed); the one
/// that brings the count to its Cards value fires it, and the count starts over. Amount: the count (the tray's number).
/// </summary>
public abstract class CounterRelic : RelicProc
{
    protected abstract bool Counts(SimCard card);
    protected abstract IEnumerable<SimState> OnComplete(SimState s, int owner);

    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || !Counts(play.Card)) return One(s);
        var n = Var(s, owner, "Cards", 0);
        if (n <= 0) return One(s);
        var count = Amount(s, owner) % n + 1;
        Set(s, owner, count % n, Data(s, owner));
        if (count < n) return One(s);
        return OnComplete(s, owner);
    }
}
