namespace SpireQoL.Sim;

/// <summary>
/// The terms of the damage or block math that the simulator doesn't model (docs/SIMULATOR.md "Pipelines"): every other
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
    internal static List<(SimModel Model, int Owner)> Listeners(SimState state)
    {
        // Cached on the state until one of the sets it was built from changes (they're immutable: a change is a new set).
        if (state.ListenerCache != null && SameSets(state, state.ListenerSets!)) return state.ListenerCache;
        var sets = CurrentSets(state);
        var result = AnyModelled(state) ? BuildListeners(state) : NoListeners;
        state.ListenerCache = result;
        state.ListenerSets = sets;
        return result;
    }

    /// <summary>Every power and relic set listeners come from, in listener order.</summary>
    private static object[] CurrentSets(SimState state)
    {
        var sets = new object[2 + 2 * state.Teammates.Count + state.Enemies.Count];
        var i = 0;
        sets[i++] = state.You.Powers; sets[i++] = state.You.Relics;
        foreach (var player in state.Teammates) { sets[i++] = player.Powers; sets[i++] = player.Relics; }
        foreach (var enemy in state.Enemies) sets[i++] = enemy.Powers;
        return sets;
    }

    private static bool SameSets(SimState state, object[] sets)
    {
        if (sets.Length != 2 + 2 * state.Teammates.Count + state.Enemies.Count) return false;
        var i = 0;
        if (!ReferenceEquals(sets[i++], state.You.Powers) || !ReferenceEquals(sets[i++], state.You.Relics)) return false;
        foreach (var player in state.Teammates) if (!ReferenceEquals(sets[i++], player.Powers) || !ReferenceEquals(sets[i++], player.Relics)) return false;
        foreach (var enemy in state.Enemies) if (!ReferenceEquals(sets[i++], enemy.Powers)) return false;
        return true;
    }

    private static List<(SimModel Model, int Owner)> BuildListeners(SimState state)
    {
        var list = new List<(SimModel, int)>();
        void Add(IEnumerable<string> names, int owner)
        {
            foreach (var name in names)
                if (SimModels.Get(name) is { } model) list.Add((model, owner));
        }
        foreach (var id in state.PlayerIds)
        {
            var player = state.Player(id)!;
            Add(player.Powers.Items.Select(entry => entry.Name.Name), id);
            Add(player.Relics.Items.Select(entry => entry.Name.Name), id);
        }
        foreach (var enemy in state.Enemies) Add(enemy.Powers.Items.Select(entry => entry.Name.Name), enemy.Id);
        return list;
    }

    private static readonly List<(SimModel Model, int Owner)> NoListeners = new();

    /// <summary>Whether any creature or player holds a modelled power or relic (most fights' cheap path: nothing to call).</summary>
    private static bool AnyModelled(SimState state)
    {
        static bool Any(PowerSet powers)
        {
            foreach (var entry in powers.Items) if (SimModels.Has(entry.Name.Name)) return true;
            return false;
        }
        static bool AnyRelic(RelicSet relics)
        {
            foreach (var entry in relics.Items) if (SimModels.Has(entry.Name.Name)) return true;
            return false;
        }
        if (Any(state.You.Powers) || AnyRelic(state.You.Relics)) return true;
        foreach (var player in state.Teammates) if (Any(player.Powers) || AnyRelic(player.Relics)) return true;
        foreach (var enemy in state.Enemies) if (Any(enemy.Powers)) return true;
        return false;
    }

    /// <summary>
    /// Runs a hook on every listener, in order, on each branch (a listener's random effect branches; identical results
    /// merge). The listeners are the ones present when the hook starts (the game builds the list first).
    /// </summary>
    internal static IEnumerable<SimState> Hook(SimState state, string hook, Func<SimModel, int, SimState, IEnumerable<SimState>> call)
    {
        IEnumerable<SimState> states = new[] { state };
        foreach (var (simModel, owner) in Listeners(state))
        {
            if (!simModel.Overrides(hook)) continue;
            var model = simModel;
            var o = owner;
            states = Then(states, branch => call(model, o, branch));
        }
        return states;
    }

    // ------------------------------------------------------------------ pipelines

    /// <summary>
    /// Hook.ModifyDamage for one hit: the base, the enchantment, every listener's additive term, then multipliers, late
    /// multipliers and the smallest cap, with the captured terms of unmodelled sources in their places; the game's int cast.
    /// </summary>
    public static int CalcDamage(SimState state, decimal baseDamage, DamageCtx damage, CapturedTerms? captured = null) => CalcDamage(state, baseDamage, damage, captured, null);

    /// <param name="modifiers">Gets the modelled sources whose term changed the amount (the game's modifiers list), when given.</param>
    public static int CalcDamage(SimState state, decimal baseDamage, DamageCtx damage, CapturedTerms? captured, List<(SimModel Model, int Owner)>? modifiers)
    {
        var terms = captured ?? CapturedTerms.None;
        var num = baseDamage;
        num += terms.EnchantAdd;
        num *= terms.EnchantMul;
        var listeners = Listeners(state);
        foreach (var (model, o) in listeners) { var t = model.ModifyDamageAdditive(state, o, damage); num += t; if (t != 0m) modifiers?.Add((model, o)); }
        num += terms.Add;
        foreach (var (simModel, o) in listeners) { var t = simModel.ModifyDamageMultiplicative(state, o, damage); num *= t; if (t != 1m) modifiers?.Add((simModel, o)); }
        num *= terms.Mul;
        foreach (var (nextModel, o) in listeners) { var t = nextModel.ModifyDamageMultiplicativeLate(state, o, damage); num *= t; if (t != 1m) modifiers?.Add((nextModel, o)); }
        num *= terms.Late;
        var cap = terms.Cap;
        (SimModel, int)? capper = null;
        foreach (var (m, o) in listeners) { var t = m.ModifyDamageCap(state, o, damage); if (t < cap) { cap = t; capper = (m, o); } }
        if (num > cap) { num = cap; if (capper is { } cm) modifiers?.Add(cm); }
        return Math.Max(0, (int)num);
    }

    /// <summary>Hook.ModifyBlock: the base, the enchantment, additive terms, then multipliers; the game's int cast.</summary>
    public static int CalcBlock(SimState state, decimal baseBlock, BlockCtx block, CapturedTerms? captured = null) => CalcBlock(state, baseBlock, block, captured, null);

    public static int CalcBlock(SimState state, decimal baseBlock, BlockCtx block, CapturedTerms? captured, List<(SimModel Model, int Owner)>? modifiers)
    {
        var terms = captured ?? CapturedTerms.None;
        var num = baseBlock;
        num += terms.EnchantAdd;
        num *= terms.EnchantMul;
        var listeners = Listeners(state);
        foreach (var (model, o) in listeners) { var t = model.ModifyBlockAdditive(state, o, block); num += t; if (t != 0m) modifiers?.Add((model, o)); }
        num += terms.Add;
        foreach (var (simModel, o) in listeners) { var t = simModel.ModifyBlockMultiplicative(state, o, block); num *= t; if (t != 1m) modifiers?.Add((simModel, o)); }
        num *= terms.Mul;
        return Math.Max(0, (int)Math.Max(0m, num));
    }

    // ------------------------------------------------------------------ creature access

    /// <summary>A creature's powers by id (enemy id, You, Teammate(k)).</summary>
    public static PowerSet PowersOf(SimState state, int id) => Who.IsEnemy(id)
        ? state.Enemy(id)?.Powers ?? PowerSet.Empty
        : state.Player(id)?.Powers ?? PowerSet.Empty;

    /// <summary>Sets a creature's powers (a teammate shared between branches is copied first).</summary>
    public static void SetPowers(SimState state, int id, PowerSet powers)
    {
        if (Who.IsEnemy(id)) { if (state.Enemy(id) is { } enemy) enemy.Powers = powers; }
        else if (state.Player(id) != null) state.MutablePlayer(id).Powers = powers;
    }

    /// <summary>A player's relic counters by id.</summary>
    public static RelicSet RelicsOf(SimState state, int id) => state.Player(id)?.Relics ?? RelicSet.Empty;

    public static void SetRelics(SimState state, int id, RelicSet relics)
    {
        if (state.Player(id) != null) state.MutablePlayer(id).Relics = relics;
    }

    public static int PowerOn(SimState state, int id, PowerId power) => PowersOf(state, id)[power];
}

public static partial class SimEngine
{
    /// <summary>A hook on every branch as one step, or nothing when no branch has a listener (so fights without models cost the same).</summary>
    private static void HookStep(BranchSet branches, string hook, Func<SimModel, int, SimState, IEnumerable<SimState>> call)
    {
        if (!branches.States.Any(branch => Listens(branch, hook))) return;
        branches.Each(branch => Hook(branch, hook, call));
    }

    /// <summary>Some listener in this state overrides the hook.</summary>
    private static bool Listens(SimState state, string hook)
    {
        foreach (var (model, _) in Listeners(state)) if (model.Overrides(hook)) return true;
        return false;
    }
}
