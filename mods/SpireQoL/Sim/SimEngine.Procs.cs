namespace SpireQoL.Sim;

/// <summary>
/// What proc models do (docs/SIMULATOR_V2.md "Models"): gain block, deal damage, or only fire, each logged under the model's name
/// with the block or damage it caused (the play log the tabs and the audit read).
/// </summary>
public static partial class SimEngine
{
    /// <summary>
    /// Block for a player from a relic or power (CreatureCmd.GainBlock without a card), <paramref name="times"/> separate gains,
    /// each through the block pipeline (Dexterity and Frail only for powered block), then the gain (Juggernaut, Beacon of Hope)
    /// and AfterBlockGained. Logged with the block gained.
    /// </summary>
    public static IEnumerable<SimState> ProcBlock(SimState s, int owner, string name, decimal baseBlock, SimProps props, int times = 1, string? label = null) =>
        ProcBlockFor(s, owner, name, owner, baseBlock, props, times, label);

    /// <summary>The same, gained by another player (Covering Fire's block for your teammates), logged under its owner.</summary>
    public static IEnumerable<SimState> ProcBlockFor(SimState s, int owner, string name, int gainer, decimal baseBlock, SimProps props, int times = 1, string? label = null)
    {
        IEnumerable<SimState> r = new[] { s };
        if (!Who.IsPlayer(gainer)) return r;
        for (var i = 0; i < times; i++)
        {
            r = Then(r, x =>
            {
                if (!x.TakesPart(gainer)) return new[] { x };
                var amount = CalcBlock(x, baseBlock, new BlockCtx(gainer, props, null, null));
                x.LogFired(name, amount, owner);
                if (amount <= 0) return new[] { x };
                return Then(GainBlock(x, amount, gainer == Who.You ? Breakdown(x, owner, name, label) : null, gainer), y => Hook(y, nameof(SimModel.AfterBlockGained), (m, o, z) => m.AfterBlockGained(z, o, gainer, amount, null)));
            });
        }
        return r;
    }

    /// <summary>
    /// The name a source's block or damage gets in Incoming Damage's breakdown (SimState.EndOfTurnBlock, EndOfTurnDamage): your end of
    /// turn only, when the simulation keeps one; the game's name for it (or the label given).
    /// </summary>
    private static string? Breakdown(SimState s, int owner, string name, string? label) =>
        s.Log && s.EndOfTurnNow && owner == Who.You ? label ?? Title(s, owner, name) : null;

    /// <summary>A relic's or power's name as the game shows it (SimState.Titles), or its class name.</summary>
    public static string Title(SimState s, int owner, string name) => s.Titles.TryGetValue($"{owner}:{name}", out var t) ? t : name;

    /// <summary>A player's last orb is evoked and goes (OrbCmd.EvokeLast: Consuming Shadow).</summary>
    public static IEnumerable<SimState> EvokeLast(SimState s, int player)
    {
        var p = s.MutablePlayer(player);
        if (p.Orbs.Count == 0) return new[] { s };
        var orb = p.Orbs[^1];
        p.OwnOrbs().RemoveAt(p.Orbs.Count - 1);
        return Evoke(s, orb, player);
    }

    /// <summary>One proc hit on an enemy from <paramref name="dealer"/>: the damage pipeline (no card), then the damage. Returns the damage.</summary>
    private static int ProcHitOn(SimState s, int? dealer, SimEnemy e, decimal baseDamage, SimProps props)
    {
        if (!e.Alive) return 0;
        var amount = CalcDamage(s, baseDamage, new DamageCtx(dealer, e.Id, props, null, null));
        if (amount > 0) Damage(s, e, amount, dealer, props);
        return amount;
    }

    /// <summary>Damage to every hittable enemy, <paramref name="times"/> times (Gravity, Letter Opener...). Logged with the total.</summary>
    public static IEnumerable<SimState> ProcDamageAll(SimState s, int owner, string name, decimal baseDamage, SimProps props, int times = 1, bool noDealer = false, string? label = null)
    {
        if (Breakdown(s, owner, name, label) is { } shown && baseDamage > 0 && s.Targetable.Any()) for (var i = 0; i < times; i++) s.EndOfTurnDamage.Add((shown + " (all)", (int)baseDamage));
        for (var i = 0; i < times; i++)
        {
            var total = 0;
            var any = false;
            foreach (var e in s.Targetable.ToList())
            {
                any = true;
                total += ProcHitOn(s, noDealer ? null : owner, e, baseDamage, props);
            }
            if (any) s.LogFired(name, total, owner);
        }
        AllyDeaths(s);
        return new[] { s };
    }

