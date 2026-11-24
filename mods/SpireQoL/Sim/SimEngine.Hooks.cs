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

/// <summary>
/// What a hook listener is, in the order CombatState.IterateHookListeners lists a creature's (allies, then enemies): its powers, then
/// for a player their relics (not melted), potions, orbs and the cards in every pile, or for a monster the monster itself. The
/// simulator has models for powers, relics and cards; no potion, orb or monster class acts on a hook in a way the order changes
/// (docs/SIMULATOR.md "Listener order").
/// </summary>
public enum ListenerKind { Power, Relic, Potion, Orb, Card, Monster }

public static partial class SimEngine
{
    /// <summary>
    /// The models listening for hooks, in the game's order (CombatState.IterateHookListeners): each player in player order (their
    /// powers, then their relics, then the cards in their piles: SimPlayer.Cards), then each enemy (its powers). Only modelled names;
    /// with each one's owner.
    /// </summary>
    internal static List<(SimModel Model, int Owner)> Listeners(SimState state)
    {
        // Cached on the state until one of the sets it was built from changes (they're immutable: a change is a new set).
        if (state.ListenerCache != null && SameSets(state, state.ListenerSets!)) return state.ListenerCache;
        var sets = CurrentSets(state);
        var result = AnyModelled(state) ? BuildListeners(state, kinds: null) : NoListeners;
        state.ListenerCache = result;
        state.ListenerSets = sets;
        return result;
    }

    /// <summary>The listeners as the order test reads them (ListenerOrderTests): each one's kind, name and owner, in listener order.</summary>
    internal static List<(ListenerKind Kind, string Name, int Owner)> ListenerOrder(SimState state)
    {
        var kinds = new List<ListenerKind>();
        var listeners = BuildListeners(state, kinds);
        return listeners.Select((listener, index) => (kinds[index], listener.Model.Name, listener.Owner)).ToList();
    }

    /// <summary>Sets kept per player: their powers, relics and cards.</summary>
    private const int SetsPerPlayer = 3;

    /// <summary>Every set listeners come from, in listener order.</summary>
    private static object[] CurrentSets(SimState state)
    {
        var sets = new object[SetsPerPlayer * (1 + state.Teammates.Count) + state.Enemies.Count];
        var next = 0;
        sets[next++] = state.You.Powers; sets[next++] = state.You.Relics; sets[next++] = state.You.Cards;
        foreach (var player in state.Teammates) { sets[next++] = player.Powers; sets[next++] = player.Relics; sets[next++] = player.Cards; }
        foreach (var enemy in state.Enemies) sets[next++] = enemy.Powers;
        return sets;
    }

    private static bool SameSets(SimState state, object[] sets)
    {
        if (sets.Length != SetsPerPlayer * (1 + state.Teammates.Count) + state.Enemies.Count) return false;
        var next = 0;
        static bool Same(object[] sets, ref int next, SimPlayer player) =>
            ReferenceEquals(sets[next++], player.Powers) && ReferenceEquals(sets[next++], player.Relics) && ReferenceEquals(sets[next++], player.Cards);
        if (!Same(sets, ref next, state.You)) return false;
        foreach (var player in state.Teammates) if (!Same(sets, ref next, player)) return false;
        foreach (var enemy in state.Enemies) if (!ReferenceEquals(sets[next++], enemy.Powers)) return false;
        return true;
    }

    /// <param name="kinds">Gets each listener's kind, in step with the list, when given.</param>
    private static List<(SimModel Model, int Owner)> BuildListeners(SimState state, List<ListenerKind>? kinds)
    {
        var list = new List<(SimModel, int)>();
        void Add(IEnumerable<string> names, int owner, ListenerKind kind)
        {
            foreach (var name in names)
            {
                // A card's model listens only from a player's piles, and a power's or relic's never from there.
                if (SimModels.Get(name) is not { } model || (model is CardListener) != (kind == ListenerKind.Card)) continue;
                list.Add((model, owner));
                kinds?.Add(kind);
            }
        }
        foreach (var id in state.PlayerIds)
        {
            var player = state.Player(id)!;
            Add(player.Powers.Items.Select(entry => entry.Name.Name), id, ListenerKind.Power);
            Add(player.Relics.Items.Select(entry => entry.Name.Name), id, ListenerKind.Relic);
            // Their potions and orbs come here: none has a model (no potion's or orb's hook changes with its place in the order).
            Add(player.Cards.Items.Select(entry => entry.Name.Name), id, ListenerKind.Card);
        }
        foreach (var enemy in state.Enemies) Add(enemy.Powers.Items.Select(entry => entry.Name.Name), enemy.Id, ListenerKind.Power);
        return list;
    }

    private static readonly List<(SimModel Model, int Owner)> NoListeners = new();

