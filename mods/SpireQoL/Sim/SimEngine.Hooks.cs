namespace SpireQoL.Sim;

/// <summary>
/// The terms of the damage or block math that the simulator doesn't model (docs/SIMULATOR_V2.md "Pipelines"): every other
/// source's term, read from the game for one card and target (fixed for the turn), plus the card's enchantment's. Applied
/// in the game's order with the modelled sources' terms (Hook.ModifyDamage, Hook.ModifyBlock).
/// </summary>
public sealed record CapturedTerms
{
    public static readonly CapturedTerms None = new();

    /// <summary>The card's enchantment (applied to the base first: EnchantDamageAdditive, then EnchantDamageMultiplicative).</summary>
    public decimal EnchantAdd { get; init; }
    public decimal EnchantMul { get; init; } = 1m;
    /// <summary>Unmodelled sources: the sum of their additive terms, the products of their multipliers, the smallest cap.</summary>
    public decimal Add { get; init; }
    public decimal Mul { get; init; } = 1m;
    public decimal Late { get; init; } = 1m;
    public decimal Cap { get; init; } = decimal.MaxValue;
}

public static partial class SimEngine
{
    /// <summary>
    /// The models listening for hooks, in the game's order (CombatState.IterateHookListeners): each player in player order
    /// (their powers, then their relics), then each enemy (its powers). Only modelled names; with each one's owner.
    /// </summary>
    internal static List<(SimModel Model, int Owner)> Listeners(SimState s)
    {
        // Cached on the state until one of the sets it was built from changes (they're immutable: a change is a new set).
        if (s.ListenerCache != null && SameSets(s, s.ListenerSets!)) return s.ListenerCache;
        var sets = CurrentSets(s);
        var result = AnyModelled(s) ? BuildListeners(s) : NoListeners;
        s.ListenerCache = result;
        s.ListenerSets = sets;
        return result;
    }

    /// <summary>Every power and relic set listeners come from, in listener order.</summary>
    private static PowerSet[] CurrentSets(SimState s)
    {
        var sets = new PowerSet[2 + 2 * s.Teammates.Count + s.Enemies.Count];
        var i = 0;
        sets[i++] = s.P.Powers; sets[i++] = s.P.Relics;
        foreach (var t in s.Teammates) { sets[i++] = t.Powers; sets[i++] = t.Relics; }
        foreach (var e in s.Enemies) sets[i++] = e.Powers;
        return sets;
    }

    private static bool SameSets(SimState s, PowerSet[] sets)
    {
        if (sets.Length != 2 + 2 * s.Teammates.Count + s.Enemies.Count) return false;
        var i = 0;
        if (!ReferenceEquals(sets[i++], s.P.Powers) || !ReferenceEquals(sets[i++], s.P.Relics)) return false;
        foreach (var t in s.Teammates) if (!ReferenceEquals(sets[i++], t.Powers) || !ReferenceEquals(sets[i++], t.Relics)) return false;
        foreach (var e in s.Enemies) if (!ReferenceEquals(sets[i++], e.Powers)) return false;
        return true;
    }

    private static List<(SimModel Model, int Owner)> BuildListeners(SimState s)
    {
        var list = new List<(SimModel, int)>();
        void Add(PowerSet set, int owner)
        {
            foreach (var e in set.Items)
                if (SimModels.Get(e.Name) is { } m) list.Add((m, owner));
        }
        var before = Math.Min(s.TeammatesBeforeMe, s.Teammates.Count);
        void Player(int k)
        {
            var p = k < 0 ? s.P : s.Teammates[k];
            var id = k < 0 ? Who.You : Who.Teammate(k);
            Add(p.Powers, id);
            Add(p.Relics, id);
        }
        for (var k = 0; k < before; k++) Player(k);
        Player(-1);
        for (var k = before; k < s.Teammates.Count; k++) Player(k);
        foreach (var e in s.Enemies) Add(e.Powers, e.Id);
        return list;
    }

    private static readonly List<(SimModel Model, int Owner)> NoListeners = new();

    /// <summary>Whether any creature or player holds a modelled power or relic (most fights' cheap path: nothing to call).</summary>
    private static bool AnyModelled(SimState s)
    {
        static bool Any(PowerSet set)
        {
            foreach (var e in set.Items) if (SimModels.Has(e.Name)) return true;
            return false;
        }
        if (Any(s.P.Powers) || Any(s.P.Relics)) return true;
        foreach (var t in s.Teammates) if (Any(t.Powers) || Any(t.Relics)) return true;
        foreach (var e in s.Enemies) if (Any(e.Powers)) return true;
        return false;
    }

    /// <summary>
    /// Runs a hook on every listener, in order, on each branch (a listener's random effect branches; identical results
    /// merge). The listeners are the ones present when the hook starts (the game builds the list first).
    /// </summary>
    internal static IEnumerable<SimState> Hook(SimState s, string hook, Func<SimModel, int, SimState, IEnumerable<SimState>> call)
    {
        IEnumerable<SimState> states = new[] { s };
        foreach (var (m, owner) in Listeners(s))
        {
            if (!m.Overrides(hook)) continue;
            var model = m;
            var o = owner;
            states = Then(states, x => call(model, o, x));
        }
        return states;
    }

