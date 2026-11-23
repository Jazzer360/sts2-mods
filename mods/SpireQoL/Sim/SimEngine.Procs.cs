namespace SpireQoL.Sim;

/// <summary>
/// What proc models do (docs/SIMULATOR.md "Models"): gain block, deal damage, or only fire, each logged under the model's name
/// with the block or damage it caused (the play log the tabs and the audit read).
/// </summary>
public static partial class SimEngine
{
    /// <summary>
    /// Block for a player from a relic or power (CreatureCmd.GainBlock without a card, its hooks in SimPipelines), <paramref name="times"/>
    /// separate gains, each through the block pipeline (Dexterity and Frail only for powered block), AfterModifyingBlockAmount for the
    /// sources that changed it, then the gain (Juggernaut, Beacon of Hope) and AfterBlockGained. Logged with the block gained.
    /// </summary>
    public static IEnumerable<SimState> ProcBlock(SimState state, int owner, string name, decimal baseBlock, SimProps props, int times = 1, string? label = null) =>
        ProcBlockFor(state, owner, name, owner, baseBlock, props, times, label);

    /// <summary>The same, gained by another player (Covering Fire's block for your teammates), logged under its owner.</summary>
    public static IEnumerable<SimState> ProcBlockFor(SimState state, int owner, string name, int gainer, decimal baseBlock, SimProps props, int times = 1, string? label = null)
    {
        IEnumerable<SimState> states = new[] { state };
        if (!Who.IsPlayer(gainer)) return states;
        for (var i = 0; i < times; i++)
        {
            states = Then(states, branch =>
            {
                if (!branch.TakesPart(gainer)) return new[] { branch };
                var modifiers = Listeners(branch).Count > 0 ? new List<(SimModel Model, int Owner)>() : null;
                var amount = CalcBlock(branch, baseBlock, new BlockCtx(gainer, props, Card: null, Play: null), captured: null, modifiers);
                branch.LogFired(name, amount, owner);
                if (amount <= 0) return new[] { branch };
                if (modifiers != null) foreach (var (model, listenerOwner) in modifiers) model.AfterModifyingBlockAmount(branch, listenerOwner, amount, play: null);
                return Then(GainBlock(branch, amount, gainer == Who.You ? Breakdown(branch, owner, name, label) : null, gainer), copy => Hook(copy, nameof(SimModel.AfterBlockGained), (model, listenerOwner, simState) => model.AfterBlockGained(simState, listenerOwner, gainer, amount, card: null)));
            });
        }
        return states;
    }

    /// <summary>
    /// The name a source's block or damage gets in Incoming Damage's breakdown (SimState.EndOfTurnBlock, EndOfTurnDamage): your end of
    /// turn only, when the simulation keeps one; the game's name for it (or the label given).
    /// </summary>
    private static string? Breakdown(SimState state, int owner, string name, string? label) =>
        state.Log && state.EndOfTurnNow && owner == Who.You ? label ?? Title(state, owner, name) : null;

    /// <summary>A relic's or power's name as the game shows it (SimState.Titles), or its class name.</summary>
    public static string Title(SimState state, int owner, string name) => state.Titles.TryGetValue($"{owner}:{name}", out var title) ? title : name;

    /// <summary>A player's last orb is evoked and goes (OrbCmd.EvokeLast: Consuming Shadow).</summary>
    public static IEnumerable<SimState> EvokeLast(SimState state, int player)
    {
        var simPlayer = state.MutablePlayer(player);
        if (simPlayer.Orbs.Count == 0) return new[] { state };
        var orb = simPlayer.Orbs[^1];
        simPlayer.OwnOrbs().RemoveAt(simPlayer.Orbs.Count - 1);
        return Evoke(state, orb, player);
    }

    /// <summary>One proc hit on an enemy from <paramref name="dealer"/>: the damage pipeline (no card), then the damage. Returns the damage.</summary>
    private static int ProcHitOn(SimState state, int? dealer, SimEnemy enemy, decimal baseDamage, SimProps props)
    {
        if (!enemy.Alive) return 0;
        var amount = ModifiedDamage(state, baseDamage, new DamageCtx(dealer, enemy.Id, props, Card: null, Play: null), captured: null);
        if (amount > 0) Damage(state, enemy, amount, dealer, props);
        return amount;
    }

    /// <summary>Damage to every hittable enemy, <paramref name="times"/> times (Gravity, Letter Opener...). Logged with the total.</summary>
    public static IEnumerable<SimState> ProcDamageAll(SimState state, int owner, string name, decimal baseDamage, SimProps props, int times = 1, bool noDealer = false, string? label = null)
    {
        if (Breakdown(state, owner, name, label) is { } shown && baseDamage > 0 && state.Targetable.Any()) for (var i = 0; i < times; i++) state.EndOfTurnDamage.Add((shown + " (all)", (int)baseDamage));
        for (var i = 0; i < times; i++)
        {
            var total = 0;
            var any = false;
            foreach (var enemy in state.Targetable.ToList())
            {
                any = true;
                total += ProcHitOn(state, noDealer ? null : owner, enemy, baseDamage, props);
            }
            if (any) state.LogFired(name, total, owner);
        }
        AllyDeaths(state);
        return new[] { state };
    }