    /// <summary>Whether any creature or player holds a modelled power, relic or card (most fights' cheap path: nothing to call).</summary>
    private static bool AnyModelled(SimState state)
    {
        static bool Any<TName>(NamedSet<TName> named) where TName : ISimName<TName>
        {
            foreach (var entry in named.Items) if (SimModels.Has(entry.Name.Name)) return true;
            return false;
        }
        static bool AnyOf(SimPlayer player) => Any(player.Powers) || Any(player.Relics) || Any(player.Cards);
        if (AnyOf(state.You)) return true;
        foreach (var player in state.Teammates) if (AnyOf(player)) return true;
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
            var listenerOwner = owner;
            states = Then(states, branch => call(model, listenerOwner, branch));
        }
        return states;
    }

    // ------------------------------------------------------------------ pipelines

    /// <summary>
    /// Hook.ModifyDamage for one hit: the base, the enchantment, every listener's additive term, then multipliers, late
    /// multipliers and the smallest cap, with the captured terms of unmodelled sources in their places; the game's int cast.
    /// </summary>
    public static int CalcDamage(SimState state, decimal baseDamage, DamageCtx damage, CapturedTerms? captured = null) => CalcDamage(state, baseDamage, damage, captured, modifiers: null);

    /// <param name="modifiers">Gets the modelled sources whose term changed the amount (the game's modifiers list), when given.</param>
    public static int CalcDamage(SimState state, decimal baseDamage, DamageCtx damage, CapturedTerms? captured, List<(SimModel Model, int Owner)>? modifiers)
    {
        var terms = captured ?? CapturedTerms.None;
        var num = baseDamage;
        num += terms.EnchantAdd;
        num *= terms.EnchantMul;
        var listeners = Listeners(state);
        foreach (var (model, owner) in listeners) { var term = model.ModifyDamageAdditive(state, owner, damage); num += term; if (term != 0m) modifiers?.Add((model, owner)); }
        num += terms.Add;
        foreach (var (model, owner) in listeners) { var term = model.ModifyDamageMultiplicative(state, owner, damage); num *= term; if (term != 1m) modifiers?.Add((model, owner)); }
        num *= terms.Mul;
        foreach (var (model, owner) in listeners) { var term = model.ModifyDamageMultiplicativeLate(state, owner, damage); num *= term; if (term != 1m) modifiers?.Add((model, owner)); }
        num *= terms.Late;
        var cap = terms.Cap;
        (SimModel, int)? capper = null;
        foreach (var (model, owner) in listeners) { var term = model.ModifyDamageCap(state, owner, damage); if (term < cap) { cap = term; capper = (model, owner); } }
        if (num > cap) { num = cap; if (capper is { } capping) modifiers?.Add(capping); }
        return Math.Max(0, (int)num);
    }

    /// <summary>
    /// CreatureCmd.Damage's first steps (SimPipelines): Hook.ModifyDamage (<see cref="CalcDamage(SimState, decimal, DamageCtx, CapturedTerms?)"/>),
    /// then AfterModifyingDamageAmount for each modelled source whose term changed the amount (Slow, Hard to Kill and Intangible
    /// flash), for every damage: a card's hits, a proc's, a card's damage that isn't an attack.
    /// </summary>
    internal static int ModifiedDamage(SimState state, decimal baseDamage, DamageCtx damage, CapturedTerms? captured)
    {
        if (Listeners(state).Count == 0) return CalcDamage(state, baseDamage, damage, captured);
        var modifiers = new List<(SimModel Model, int Owner)>();
        var amount = CalcDamage(state, baseDamage, damage, captured, modifiers);
        foreach (var (model, owner) in modifiers) model.AfterModifyingDamageAmount(state, owner, damage.Card);
        return amount;
    }

    /// <summary>Hook.ModifyBlock: the base, the enchantment, additive terms, then multipliers; the game's int cast.</summary>
    public static int CalcBlock(SimState state, decimal baseBlock, BlockCtx block, CapturedTerms? captured = null) => CalcBlock(state, baseBlock, block, captured, modifiers: null);

    public static int CalcBlock(SimState state, decimal baseBlock, BlockCtx block, CapturedTerms? captured, List<(SimModel Model, int Owner)>? modifiers)
    {
        var terms = captured ?? CapturedTerms.None;
        var num = baseBlock;
        num += terms.EnchantAdd;
        num *= terms.EnchantMul;
        var listeners = Listeners(state);
        foreach (var (model, owner) in listeners) { var term = model.ModifyBlockAdditive(state, owner, block); num += term; if (term != 0m) modifiers?.Add((model, owner)); }
        num += terms.Add;
        foreach (var (model, owner) in listeners) { var term = model.ModifyBlockMultiplicative(state, owner, block); num *= term; if (term != 1m) modifiers?.Add((model, owner)); }
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

    /// <summary>A power's amount down by one, gone at 0 (PowerCmd.Decrement).</summary>
    public static void DecrementPower(SimState state, int id, PowerId power)
    {
        var powers = PowersOf(state, id);
        if (!powers.Has(power)) return;
        SetPowers(state, id, powers[power] > 1 ? powers.With(power, powers[power] - 1, powers.DataOf(power)) : powers.Without(power));
    }
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