    // ------------------------------------------------------------------ pipelines

    /// <summary>
    /// Hook.ModifyDamage for one hit: the base, the enchantment, every listener's additive term, then multipliers, late
    /// multipliers and the smallest cap, with the captured terms of unmodelled sources in their places; the game's int cast.
    /// </summary>
    public static int CalcDamage(SimState s, decimal baseDamage, DamageCtx d, CapturedTerms? captured = null) => CalcDamage(s, baseDamage, d, captured, null);

    /// <param name="modifiers">Gets the modelled sources whose term changed the amount (the game's modifiers list), when given.</param>
    public static int CalcDamage(SimState s, decimal baseDamage, DamageCtx d, CapturedTerms? captured, List<(SimModel Model, int Owner)>? modifiers)
    {
        var c = captured ?? CapturedTerms.None;
        var num = baseDamage;
        num += c.EnchantAdd;
        num *= c.EnchantMul;
        var listeners = Listeners(s);
        foreach (var (m, o) in listeners) { var t = m.ModifyDamageAdditive(s, o, d); num += t; if (t != 0m) modifiers?.Add((m, o)); }
        num += c.Add;
        foreach (var (m, o) in listeners) { var t = m.ModifyDamageMultiplicative(s, o, d); num *= t; if (t != 1m) modifiers?.Add((m, o)); }
        num *= c.Mul;
        foreach (var (m, o) in listeners) { var t = m.ModifyDamageMultiplicativeLate(s, o, d); num *= t; if (t != 1m) modifiers?.Add((m, o)); }
        num *= c.Late;
        var cap = c.Cap;
        (SimModel, int)? capper = null;
        foreach (var (m, o) in listeners) { var t = m.ModifyDamageCap(s, o, d); if (t < cap) { cap = t; capper = (m, o); } }
        if (num > cap) { num = cap; if (capper is { } cm) modifiers?.Add(cm); }
        return Math.Max(0, (int)num);
    }

    /// <summary>Hook.ModifyBlock: the base, the enchantment, additive terms, then multipliers; the game's int cast.</summary>
    public static int CalcBlock(SimState s, decimal baseBlock, BlockCtx b, CapturedTerms? captured = null) => CalcBlock(s, baseBlock, b, captured, null);

    public static int CalcBlock(SimState s, decimal baseBlock, BlockCtx b, CapturedTerms? captured, List<(SimModel Model, int Owner)>? modifiers)
    {
        var c = captured ?? CapturedTerms.None;
        var num = baseBlock;
        num += c.EnchantAdd;
        num *= c.EnchantMul;
        var listeners = Listeners(s);
        foreach (var (m, o) in listeners) { var t = m.ModifyBlockAdditive(s, o, b); num += t; if (t != 0m) modifiers?.Add((m, o)); }
        num += c.Add;
        foreach (var (m, o) in listeners) { var t = m.ModifyBlockMultiplicative(s, o, b); num *= t; if (t != 1m) modifiers?.Add((m, o)); }
        num *= c.Mul;
        return Math.Max(0, (int)Math.Max(0m, num));
    }

    // ------------------------------------------------------------------ creature access

    /// <summary>A creature's powers by id (enemy id, You, Teammate(k)).</summary>
    public static PowerSet PowersOf(SimState s, int id) => id >= 0
        ? s.Enemy(id)?.Powers ?? PowerSet.Empty
        : id == Who.You ? s.P.Powers : s.Teammates[Who.TeammateIndex(id)].Powers;

    /// <summary>Sets a creature's powers (a teammate shared between branches is copied first).</summary>
    public static void SetPowers(SimState s, int id, PowerSet powers)
    {
        if (id >= 0) { if (s.Enemy(id) is { } e) e.Powers = powers; }
        else if (id == Who.You) s.P.Powers = powers;
        else s.MutableTeammate(Who.TeammateIndex(id)).Powers = powers;
    }

    /// <summary>A player's relic counters by id.</summary>
    public static PowerSet RelicsOf(SimState s, int id) => id == Who.You ? s.P.Relics : id <= -2 ? s.Teammates[Who.TeammateIndex(id)].Relics : PowerSet.Empty;

    public static void SetRelics(SimState s, int id, PowerSet relics)
    {
        if (id == Who.You) s.P.Relics = relics;
        else if (id <= -2) s.MutableTeammate(Who.TeammateIndex(id)).Relics = relics;
    }

    public static int PowerOn(SimState s, int id, string power) => PowersOf(s, id)[power];
}

public static partial class SimEngine
{
    /// <summary>A hook on every branch as one step, or nothing when no branch has a listener (so fights without models cost the same).</summary>
    private static void HookStep(BranchSet b, string hook, Func<SimModel, int, SimState, IEnumerable<SimState>> call)
    {
        if (!b.States.Any(s => Listens(s, hook))) return;
        b.Each(s => Hook(s, hook, call));
    }

    /// <summary>Some listener in this state overrides the hook.</summary>
    private static bool Listens(SimState s, string hook)
    {
        foreach (var (m, _) in Listeners(s)) if (m.Overrides(hook)) return true;
        return false;
    }
}