    /// <summary>Damage to a random hittable enemy, <paramref name="times"/> times: a branch per target each time. Logged per hit.</summary>
    public static IEnumerable<SimState> ProcDamageRandom(SimState state, int owner, string name, decimal baseDamage, SimProps props, int times = 1, bool noDealer = false, string? label = null)
    {
        if (Breakdown(state, owner, name, label) is { } shown && baseDamage > 0) for (var i = 0; i < times; i++) state.EndOfTurnDamage.Add((shown, (int)baseDamage));
        IEnumerable<SimState> states = new[] { state };
        for (var i = 0; i < times; i++)
            states = Then(states, branch => RandomTarget(branch, (copy, id) =>
            {
                copy.LogFired(name, ProcHitOn(copy, noDealer ? null : owner, copy.Enemy(id)!, baseDamage, props), owner);
                AllyDeaths(copy);
                return new[] { copy };
            }));
        return states;
    }

    /// <summary>Damage to one enemy (Strangle on its owner). Logged with the damage.</summary>
    public static IEnumerable<SimState> ProcDamageOn(SimState state, int owner, string name, int target, decimal baseDamage, SimProps props, int times = 1, bool noDealer = false)
    {
        for (var i = 0; i < times; i++)
        {
            if (state.Enemy(target) is not { Alive: true } enemy) break;
            state.LogFired(name, ProcHitOn(state, noDealer ? null : owner, enemy, baseDamage, props), owner);
        }
        AllyDeaths(state);
        return new[] { state };
    }

    /// <summary>
    /// Damage to a player from a creature's proc (Galvanic: the enemy's power hits you; Constrict: a debuff on you hits you), through
    /// the damage pipeline on that player, blocked unless Unblockable. Logged with the damage; <paramref name="label"/> names it
    /// in Incoming Damage's breakdown (default: the source's class name).
    /// </summary>
    public static IEnumerable<SimState> ProcDamagePlayer(SimState state, int owner, string name, int player, decimal baseDamage, SimProps props, bool noDealer = false, string? label = null)
    {
        var dealer = noDealer ? null : (int?)owner;
        var amount = CalcDamage(state, baseDamage, new DamageCtx(dealer, player, props, Card: null, Play: null));
        state.LogFired(name, amount, owner);
        if (amount <= 0) return new[] { state };
        if (props.HasFlag(SimProps.Unblockable)) LandUnblockable(state, label ?? name, amount, player, dealer, props);
        else LandBlockable(state, label ?? name, amount, player, dealer, props);
        return new[] { state };
    }

    /// <summary>Card events a proc causes (Dark Embrace's draw...): each through every listener, as an indirect event.</summary>
    /// <param name="onEach">Called for each event that happens (each card drawn: a full hand or empty piles give fewer), before
    /// its listeners: a draw or energy tab counting its source's (decision #132).</param>
    public static IEnumerable<SimState> ProcCardEvent(SimState state, CardEvent cardEvent, int count, int player, bool indirect = true, Action<SimState>? onEach = null)
    {
        // Your draws come from your draw pile (decision #90): which card matters.
        if (cardEvent == CardEvent.Drawn && DrawsStopped(state, player)) return new[] { state };
        if (cardEvent == CardEvent.Drawn && DrawsFromPile(state, player)) return DrawCards(state, player, count, indirect, onEach);
        IEnumerable<SimState> states = new[] { state };
        for (var i = 0; i < count; i++) states = Then(states, branch => { onEach?.Invoke(branch); return FireCardEvent(branch, cardEvent, player, indirect); });
        return states;
    }

    /// <summary>
    /// The amount a draw or energy tab shows (decision #132): <paramref name="source"/>'s count of what it gave, one per card drawn
    /// or energy gained, when the event is the kind its tab counts; null otherwise.
    /// </summary>
    internal static Action<SimState>? TallyFor(string source, TabKind? tab, CardEvent cardEvent, int owner) =>
        (tab, cardEvent) is (TabKind.Draw, CardEvent.Drawn) or (TabKind.Energy, CardEvent.EnergyGained)
            ? branch => branch.LogAmount(source, amount: 1, owner)
            : null;

    /// <summary>One card event's bookkeeping and its listeners (AfterCardEvent).</summary>
    internal static List<SimState> FireCardEvent(SimState state, CardEvent cardEvent, int player, bool indirect)
    {
        CountEvent(state, cardEvent, player);
        var was = state.IndirectNow;
        state.IndirectNow = indirect || was;
        var after = Hook(state, nameof(SimModel.AfterCardEvent), (model, owner, branch) => model.AfterCardEvent(branch, owner, cardEvent, player)).ToList();
        foreach (var branch in after) branch.IndirectNow = was;
        return after;
    }

    /// <summary>Bookkeeping of one card event: the play's event counts, and the player's energy.</summary>
    internal static void CountEvent(SimState state, CardEvent cardEvent, int player)
    {
        // The play's own events (Unceasing Top: a play that draws or creates nothing for its player); a teammate's draws and
        // cards (Huddle Up, Blade Symphony) aren't the player's.
        if (player == (state.CurrentPlay?.Owner ?? Who.You)) state.PlayEvents = state.PlayEvents.Add(cardEvent);
        if (!Who.IsPlayer(player)) return;
        var simPlayer = state.MutablePlayer(player);
        if (cardEvent == CardEvent.EnergySpent) simPlayer.Energy--;
        else if (cardEvent == CardEvent.EnergyGained) simPlayer.Energy++;
    }

    /// <summary>A player's turn counters (SimPlayer.Turn) by id.</summary>
    public static NamedCounts TurnOf(SimState state, int id) => state.Player(id)?.Turn ?? NamedCounts.Empty;
}
