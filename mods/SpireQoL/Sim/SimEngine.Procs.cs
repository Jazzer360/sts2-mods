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
    /// Block an enemy's power gives it (CreatureCmd.GainBlock(base.Owner) on an enemy: Sneaky's on your Attack), through the block math, while
    /// it lives: the enemy reacted with block (SimEnemy.ReactedWithBlock, part of its branch's key). Logged with no amount: an enemy's block
    /// is no resource of yours, so its tab is crossed, a trigger (decision #132).
    /// </summary>
    public static IEnumerable<SimState> EnemyBlock(SimState state, int enemy, string name, decimal baseBlock, SimProps props)
    {
        if (state.Enemy(enemy) is not { Alive: true } gainer) return new[] { state };
        var amount = CalcBlock(state, baseBlock, new BlockCtx(enemy, props, Card: null, Play: null));
        gainer.Block += amount;
        gainer.ReactedWithBlock = true;
        state.LogFired(name, amount: 0, enemy);
        return new[] { state };
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
    /// in Incoming Damage's breakdown (default: the source's class name). A hook's CreatureCmd.Damage passes no card (its cardSource is
    /// null): the one path for damage to a player (DamagePlayer, decision #219).
    /// </summary>
    public static IEnumerable<SimState> ProcDamagePlayer(SimState state, int owner, string name, int player, decimal baseDamage, SimProps props, bool noDealer = false, string? label = null)
    {
        DamagePlayer(state, label ?? name, player, baseDamage, props, noDealer ? null : owner, card: null, fired: (name, owner));
        return new[] { state };
    }

    /// <summary>
    /// Damage to <paramref name="player"/>'s Osty from a creature's proc (an enemy's Thorns as Osty attacks it), while he lives: CreatureCmd.Damage of
    /// him, so the damage math with him as its target; his player's block takes it first unless it's Unblockable (a pet's damage is blocked by its
    /// owner's block: GetDamageResults' target.PetOwner?.Creature, the blockRedirect result), the HP-loss pipeline on him, BeforeDamageReceived, his HP;
    /// then his result in the fight's log and told to the hooks (Necro Mastery's AfterCurrentHpChanged, AfterDamageGiven, AfterDamageReceived), and
    /// on beta the player's block result after it (DamageResults.AllResults: Reflect hits back what their block took); main has none. Logged with
    /// the damage, as every proc's is.
    /// </summary>
    public static IEnumerable<SimState> ProcDamageOsty(SimState state, int owner, string name, int player, decimal baseDamage, SimProps props, bool noDealer = false)
    {
        if (player != Who.You || state.Player(player) is not { OstyHp: > 0 }) return new[] { state };
        var dealer = noDealer ? (int?)null : owner;
        var damage = new DamageCtx(dealer, Who.YourOsty, props, Card: null, Play: null);
        var modifiers = Listeners(state).Count == 0 ? null : new List<(SimModel Model, int Owner)>();
        var amount = CalcDamage(state, baseDamage, damage, captured: null, modifiers);
        state.LogFired(name, amount, owner);
        if (modifiers != null) foreach (var (model, listener) in modifiers) model.AfterModifyingDamageAmount(state, listener, card: null);
        if (amount <= 0) return new[] { state };
        // Worked out first, then BeforeDamageReceived, then applied (beta, v0.114.0: CreatureCmd.GetDamageResults); main tells BeforeDamageReceived
        // first (MainRule.ReceivedBeforeTheResults).
        var receivedFirst = (state.MainRules & MainRule.ReceivedBeforeTheResults) != 0;
        if (receivedFirst) foreach (var (model, listener) in Listeners(state)) model.BeforeDamageReceived(state, listener, damage, amount);
        var blocked = props.HasFlag(SimProps.Unblockable) ? 0 : Math.Min(state.Player(player)!.Block, amount);
        var notes = default(HpLossNotes);
        var rest = amount - blocked;
        var lost = rest <= 0 ? 0 : Listeners(state).Count == 0 ? rest : Math.Max(0, (int)HpLossFor(state, damage, rest, out notes));
        if (!receivedFirst) foreach (var (model, listener) in Listeners(state)) model.BeforeDamageReceived(state, listener, damage, amount);
        if (blocked > 0)
        {
            var petOwner = state.MutablePlayer(player);
            petOwner.Block -= Math.Min(petOwner.Block, blocked);
        }
        TellHpLoss(state, notes);
        var hpBefore = state.Player(player)!.OstyHp;
        var ostyLost = OstyDrops(state, player, lost);
        RecordOstyHit(state, player, props, ostyLost);
        if (!receivedFirst && blocked > 0) RecordDamageReceived(state, player, props, lost: 0);
        TellOstyHit(state, player, dealer, props, card: null, ostyLost, killed: hpBefore > 0 && lost >= hpBefore);
        if (!receivedFirst && blocked > 0) PlayerDamaged(state, player, dealer, props, blocked, lost: 0, card: null);
        return new[] { state };
    }

    /// <summary>Card events a proc causes (Dark Embrace's draw...): each through every listener, as an indirect event.</summary>
    /// <param name="onEach">Called for each event that happens (each card drawn: a full hand or empty piles give fewer), before
    /// its listeners: a draw or energy tab counting its source's (decision #132).</param>
    /// <param name="card">The card each event is about, when known (a made card's class and type: Hook.AfterCardGeneratedForCombat's card).</param>
    public static IEnumerable<SimState> ProcCardEvent(SimState state, CardEvent cardEvent, int count, int player, bool indirect = true, Action<SimState>? onEach = null, SimCard? card = null, bool causedByEthereal = false)
    {
        // Your draws come from your draw pile (decision #90): which card matters.
        if (cardEvent == CardEvent.Drawn && DrawsStopped(state, player)) return DrawPrevented(state, player);
        if (cardEvent == CardEvent.Drawn && DrawsFromPile(state, player)) return DrawCards(state, player, count, indirect, onEach);
        IEnumerable<SimState> states = new[] { state };
        for (var i = 0; i < count; i++) states = Then(states, branch => { onEach?.Invoke(branch); return FireCardEvent(branch, cardEvent, player, indirect, card, causedByEthereal: causedByEthereal); });
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

    /// <summary>
    /// One card event's bookkeeping and its listeners (AfterCardEvent), about <paramref name="card"/> when it's known (SimState.EventCard
    /// while they run; a card event inside a listener's sets its own and gives the outer one back). A draw's card is its profile
    /// (<paramref name="drawn"/>, SimState.DrawnCard: from the draw pile; null, a draw whose card isn't read), never an outer draw's.
    /// </summary>
    internal static List<SimState> FireCardEvent(SimState state, CardEvent cardEvent, int player, bool indirect, SimCard? card = null, SimDrawCard? drawn = null, bool causedByEthereal = false)
    {
        var outerCard = state.EventCard;
        var outerDrawn = state.DrawnCard;
        state.EventCard = card;
        if (cardEvent == CardEvent.Drawn) state.DrawnCard = drawn;
        CountEvent(state, cardEvent, player);
        var was = state.IndirectNow;
        var outerEthereal = state.EtherealNow;
        state.IndirectNow = indirect || was;
        state.EtherealNow = causedByEthereal;
        var after = Hook(state, nameof(SimModel.AfterCardEvent), (model, owner, branch) => model.AfterCardEvent(branch, owner, cardEvent, player)).ToList();
        foreach (var branch in after)
        {
            branch.IndirectNow = was;
            branch.EtherealNow = outerEthereal;
            branch.EventCard = outerCard;
            branch.DrawnCard = outerDrawn;
        }
        return after;
    }

    /// <summary>Bookkeeping of one card event: the play's event counts, and the player's energy.</summary>
    internal static void CountEvent(SimState state, CardEvent cardEvent, int player)
    {
        // The play's own events (Unceasing Top: a play that draws or creates nothing for its player); a teammate's draws and
        // cards (Huddle Up, Blade Symphony) aren't the player's.
        if (player == (state.CurrentPlay?.Owner ?? Who.You))
        {
            state.PlayEvents = state.PlayEvents.Add(cardEvent);
            state.LogFired(SimKeys.PlayEvent(cardEvent));
        }
        if (!Who.IsPlayer(player)) return;
        RecordCardEvent(state, cardEvent, player); // the game's log has it before its After hook (decision #191)
        var simPlayer = state.MutablePlayer(player);
        if (cardEvent == CardEvent.Exhausted) simPlayer.ExhaustPile++;
        if (cardEvent == CardEvent.EnergySpent) simPlayer.Energy--;
        else if (cardEvent == CardEvent.EnergyGained) simPlayer.Energy++;
    }

    /// <summary>A player's turn counters (SimPlayer.Turn) by id.</summary>
    public static NamedCounts TurnOf(SimState state, int id) => state.Player(id)?.Turn ?? NamedCounts.Empty;
}