    /// <summary>Damage to a random hittable enemy, <paramref name="times"/> times: a branch per target each time. Logged per hit.</summary>
    public static IEnumerable<SimState> ProcDamageRandom(SimState s, int owner, string name, decimal baseDamage, SimProps props, int times = 1, bool noDealer = false, string? label = null)
    {
        if (Breakdown(s, owner, name, label) is { } shown && baseDamage > 0) for (var i = 0; i < times; i++) s.EndOfTurnDamage.Add((shown, (int)baseDamage));
        IEnumerable<SimState> r = new[] { s };
        for (var i = 0; i < times; i++)
            r = Then(r, x => RandomTarget(x, (y, id) =>
            {
                y.LogFired(name, ProcHitOn(y, noDealer ? null : owner, y.Enemy(id)!, baseDamage, props), owner);
                AllyDeaths(y);
                return new[] { y };
            }));
        return r;
    }

    /// <summary>Damage to one enemy (Strangle on its owner). Logged with the damage.</summary>
    public static IEnumerable<SimState> ProcDamageOn(SimState s, int owner, string name, int target, decimal baseDamage, SimProps props, int times = 1, bool noDealer = false)
    {
        for (var i = 0; i < times; i++)
        {
            if (s.Enemy(target) is not { Alive: true } e) break;
            s.LogFired(name, ProcHitOn(s, noDealer ? null : owner, e, baseDamage, props), owner);
        }
        AllyDeaths(s);
        return new[] { s };
    }

    /// <summary>
    /// Damage to a player from a creature's proc (Galvanic: the enemy's power hits you; Constrict: a debuff on you hits you), through
    /// the damage pipeline on that player, blocked unless Unblockable. Logged with the damage; <paramref name="label"/> names it
    /// in Incoming Damage's breakdown (default: the source's class name).
    /// </summary>
    public static IEnumerable<SimState> ProcDamagePlayer(SimState s, int owner, string name, int player, decimal baseDamage, SimProps props, bool noDealer = false, string? label = null)
    {
        var dealer = noDealer ? null : (int?)owner;
        var amount = CalcDamage(s, baseDamage, new DamageCtx(dealer, player, props, null, null));
        s.LogFired(name, amount, owner);
        if (amount <= 0) return new[] { s };
        if (props.HasFlag(SimProps.Unblockable)) LandUnblockable(s, label ?? name, amount, player, dealer, props);
        else LandBlockable(s, label ?? name, amount, player, dealer, props);
        return new[] { s };
    }

    /// <summary>Card events a proc causes (Dark Embrace's draw...): each through every listener, as an indirect event.</summary>
    /// <param name="onEach">Called for each event that happens (each card drawn: a full hand or empty piles give fewer), before
    /// its listeners: a draw or energy tab counting its source's (decision #132).</param>
    public static IEnumerable<SimState> ProcCardEvent(SimState s, CardEvent ev, int count, int player, bool indirect = true, Action<SimState>? onEach = null)
    {
        // Your draws come from your draw pile (decision #90): which card matters.
        if (ev == CardEvent.Drawn && DrawsFromPile(s, player)) return DrawCards(s, player, count, indirect, onEach);
        IEnumerable<SimState> r = new[] { s };
        for (var i = 0; i < count; i++) r = Then(r, x => { onEach?.Invoke(x); return FireCardEvent(x, ev, player, indirect); });
        return r;
    }

    /// <summary>
    /// The amount a draw or energy tab shows (decision #132): <paramref name="source"/>'s count of what it gave, one per card drawn
    /// or energy gained, when the event is the kind its tab counts; null otherwise.
    /// </summary>
    internal static Action<SimState>? TallyFor(string source, TabKind? tab, CardEvent ev, int owner) =>
        (tab, ev) is (TabKind.Draw, CardEvent.Drawn) or (TabKind.Energy, CardEvent.EnergyGained)
            ? x => x.LogAmount(source, 1, owner)
            : null;

    /// <summary>One card event's bookkeeping and its listeners (AfterCardEvent).</summary>
    internal static IEnumerable<SimState> FireCardEvent(SimState x, CardEvent ev, int player, bool indirect)
    {
        CountEvent(x, ev, player);
        var was = x.IndirectNow;
        x.IndirectNow = indirect || was;
        var after = Hook(x, nameof(SimModel.AfterCardEvent), (m, o, y) => m.AfterCardEvent(y, o, ev, player)).ToList();
        foreach (var y in after) y.IndirectNow = was;
        return after;
    }

    /// <summary>Bookkeeping of one card event: the play's event counts, and the player's energy.</summary>
    internal static void CountEvent(SimState s, CardEvent ev, int player)
    {
        // The play's own events (Unceasing Top: a play that draws or creates nothing for its player); a teammate's draws and
        // cards (Huddle Up, Blade Symphony) aren't the player's.
        if (player == (s.CurrentPlay?.Owner ?? Who.You)) s.PlayEvents = s.PlayEvents.Add(ev);
        if (!Who.IsPlayer(player)) return;
        var p = s.MutablePlayer(player);
        if (ev == CardEvent.EnergySpent) p.Energy--;
        else if (ev == CardEvent.EnergyGained) p.Energy++;
    }

    /// <summary>A player's turn counters (SimPlayer.Turn) by id.</summary>
    public static PowerSet TurnOf(SimState s, int id) => s.Player(id)?.Turn ?? PowerSet.Empty;
}
