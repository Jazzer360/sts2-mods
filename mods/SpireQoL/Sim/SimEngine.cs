namespace SpireQoL.Sim;

/// <summary>The branches still possible after the simulation, with helpers for guarantees and the worst case.</summary>
public sealed class SimOutcome
{
    public List<SimState> Branches = new();
    /// <summary>
    /// The search stopped at its memory cap (decision #107): Branches are where it stopped, part-way through the turn, each with its
    /// share of the rolls. What's settled in them stands (an enemy's death: Chance's lasting); everything else is unknown.
    /// </summary>
    public bool Truncated;
    /// <summary>Which cap stopped it ("the memory cap", "the work cap"), or null.</summary>
    public string? CapHit;

    /// <summary>
    /// The chance something happens: exact when the search finished; otherwise from what's settled (Low) up to that plus every
    /// unknown branch (High). A branch with something not modelled (decision #92) is unknown too.
    /// </summary>
    /// <param name="lasting">Once it happens nothing undoes it (an enemy's death): a branch unknown past that point still counts it.</param>
    public SimChance Chance(Func<SimState, bool> happens, bool lasting = false)
    {
        if (Branches.Count == 0) return new SimChance(0, Truncated ? 1 : 0, false);
        bool Known(SimState b) => (b.Unmodelled == null && !Truncated) || (lasting && happens(b));
        var seen = Branches.Any(b => Known(b) && happens(b));
        var unknown = Branches.Where(b => !Known(b)).Sum(b => b.Weight);
        var total = Branches.Sum(b => b.Weight);
        if (total <= 0) return new SimChance(0, 0, seen);
        var p = Branches.Where(b => Known(b) && happens(b)).Sum(b => b.Weight) / total;
        return new SimChance(p, Math.Min(1, p + unknown / total), seen);
    }

    /// <summary>Some branch has something not modelled (decision #92): what's shown carries the ?.</summary>
    public bool AnyUnmodelled => Branches.Any(b => b.Unmodelled != null);

    /// <summary>Why, over the branches: the gaps to model.</summary>
    public IReadOnlyList<string> UnmodelledReasons => Branches.Where(b => b.Unmodelled != null).Select(b => b.Unmodelled!).Distinct().ToList();

    /// <summary>The chance the enemy dies before it acts.</summary>
    /// <remarks>A search stopped at a cap before the enemy turn (decision #107): an enemy already dead there counts (no revive comes
    /// before it acts), so the range's floor is the deaths already settled.</remarks>
    public SimChance DeadChanceRange(int id) => Chance(b => b.Enemy(id) is { DiedBeforeActing: true } || (Truncated && !b.EnemyTurnNow && b.Enemy(id) is { Alive: false, StartedAlive: true }), lasting: true);

    /// <summary>The chance the enemy turn kills you (nothing left to save you).</summary>
    public SimChance YouDieChance() => Chance(b => b.Died);

    /// <summary>
    /// The simulator's work, the same on every machine (the performance tests limit these, not time): steps applied to the
    /// branch set, branches stepped over all steps, state copies, the most copies one step made, and the most states held at once.
    /// </summary>
    public int Steps, Stepped, Copies, MaxStepCopies, PeakHeld;

    public SimState Worst => Branches.OrderByDescending(b => b.Taken).ThenByDescending(b => b.OstyTaken).First();
    public int TakenMin => Branches.Min(b => b.Taken);
    /// <summary>Teammate k's HP lost: the worst branch and the best.</summary>
    public int TeammateTakenMax(int k) => Branches.Max(b => b.Teammates[k].Taken);
    public int TeammateTakenMin(int k) => Branches.Min(b => b.Teammates[k].Taken);
    /// <summary>What teammate k's Osty takes: the worst branch and the best.</summary>
    public int TeammateOstyTakenMax(int k) => Branches.Max(b => b.Teammates[k].OstyTaken);
    public int TeammateOstyTakenMin(int k) => Branches.Min(b => b.Teammates[k].OstyTaken);
    /// <summary>Your Parrying Shield fires in every branch (the search finished) / in some branch.</summary>
    public bool ParryingShieldInAll => !Truncated && Branches.All(b => b.ParryingShieldFired);
    public bool ParryingShieldInAny => Branches.Any(b => b.ParryingShieldFired);
    /// <summary>Teammate k in the branch where they lose the most HP: their Incoming Damage.</summary>
    public SimPlayer TeammateWorst(int k) => Branches.Select(b => b.Teammates[k]).OrderByDescending(t => t.Taken).First();
    public int TakenMax => Branches.Max(b => b.Taken);
    public int OstyTakenMin => Branches.Min(b => b.OstyTaken);
    public int OstyTakenMax => Branches.Max(b => b.OstyTaken);

    /// <summary>Enemies the play itself kills (before your end of turn) in some branch / in every branch.</summary>
    public HashSet<int> KilledByPlayInAny = new();
    public HashSet<int> KilledByPlayInAll = new();
    /// <summary>Enemies that gain block from Curl Up, Sneaky or Skittish because of the play, in some branch.</summary>
    public HashSet<int> BlockReactionInAny = new();
    /// <summary>The play breaks some enemy's block in every branch (Hand Drill).</summary>
    public bool PlayBreaksBlockInEveryBranch;
    /// <summary>Block gains during the play (each sets off Juggernaut): the most over branches.</summary>
    public int PlayBlockGainsMax;

    /// <summary>Stunned during your turn (Burrowed, Flutter, Plow, Shriek) and alive when its turn comes, in every branch.</summary>
    public bool StunnedInAll(int id) => !Truncated && Branches.All(b => b.Enemy(id) is { StunnedThisTurn: true, DiedBeforeActing: false });
    public bool GambitInAll(int id) => !Truncated && Branches.All(b => b.Enemy(id) is { GambitTriggered: true });
    public bool SlipperyInAny(int id) => Branches.Any(b => b.Enemy(id) is { SlipperyTriggered: true });
    /// <summary>The play kills some enemy in every branch (not always the same one).</summary>
    public bool PlayKillsInEveryBranch;
    /// <summary>The play's biggest powered hit (blocked plus HP lost), in the branch where it's smallest (Mighty Wings).</summary>
    public int PlayBiggestHitMin;
    /// <summary>The play's HP loss of yours, in the branch where it's smallest (SimState.PlayHpReached, PlayHpLosses, PlayHpLost).</summary>
    public int PlayHpReachedMin, PlayHpLossesMin, PlayHpLostMin;
    /// <summary>What fired during the play (its log): the counts in every branch (smallest), and in some branch (largest).</summary>
    public PowerSet PlayFiredEvery = PowerSet.Empty, PlayFiredAny = PowerSet.Empty;
    /// <summary>The first hit of the card's attack on each enemy (name: the enemy id), the same in every branch; empty when it isn't.</summary>
    public PowerSet PlayFirstHits = PowerSet.Empty;

    /// <summary>The enemy is dead before it acts in every branch.</summary>
    public bool DeadInAll(int id) => !Truncated && Branches.All(b => b.Enemy(id) is { DiedBeforeActing: true }); // a death that happened stands, modelled past it or not
    public bool DeadInAny(int id) => Branches.Any(b => b.Enemy(id) is { DiedBeforeActing: true });
    /// <summary>The chance it dies before acting (NaN when the search stopped at its cap: unknown).</summary>
    public double DeadChance(int id)
    {
        if (Truncated) return double.NaN;
        var total = Branches.Sum(b => b.Weight);
        return total <= 0 ? double.NaN : Branches.Where(b => b.Enemy(id) is { DiedBeforeActing: true }).Sum(b => b.Weight) / total;
    }

    /// <summary>Damage instances through its block during your turn: fewest and most over branches.</summary>
    public int UnblockedMin(int id) => Branches.Min(b => b.Enemy(id)?.UnblockedInstances ?? 0);
    public int UnblockedMax(int id) => Branches.Max(b => b.Enemy(id)?.UnblockedInstances ?? 0);

    public bool FirstHitBlockedInAll(int id) => !Truncated && Branches.All(b => b.Enemy(id) is { FirstHitFullyBlocked: true });
    public bool FirstHitBlockedInAny(int id) => Branches.Any(b => b.Enemy(id) is { FirstHitFullyBlocked: true });

    /// <summary>Its HP when your end of turn is over: lowest and highest over branches.</summary>
    public int HpAtTurnEndMin(int id) => Branches.Min(b => b.Enemy(id)?.HpAtTurnEnd ?? 0);
    public int HpAtTurnEndMax(int id) => Branches.Max(b => b.Enemy(id)?.HpAtTurnEnd ?? 0);
}

/// <summary>
/// The outcome simulator: plays a card (optionally), your end of turn, then the enemy turn, in the
/// game's order, on plain state. Every random target is explored as a branch (enemies killed
/// earlier aren't targets any more), and branches that end up identical are merged. Predictions
/// read guarantees ("in every branch") for good outcomes and possibilities ("in any branch") or
/// the worst branch for bad ones.
///
/// Order (v0.111.0 decompile, CombatManager.DoTurnEnd and the hooks it calls):
///  1. BeforeSideTurnEnd: Orichalcum (if you had no block), Plating, Cloak Clasp, Ripple Basin;
///     Hailstorm, The Bomb, Screaming Flagon, Stone Calendar (all enemies).
///  2. Orb passives, front first (Lightning: random enemy; Frost: block; Dark: grows its evoke;
///     Glass: every enemy, 1 less per trigger).
///  3. Cards in hand: Burn/Decay... (blockable), Bad Luck/Regret (HP loss); Ethereal cards exhaust
///     (Feel No Pain block, Charon's Ashes all enemies, Forgotten Soul random enemy).
///  4. Debuffs on you (Constrict, Magic Bomb, Disintegration; Demise).
///  5. AfterSideTurnEnd: Consuming Shadow evokes (last orb first), Parrying Shield (random enemy).
///  6. Enemy turn, enemy by enemy: poison tick, block cleared (unless Barricade), then each hit:
///     your Thorns before it, your block, Osty, HP-loss effects, then Flame Barrier and Reflect.
/// Every block gain sets off Juggernaut on a random enemy.
/// </summary>
public static partial class SimEngine
{
    /// <summary>OrbQueue.maxCapacity: OrbCmd.AddSlots stops here.</summary>
    public const int MaxOrbSlots = 10;

    /// <summary>This card (none: just end the turn), then the end of the turn and the enemy turn, from the board as it is (decision #28).</summary>
    public static SimOutcome Run(SimState start, SimPlay? play = null) => Run(start, play == null ? null : SimScript.Of(play), null);

    public static SimOutcome Run(SimState start, SimScript play) => Run(start, play, null);

    /// <summary>
    /// One search (decision #107): every step advances all branches together, and branches that reach the same state merge
    /// (adding their shares of the rolls), so the work grows with the distinct states, not the orders of random targets. Bounded by
    /// the memory cap (SimWork.Hold): past it the search stops where it is, and the outcome is Truncated. <paramref name="work"/>
    /// carries the cancellation and the background gate (SimJob).
    /// </summary>
    internal static SimOutcome Run(SimState start, SimScript? play, SimWork? work)
    {
        work ??= new SimWork();
        var first = start.Clone();
        first.Work = work;
        var b = new BranchSet(first);
        var aliveAtStart = start.Enemies.Where(e => e.Alive).Select(e => e.Id).ToList();
        SimOutcome? outcome = null;
        try
        {
            try
            {
                if (play != null) RunScript(b, play);
                // The play is over in every branch: its own results are exact from here, even if the end of turn hits the cap.
                outcome = PlayResults(b.States, start, aliveAtStart, complete: true);
                AutoPostPlay(b);
                EndOfTurn(b);
                b.Do(EnemyTurn);
            }
            catch (SimCapped)
            {
                // Stopped at a cap: the branches where it stopped, each part-way through (BranchSet keeps them whole).
                outcome ??= PlayResults(b.States, start, aliveAtStart, complete: false);
                outcome.Truncated = true;
                outcome.CapHit = work.CapHit ?? "the work cap";
            }
            outcome.Branches = Detached(b.States);
            work.EndStep();
            outcome.Steps = work.Steps;
            outcome.Stepped = work.Stepped;
            outcome.Copies = work.Copies;
            outcome.MaxStepCopies = work.MaxStepCopies;
            outcome.PeakHeld = work.PeakHeld;
            return outcome;
        }
        finally
        {
            work.Release();
        }
    }

    /// <summary>
    /// The play's own results over its branches (kills, block breaks and gains, reactions, its log). Not <paramref name="complete"/>
    /// (the search stopped before the play was over): only what can happen in some branch, no guarantees.
    /// </summary>
    private static SimOutcome PlayResults(List<SimState> states, SimState start, List<int> aliveAtStart, bool complete)
    {
        // Kills during the play itself (its hits, orbs, procs, Juggernaut), for kill-reactive tabs.
        var killedPerBranch = states.Select(s => aliveAtStart.Where(id => s.Enemy(id) is not { Alive: true }).ToHashSet()).ToList();
        // Enemies that gained block from Curl Up, Sneaky or Skittish because of the play, in some branch.
        var reacted = states.SelectMany(s => s.Enemies.Where(e => e.ReactedWithBlock).Select(e => e.Id)).ToHashSet();
        // Some enemy's block broken by the play, in every branch (Hand Drill).
        var breaksBlock = complete && states.All(s => s.Enemies.Any(e => e.BlockBroken && start.Enemy(e.Id) is { BlockBroken: false }));
        // Block gains during the play (each sets off Juggernaut).
        var gains = states.Select(s => s.P.BlockGains - start.P.BlockGains).ToList();
        int PlayMin(Func<SimState, int> f) => !complete || states.Count == 0 ? 0 : states.Min(f);
        // The play's log over its branches: in every branch (each merged one keeps its smallest), and in some branch.
        var firedEvery = !complete || states.Count == 0 ? PowerSet.Empty
            : states.Skip(1).Aggregate(states[0].Fired, (acc, x) => SimState.MergeLog(acc, x.Fired, every: true));
        var firedAny = states.Aggregate(PowerSet.Empty, (acc, x) => SimState.MergeLog(acc, x.FiredAny, every: false));
        // The first hit of the card's attack on each enemy, when nothing random came before it (the damage-formula audit).
        var firstHits = complete && states.Count > 0 && states.All(x => ReferenceEquals(x.FirstHits, states[0].FirstHits) || x.FirstHits.Items.AsSpan().SequenceEqual(states[0].FirstHits.Items)) ? states[0].FirstHits : PowerSet.Empty;
        return new SimOutcome
        {
            KilledByPlayInAny = killedPerBranch.SelectMany(k => k).ToHashSet(),
            KilledByPlayInAll = !complete || killedPerBranch.Count == 0
                ? new HashSet<int>()
                : killedPerBranch.Skip(1).Aggregate(new HashSet<int>(killedPerBranch[0]), (acc, k) => { acc.IntersectWith(k); return acc; }),
            PlayKillsInEveryBranch = complete && killedPerBranch.Count > 0 && killedPerBranch.All(k => k.Count > 0),
            BlockReactionInAny = reacted,
            PlayBreaksBlockInEveryBranch = breaksBlock,
            PlayBlockGainsMax = gains.Count > 0 ? gains.Max() : 0,
            PlayBiggestHitMin = PlayMin(s => s.PlayBiggestHit),
            PlayHpReachedMin = PlayMin(s => s.PlayHpReached),
            PlayHpLossesMin = PlayMin(s => s.PlayHpLosses),
            PlayHpLostMin = PlayMin(s => s.PlayHpLost),
            PlayFiredEvery = firedEvery,
            PlayFiredAny = firedAny,
            PlayFirstHits = firstHits,
        };
    }

    // ------------------------------------------------------------------ branching

    /// <summary>
    /// Chains a branching effect inside one step (Juggernaut on each block gain Beacon of Hope shares,
    /// Forgotten Soul after Feel No Pain): identical results merge, as between steps.
    /// </summary>
    private static List<SimState> Then(IEnumerable<SimState> states, Func<SimState, IEnumerable<SimState>> f)
    {
        var next = new List<SimState>();
        var seen = new Dictionary<long, SimState>();
        foreach (var x in states)
            foreach (var r in f(x))
                Merge(next, seen, r);
        return next;
    }

    /// <summary>Adds a branch to a step's results, or merges it into the identical one there (both shares of the rolls).</summary>
    private static void Merge(List<SimState> next, Dictionary<long, SimState> seen, SimState r)
    {
        var key = r.Key();
        if (seen.TryGetValue(key, out var same))
        {
            same.Weight += r.Weight;
            same.MergeResults(r); // not in the key: the guarantee is the smallest
            return;
        }
        seen[key] = r;
        next.Add(r);
    }

    /// <summary>The search stopped at the memory cap (SimWork.Hold): Run keeps the branches where it stopped.</summary>
    internal sealed class SimCapped : Exception
    {
    }

    private sealed class BranchSet
    {
        public List<SimState> States;
        /// <summary>Inside another branch set's step (an auto-play's own play): the outer set counts what's held.</summary>
        private readonly bool _nested;

        public BranchSet(SimState s, bool nested = false)
        {
            States = new List<SimState> { s };
            _nested = nested;
        }

        private SimWork? Work => States.Count > 0 ? States[0].Work : null;

        private void Count()
        {
            if (Work is { } w) { w.StartStep(); w.Steps++; w.Stepped += States.Count; }
        }

        /// <summary>
        /// Applies a step that may branch, merging identical results. The memory cap is checked between branches, the work cap at
        /// every copy: when one is hit, the branches stepped so far and those not yet stepped are kept as they are (every share of
        /// the rolls stays; a branch stopped part-way is kept as far as it got, with its own share), and the search stops (SimCapped).
        /// </summary>
        public void Each(Func<SimState, IEnumerable<SimState>> step)
        {
            Count();
            var work = _nested ? null : Work;
            var next = new List<SimState>();
            var seen = new Dictionary<long, SimState>();
            var mine = new List<SimState>();
            for (var i = 0; i < States.Count; i++)
            {
                var parent = States[i];
                var weight = parent.Weight;
                mine.Clear();
                try
                {
                    foreach (var r in step(parent)) mine.Add(r);
                }
                catch (SimCapped) when (!_nested)
                {
                    // The work cap, part-way through this branch: it stays as far as it got (what happened in it stands), its children go.
                    parent.Weight = weight;
                    Stop(next, i);
                }
                foreach (var r in mine) Merge(next, seen, r);
                if (work != null && !work.Hold(next.Count + States.Count)) Stop(next, i + 1);
            }
            States = next;
            work?.Hold(next.Count);
        }

        /// <summary>The search stops here: what's stepped and every branch from <paramref name="from"/> on not yet stepped.</summary>
        private void Stop(List<SimState> next, int from)
        {
            if (Work is { } w) w.CapHit ??= "the work cap";
            next.AddRange(States.Skip(from));
            States = next;
            throw new SimCapped();
        }

        public void Do(Action<SimState> step)
        {
            Count();
            foreach (var s in States) step(s);
        }
    }

    // ------------------------------------------------------------------ primitives

    /// <summary>
    /// Damage to an enemy (CreatureCmd.Damage), in the game's order: the damage caps (Hard to Kill, Intangible: the damage math
    /// applies them, amounts given as final numbers get them here), BeforeDamageReceived (Thorns hits the dealer back), its
    /// block (AfterBlockBroken when it breaks: Burrowed), the HP-loss pipeline (Hardened Shell, Slippery, The Boot...), its HP,
    /// then AfterDamageGiven and AfterDamageReceived (Envenom, Mighty Wings, Asleep, Slumber, Plow, Shriek, Flutter, The
    /// Gambit...), even when its block took it all. Returns the HP it lost (overkill included).
    /// </summary>
    /// <param name="dealer">Who deals it (you by default; Who.YourOsty for Osty's hits).</param>
    /// <param name="props">The damage's props: a card's hit is a powered Move; procs, orbs and the like are Unpowered.</param>
    internal static int Damage(SimState s, SimEnemy e, int amount, int? dealer = Who.You, SimProps props = SimProps.Unpowered, SimCard? card = null)
    {
        if (amount <= 0 || !e.Alive) return 0;
        var listeners = Listeners(s);
        if (!AnyListensToDamage(listeners)) return PlainDamage(s, e, amount, dealer, props);
        var ctx = new DamageCtx(dealer, e.Id, props, card, s.CurrentPlay);
        foreach (var (m, o) in listeners) amount = (int)Math.Min(amount, m.ModifyDamageCap(s, o, ctx));
        foreach (var (m, o) in listeners) m.BeforeDamageReceived(s, o, ctx, amount);
        if (!e.Alive) return 0;
        var hadBlock = e.Block > 0;
        var absorbed = props.HasFlag(SimProps.Unblockable) ? 0 : Math.Min(e.Block, amount);
        e.Block -= absorbed;
        if (hadBlock && e.Block == 0)
        {
            e.BlockBroken = true;
            foreach (var (m, o) in Listeners(s)) m.AfterBlockBroken(s, o, e.Id);
        }
        var hpBefore = e.Hp;
        var toHp = 0;
        if (amount - absorbed > 0)
        {
            toHp = Math.Max(0, (int)HpLossPipeline(s, ctx, amount - absorbed));
            e.Hp -= toHp;
            if (toHp > 0)
            {
                e.UnblockedInstances++; // UnblockedDamage is 0 when Hardened Shell stops it
            }
        }
        var ev = new DamageEvent(dealer, e.Id, props, card, absorbed + Math.Min(toHp, Math.Max(0, hpBefore)), absorbed, toHp, hpBefore > 0 && e.Hp <= 0);
        if (props.IsPoweredAttack() && dealer is Who.You or Who.YourOsty) BiggestHit(s, ev.Total);
        foreach (var (m, o) in Listeners(s)) m.AfterDamageGiven(s, o, ev);
        foreach (var (m, o) in Listeners(s)) m.AfterDamageReceived(s, o, ev);
        return toHp;
    }

    private static bool AnyListensToDamage(List<(SimModel Model, int Owner)> listeners)
    {
        foreach (var (m, _) in listeners) if (m.ListensToDamage) return true;
        return false;
    }

    /// <summary>Damage with no models listening (most fights): block, then HP; nothing else can react.</summary>
    private static int PlainDamage(SimState s, SimEnemy e, int amount, int? dealer, SimProps props)
    {
        var hadBlock = e.Block > 0;
        var absorbed = props.HasFlag(SimProps.Unblockable) ? 0 : Math.Min(e.Block, amount);
        e.Block -= absorbed;
        if (hadBlock && e.Block == 0) e.BlockBroken = true;
        var hpBefore = e.Hp;
        var toHp = amount - absorbed;
        if (toHp > 0)
        {
            e.Hp -= toHp;
            e.UnblockedInstances++;
        }
        else toHp = 0;
        if (props.IsPoweredAttack() && dealer is Who.You or Who.YourOsty) BiggestHit(s, absorbed + Math.Min(toHp, Math.Max(0, hpBefore)));
        return toHp;
    }

    /// <summary>
    /// Hook.ModifyHpLost*: every listener's BeforeOsty, BeforeOstyLate, AfterOsty, then AfterOstyLate term; a listener that
    /// changes the (whole-number) amount gets AfterModifyingHpLost (it flashes).
    /// </summary>
    internal static decimal HpLossPipeline(SimState s, DamageCtx ctx, decimal amount)
    {
        var listeners = Listeners(s);
        var changed = new List<(SimModel Model, int Owner)>();
        void Phase(Func<SimModel, int, decimal, decimal> f)
        {
            foreach (var (m, o) in listeners)
            {
                var before = amount;
                amount = f(m, o, amount);
                if (decimal.Truncate(before) != decimal.Truncate(amount)) changed.Add((m, o));
            }
        }
        Phase((m, o, a) => m.ModifyHpLostBeforeOsty(s, o, ctx, a));
        Phase((m, o, a) => m.ModifyHpLostBeforeOstyLate(s, o, ctx, a));
        Phase((m, o, a) => m.ModifyHpLostAfterOsty(s, o, ctx, a));
        Phase((m, o, a) => m.ModifyHpLostAfterOstyLate(s, o, ctx, a));
        foreach (var (m, o) in changed) m.AfterModifyingHpLost(s, o);
        return amount;
    }

    internal static void Stun(SimEnemy e)
    {
        e.Stunned = true;
        e.StunnedThisTurn = true;
    }

    /// <summary>
    /// A powered hit of yours or Osty's: its DamageResult.TotalDamage (blocked plus unblocked, which stops at the HP it had: the
    /// rest is overkill) for the Mighty Wings tab, and the legacy count of 30+ hits (SimPlay.BigHitThreshold).
    /// </summary>
    private static void BiggestHit(SimState s, int total)
    {
        if (total > s.PlayBiggestHit) s.PlayBiggestHit = total;
    }

    /// <summary>One hit on a random living enemy: a branch per possible target.</summary>
    private static IEnumerable<SimState> HitRandom(SimState s, Func<SimEnemy, int> amount, Action<SimState, SimEnemy>? then = null)
    {
        void Hit(SimState st, SimEnemy e)
        {
            Damage(st, e, amount(e));
            then?.Invoke(st, e);
        }
        var targets = s.Targetable.Select(e => e.Id).ToList();
        if (targets.Count == 0)
        {
            yield return s;
            yield break;
        }
        if (targets.Count == 1)
        {
            Hit(s, s.Enemy(targets[0])!);
            yield return s;
            yield break;
        }
        foreach (var id in targets)
        {
            var c = s.Clone();
            c.Weight = s.Weight / targets.Count;
            Hit(c, c.Enemy(id)!);
            yield return c;
        }
    }

    /// <summary>Unpowered damage from you to every hittable enemy (orbs, procs), each its own amount.</summary>
    private static void HitAll(SimState s, Func<SimEnemy, int> amount)
    {
        foreach (var e in s.Targetable.ToList()) Damage(s, e, amount(e));
    }

    /// <summary>Gaining block (amount &gt; 0) sets off Juggernaut on a random enemy.</summary>
    /// <param name="who">-1: you; k: teammate k.</param>
    private static IEnumerable<SimState> GainBlock(SimState s, int amount, string? log, int who = -1)
    {
        if (amount <= 0) return new[] { s };
        var p = who < 0 ? s.P : s.MutableTeammate(who);
        p.Block += amount;
        p.BlockGains++;
        if (who >= 0) p.BlockGained += amount;
        if (who < 0 && log != null) if (s.Log) s.EndOfTurnBlock.Add((log, amount));
        IEnumerable<SimState> r = new[] { s };
        if (p.Juggernaut > 0)
        {
            var j = p.Juggernaut;
            if (who < 0 && log != null) if (s.Log) s.EndOfTurnDamage.Add(("Juggernaut", j));
            var jOwner = who < 0 ? Who.You : Who.Teammate(who);
            r = HitRandom(s, _ => j, then: (st, _) => st.LogFired("JuggernautPower", j, jOwner));
        }
        // Beacon of Hope: half (whole block only) to every other living player; theirs can pass half of
        // that on once (the giver's is held while it gives, and the giver still gets block back).
        if (p.BeaconOfHope && !p.BeaconGiving && amount / 2 >= 1) r = Then(r, x => ShareBlock(x, who, amount / 2));
        return r;
    }

    private static IEnumerable<SimState> ShareBlock(SimState s, int giver, int half)
    {
        (giver < 0 ? s.P : s.MutableTeammate(giver)).BeaconGiving = true;
        IEnumerable<SimState> r = new[] { s };
        foreach (var other in PlayerOrder(s))
        {
            if (other == giver || (other >= 0 && s.Teammates[other].Hp <= 0)) continue;
            var o = other;
            r = Then(r, x => GainBlock(x, half, o < 0 ? "Beacon of Hope" : null, o));
        }
        return r.Select(x =>
        {
            (giver < 0 ? x.P : x.MutableTeammate(giver)).BeaconGiving = false;
            return x;
        }).ToList();
    }

    /// <summary>Players in player order: teammate indexes, with -1 (you) at your place.</summary>
    private static IEnumerable<int> PlayerOrder(SimState s)
    {
        var before = Math.Min(s.TeammatesBeforeMe, s.Teammates.Count);
        for (var k = 0; k < before; k++) yield return k;
        yield return -1;
        for (var k = before; k < s.Teammates.Count; k++) yield return k;
    }

    /// <param name="thunder">The orb owner's Thunder (yours when null).</param>
    /// <param name="mine">Your orb: a teammate's Frost blocks for them, not you.</param>
    private static IEnumerable<SimState> Evoke(SimState s, SimOrb orb, int? thunder = null, int who = -1)
    {
        // An evoke at your end of turn (Consuming Shadow) is in the breakdown with the orbs' passives: its block is what the forecast
        // check compares with the block the game gave, its damage what the forecast log lists.
        var listed = s.Log && s.EndOfTurnNow && who < 0;
        switch (orb.Kind)
        {
            // Thunder hits the evoke's target again if it's still alive.
            case OrbKind.Lightning:
                if (listed && s.Targetable.Any()) s.EndOfTurnDamage.Add(("Lightning (evoked)", orb.Evoke));
                return HitRandom(s, _ => orb.Evoke, then: (st, e) => { var t = thunder ?? st.P.Thunder; if (t > 0 && e.Alive) { Damage(st, e, t); st.LogFired("ThunderPower", t, who < 0 ? Who.You : Who.Teammate(who)); } });
            case OrbKind.Frost: return GainBlock(s, orb.Evoke, listed ? "Frost (evoked)" : null, who);
            case OrbKind.Dark:
            {
                var weakest = s.Targetable.OrderBy(e => e.Hp).FirstOrDefault();
                if (listed && weakest != null) s.EndOfTurnDamage.Add(("Dark (evoked)", orb.Evoke));
                if (weakest != null) Damage(s, weakest, orb.Evoke);
                return new[] { s };
            }
            case OrbKind.Glass:
                if (listed && s.Targetable.Any()) s.EndOfTurnDamage.Add(("Glass (evoked, all)", orb.Evoke));
                HitAll(s, _ => orb.Evoke);
                return new[] { s };
            default:
                return new[] { s };
        }
    }

    /// <summary>
    /// HP a player loses from <paramref name="amount"/> past their block (who: -1 you, k teammate k): the HP-loss pipeline
    /// (Tungsten Rod, Beating Remnant, Buffer, Intangible...). Apply it, then call PlayerDamaged.
    /// </summary>
    private static int HpLoss(SimState s, int who, int amount, int? dealer, SimProps props)
    {
        if (amount <= 0) return 0;
        var id = who < 0 ? Who.You : Who.Teammate(who);
        if (Listeners(s).Count == 0) return amount;
        return Math.Max(0, (int)HpLossPipeline(s, new DamageCtx(dealer, id, props, s.CurrentPlay?.Card, s.CurrentPlay), amount));
    }

    /// <summary>After a player took damage (their HP already lower): AfterDamageGiven and AfterDamageReceived (Beating Remnant counts, Inferno...).</summary>
    private static void PlayerDamaged(SimState s, int who, int? dealer, SimProps props, int blocked, int lost)
    {
        if (blocked <= 0 && lost <= 0) return;
        var listeners = Listeners(s);
        if (listeners.Count == 0) return;
        var ev = new DamageEvent(dealer, who < 0 ? Who.You : Who.Teammate(who), props, s.CurrentPlay?.Card, blocked + lost, blocked, lost, false);
        foreach (var (m, o) in listeners) m.AfterDamageGiven(s, o, ev);
        foreach (var (m, o) in Listeners(s)) m.AfterDamageReceived(s, o, ev);
    }

    /// <summary>Counts HP loss of yours for the play's HP-loss tabs (SimState.PlayHpReached...): <paramref name="reaching"/> got
    /// past your block to the HP-loss modifiers, <paramref name="lost"/> is what you lost after them.</summary>
    private static void PlayHp(SimState s, int reaching, int lost)
    {
        if (reaching > 0) s.PlayHpReached++;
        if (lost <= 0) return;
        s.PlayHpLosses++;
        s.PlayHpLost += lost;
    }

    /// <summary>A player's state to change (who: -1 you, k teammate k).</summary>
    private static SimPlayer Mutable(SimState s, int who) => who < 0 ? s.P : s.MutableTeammate(who);

    /// <summary>
    /// Blockable damage to a player (who: -1 you, k teammate k): their block, then HP loss; Unpowered unless said. The same for
    /// every player (decision #93); yours also feeds the breakdown, Imbalanced's threshold and your play's HP-loss tabs.
    /// </summary>
    private static void LandBlockable(SimState s, string name, int amount, int who = -1, int? dealer = null, SimProps props = SimProps.Unpowered)
    {
        if (amount <= 0) return;
        var p = Mutable(s, who);
        p.Incoming += amount;
        if (who < 0)
        {
            s.BlockDemand += amount;
            if (s.Log) s.Blockable.Add((name, amount));
        }
        var blocked = Math.Min(p.Block, amount);
        p.Block -= blocked;
        p.Through += amount - blocked;
        var lost = HpLoss(s, who, amount - blocked, dealer, props);
        Lose(s, who, lost);
        if (who < 0) PlayHp(s, amount - blocked, lost);
        PlayerDamaged(s, who, dealer, props, blocked, lost);
    }

    /// <summary>
    /// Explosive (ExplosivePower.AfterDeath): an enemy holding it that died since the last check damages every living
    /// player, blocked by the block they have then. Runs where deaths are handled (AllyDeaths) and after each
    /// end-of-turn step, so the block it meets is the block at that point.
    /// </summary>
    /// <summary>An end-of-turn step's explosions, only in fights with an Explosive enemy (no extra step otherwise).</summary>
    private static void ExplosionCheck(BranchSet b)
    {
        if (b.States.Any(s => s.Enemies.Any(e => !e.Exploded && (e.Explosive > 0 || e.ExplosiveOnTeammates != null)))) b.Do(s => Explosions(s));
    }

    private static void Explosions(SimState s)
    {
        foreach (var e in s.Enemies)
        {
            if (e.Alive || e.Exploded || !e.StartedAlive || (e.Explosive <= 0 && e.ExplosiveOnTeammates == null)) continue;
            e.Exploded = true;
            if (s.P.Hp > 0) LandBlockable(s, e.Name + " Explosive", e.Explosive, dealer: e.Id);
            for (var k = 0; k < s.Teammates.Count; k++)
            {
                if (s.Teammates[k].Hp <= 0) continue;
                var amount = e.ExplosiveOnTeammates is { } on && k < on.Count ? on[k] : e.Explosive;
                LandBlockable(s, e.Name + " Explosive", amount, k, e.Id);
            }
        }
    }

    /// <summary>Damage to every hittable enemy (Inferno and the like: Unpowered, their block first), dealt by <paramref name="dealer"/>.</summary>
    internal static void DamageAllEnemies(SimState s, int amount, int dealer)
    {
        foreach (var e in s.Targetable.ToList()) Damage(s, e, amount, dealer);
    }

    /// <summary>
    /// A player loses HP (after block and the HP-loss modifiers). Taken counts it all; their HP, when tracked, goes down, and
    /// at 0 something prevents death if it can (CreatureCmd.Kill → Hook.ShouldDie: Fairy in a Bottle first, then
    /// ShouldDieLate: Lizard Tail), healing them from 0; the rest of that hit is lost. With nothing left, they die. The same
    /// for every player, with their own potions and relics (decision #93).
    /// </summary>
    private static void Lose(SimState s, int who, int lost)
    {
        var p = Mutable(s, who);
        p.Taken += lost;
        if (lost <= 0 || p.Hp == int.MaxValue || p.Died) return;
        p.Hp -= lost;
        if (p.Hp > 0) return;
        if (p.Fairies > 0)
        {
            p.Fairies--;
            p.FairiesUsed++;
            p.Hp = p.FairyHeal;
        }
        else if (p.LizardTailHeal > 0)
        {
            p.Hp = p.LizardTailHeal;
            p.LizardTailHeal = 0;
            p.LizardTailUsed = true;
        }
        else
        {
            p.Hp = 0;
            p.Died = true;
        }
    }

    private static void LandUnblockable(SimState s, string name, int amount, int who = -1, int? dealer = null, SimProps props = SimProps.Unblockable | SimProps.Unpowered)
    {
        if (amount <= 0) return;
        if (who < 0 && s.Log) s.Unblockable.Add((name, amount));
        var lost = HpLoss(s, who, amount, dealer, props);
        Lose(s, who, lost);
        Mutable(s, who).UnblockableTaken += lost;
        if (who < 0) PlayHp(s, amount, lost);
        PlayerDamaged(s, who, dealer, props, 0, lost);
    }

    // ------------------------------------------------------------------ card play

    /// <summary>A script on its own (the state right after it), for tests of v2 plays.</summary>
    public static void RunScriptTo(SimState s, SimScript script, out List<SimState> branches, out bool truncated)
    {
        var b = Scratch(s, out var work);
        truncated = Capped(work, () => RunScript(b, script));
        branches = Detached(b.States);
    }

    /// <summary>This play on its own: the state right after it resolves, per branch.</summary>
    public static void ApplyPlayTo(SimState s, SimPlay play, out List<SimState> branches, out bool truncated)
    {
        var b = Scratch(s, out var work);
        truncated = Capped(work, () => ApplyPlay(b, play));
        branches = Detached(b.States);
    }

    /// <summary>
    /// The search is over: its branches stop counting against its work (a copy made from them later, by a check or a test, is
    /// nobody's, and never meets the search's caps).
    /// </summary>
    private static List<SimState> Detached(List<SimState> states)
    {
        foreach (var s in states) s.Work = null;
        return states;
    }

    /// <summary>A branch set of its own for a script on its own (RunScriptTo, ApplyPlayTo).</summary>
    private static BranchSet Scratch(SimState s, out SimWork work)
    {
        var first = s.Clone();
        first.Work = work = new SimWork();
        return new BranchSet(first);
    }

    /// <summary>Runs <paramref name="f"/> to the end or the memory cap (true), then frees what it held.</summary>
    private static bool Capped(SimWork work, Action f)
    {
        try
        {
            f();
            return false;
        }
        catch (SimCapped)
        {
            return true;
        }
        finally
        {
            work.Release();
        }
    }

    private static void ApplyPlay(BranchSet b, SimPlay p) => RunScript(b, SimScript.Of(p));

    /// <summary>Runs a script's actions in order on the branch set.</summary>
    private static void RunScript(BranchSet b, SimScript script)
    {
        foreach (var a in script.Actions) Run(b, a);
    }

    private static void Run(BranchSet b, SimAction a)
    {
        switch (a)
        {
            case FixedHitsAction fh: RunFixedHits(b, fh); break;
            case HitYouAction hy: b.Do(s => LandBlockable(s, hy.Name, hy.Amount)); break;
            case FirstPlayOnly fp: if (b.States.Count > 0 && b.States[0].CurrentPlay is not { PlayIndex: > 0 }) foreach (var x in fp.Actions) Run(b, x); break;
            case CardResolvedAction cr:
                b.Do(s => CardResolved(s, cr));
                HookStep(b, nameof(SimModel.AfterPlayResolved), (m, o, x) => m.AfterPlayResolved(x, o, cr.Owner));
                break;
            case OrbAction oa: RunOrb(b, oa); break;
            case FocusAction fa: b.Do(s => ChangeFocus(s, fa.Delta)); break;
            case ShootAction: RunShoot(b); break;
            case ImpaleAction: RunImpale(b); break;
            case DislodgeAllAction: RunDislodgeAll(b); break;
            case DamageAction da: RunDamage(b, da); break;
            case ExecuteAction ex: RunExecute(b, ex); break;
            case StripBlockAction sb: b.Do(x => StripBlock(x, sb)); break;
            case TriggerPoisonAction: b.Each(TriggerPoison); break;
            case KillOstyAction: b.Do(s => OstyLoses(s, Who.You, s.P.OstyHp)); break;
            case CardPlayAction pc: RunCardPlay(b, pc); break;
            case AttackAction at: RunAttack(b, at); break;
            case GainBlockAction gb: RunBlock(b, gb); break;
            case ApplyPowerAction ap: RunApplyPower(b, ap); break;
            case CardEventAction ce: RunCardEvent(b, ce); break;
            case LoseHpAction lh: b.Do(s => LandUnblockable(s, s.CurrentPlay?.Card.Name ?? "HP cost", lh.Amount)); break;
            default: throw new NotSupportedException($"No runner for {a.GetType().Name}");
        }
        // Events raised inside its hits or deaths (an arrow knocked loose) run once the action is over.
        if (b.States.Any(s => s.Pending.Count > 0)) b.Each(DrainPending);
    }

    // ------------------------------------------------------------------ end of turn

    private static void AllEnemies(SimState s, string name, int amount)
    {
        if (amount <= 0) return;
        if (s.Log) s.EndOfTurnDamage.Add((name + " (all)", amount));
        HitAll(s, _ => amount);
    }

    /// <summary>One player's orb passives at end of turn, front first (yours, or a teammate's: their Frost blocks for them).</summary>
    /// <param name="mutable">The same player, to change (a teammate shared between branches is copied first).</param>
    /// <param name="player">-1: you; k: teammate k.</param>
    private static void OrbPassives(BranchSet b, Func<SimState, SimPlayer> who, int player, Func<SimState, SimPlayer>? mutable = null)
    {
        mutable ??= who;
        var maxOrbs = b.States.Max(s => who(s).Orbs.Count);
        for (var i = 0; i < maxOrbs; i++)
        {
            var index = i;
            int Triggers(SimState s) => index < who(s).Orbs.Count ? who(s).Orbs[index].Triggers + (index == 0 ? who(s).FrontOrbBonusTriggers : 0) : 0;
            var maxTriggers = b.States.Max(Triggers);
            for (var t = 0; t < maxTriggers; t++)
            {
                var trigger = t;
                b.Each(s =>
                {
                    if (index >= who(s).Orbs.Count) return new[] { s };
                    var orb = who(s).Orbs[index];
                    if (trigger >= Triggers(s)) return new[] { s };
                    switch (orb.Kind)
                    {
                        case OrbKind.Lightning:
                            if (s.Log) s.EndOfTurnDamage.Add(("Lightning", orb.Passive));
                            return HitRandom(s, _ => orb.Passive);
                        case OrbKind.Frost:
                            return GainBlock(s, orb.Passive, player < 0 ? "Frost" : null, player);
                        case OrbKind.Dark:
                            mutable(s).OwnOrbs()[index].Evoke += orb.Passive;
                            return new[] { s };
                        case OrbKind.Glass:
                            // GlassOrb.Passive: if PassiveVal (base + Focus) > 0, the base drops by 1
                            // (not below 0) and every enemy takes the value from before the drop.
                            if (orb.Passive > 0)
                            {
                                orb = mutable(s).OwnOrbs()[index];
                                var v = orb.Passive;
                                if (s.Log) s.EndOfTurnDamage.Add(("Glass (all)", v));
                                HitAll(s, _ => v);
                                orb.GlassBase = Math.Max(0, orb.GlassBase - 1);
                                orb.Passive = Math.Max(0, orb.GlassBase + orb.GlassBonus);
                                orb.Evoke = orb.Passive * 2; // GlassOrb.EvokeVal => PassiveVal * 2
                            }
                            return new[] { s };
                        default:
                            return new[] { s };
                    }
                });
            }
        }

    }

    /// <summary>
    /// The auto-play phase of the players' end of turn (CombatManager.EndPlayerTurnPhaseOneInternal: PlayerTurnPhase.AutoPostPlay,
    /// Hook.AfterAutoPostPlayPhaseEntered for each player ending the turn, in player order), before BeforeSideTurnEnd. For each
    /// player, hook listeners run their powers first (Stampede, Simpleton), then the cards in their piles (I Am Invincible on
    /// top of their draw pile, Howl from Beyond in their exhaust pile). Auto-played cards cost nothing (CardCmd.AutoPlay); one
    /// aimed at an enemy takes a random hittable one. A teammate's Stampede and Simpleton pick from their hand: only with
    /// their hand (SimState.WithoutTeammateHands clears them; decision #25), and Stampede is never filled for them.
    /// </summary>
    private static void AutoPostPlay(BranchSet b)
    {
        static bool Any(SimPlayer p) => p.Stampede > 0 || p.Simpleton > 0 || p.ExhaustAutoPlays.Count > 0 || p.InvincibleBlock > 0 || p.Draw?.HasInvincible == true;
        if (!b.States.Any(s => Any(s.P) || s.Teammates.Any(Any))) return;
        // Your turn is over: block gained now (the auto-played card's, Daughter of the Wind's on it) is end-of-turn block in the
        // Incoming Damage breakdown, as the forecast check counts it.
        b.Do(s => s.EndOfTurnNow = true);
        foreach (var k in PlayerOrder(b.States[0]).ToList()) AutoPostPlayOf(b, k);
    }

    /// <summary>One player's auto-plays (k: -1 you, else teammate k).</summary>
    private static void AutoPostPlayOf(BranchSet b, int k)
    {
        SimPlayer P(SimState s) => k < 0 ? s.P : s.Teammates[k];
        bool Alive(SimState s) => k < 0 || s.Teammates[k].Hp > 0;
        // Stampede: a random Attack from their hand (unplayable cards aren't in AutoHand), each time.
        var stampede = b.States.Max(s => P(s).Stampede);
        for (var i = 0; i < stampede; i++)
        {
            var n = i;
            b.Each(s => n < P(s).Stampede && Alive(s) ? AutoPlayFromHand(s, k, hand => hand.Where(c => c.IsAttack).ToList()) : new[] { s });
        }
        // Simpleton: a random card among their cheapest, each time.
        var simpleton = b.States.Max(s => P(s).Simpleton);
        for (var i = 0; i < simpleton; i++)
        {
            var n = i;
            b.Each(s => n < P(s).Simpleton && Alive(s)
                ? AutoPlayFromHand(s, k, hand => hand.Count == 0 ? hand : hand.Where(c => c.Cost == hand.Min(x => x.Cost)).ToList())
                : new[] { s });
        }
        // I Am Invincible on top of their draw pile: its block. With your draw pile (decision #91): certain when it's known to be
        // on top, else a chance (how many of the unordered rest are copies of it).
        b.Each(s =>
        {
            if (P(s).Draw is { } pile && Alive(s)) return InvincibleFromPile(s, k, pile);
            if (P(s).InvincibleBlock <= 0 || !Alive(s)) return new[] { s };
            if (k < 0) s.AutoPlayed++; // yours (the outcome audit counts your auto-plays)
            return GainBlock(s, P(s).InvincibleBlock, "I Am Invincible", k);
        });
        // Howl from Beyond in their exhaust pile: each copy plays (an attack on every enemy).
        var howls = b.States.Max(s => P(s).ExhaustAutoPlays.Count);
        for (var i = 0; i < howls; i++)
        {
            var play = b.States.Select(s => P(s).ExhaustAutoPlays).First(l => l.Count > i)[i];
            if (!b.States.Any(Alive)) break;
            if (k < 0) b.Do(s => s.AutoPlayed++);
            ApplyPlay(b, play);
        }
    }

    /// <summary>
    /// I Am Invincible (IAmInvincible.AfterAutoPostPlayPhaseEntered): a copy that's the top card of your draw pile plays itself,
    /// gaining its block, then goes to the discard pile. The known top decides it; else the top is any card of the rest, each
    /// equally likely: a branch where it's a copy (their share), and one where it isn't (nothing happens; which card it is
    /// stays unknown, close enough for the rest of the turn).
    /// </summary>
    private static IEnumerable<SimState> InvincibleFromPile(SimState s, int k, SimDrawPile pile)
    {
        IEnumerable<SimState> Play(SimState x, SimDrawCard card, SimDrawPile after)
        {
            MutablePlayer(x, k < 0 ? Who.You : Who.Teammate(k)).Draw = after.WithDiscarded(card);
            if (k < 0) x.AutoPlayed++;
            return GainBlock(x, card.InvincibleBlock, "I Am Invincible", k);
        }
        if (pile.Top.Count > 0)
            return pile.Top[0].InvincibleBlock > 0 ? Play(s, pile.Top[0], pile.WithoutTop()) : new[] { s };
        var total = pile.RestCount;
        var copies = pile.Rest.Select((g, i) => (g, i)).Where(x => x.g.Card.InvincibleBlock > 0).ToList();
        if (total == 0 || copies.Count == 0) return new[] { s };
        var result = new List<SimState>();
        var other = total - copies.Sum(x => x.g.Count);
        foreach (var (g, i) in copies)
        {
            var c = s.Clone();
            c.Weight = s.Weight * g.Count / total;
            result.AddRange(Play(c, g.Card, pile.WithoutOneOf(i)));
        }
        if (other > 0)
        {
            var c = s.Clone();
            c.Weight = s.Weight * other / total;
            result.Add(c);
        }
        return result;
    }

    /// <summary>
    /// One auto-play from a player's hand (k: -1 you, else teammate k): a branch per card <paramref name="pick"/> allows
    /// (CombatCardSelection.NextItem: each equally likely), and per hittable target for a card aimed at one (SimPlay.TargetId). A card that changes the hand (it draws or makes cards) still plays; later auto-plays in
    /// that branch aren't predicted (CardResolved). One with no play built ends them too.
    /// </summary>
    private static IEnumerable<SimState> AutoPlayFromHand(SimState s, int k, Func<List<SimHandCard>, List<SimHandCard>> pick)
    {
        var hand = k < 0 ? s.P.AutoHand : s.Teammates[k].AutoHand;
        if (hand == null) return new[] { s };
        if (s.AutoPlaysUnknown)
        {
            // A pick is due from a hand the simulator lost track of (a card made or brought back cards, one drawn can't be played).
            s.Unmodelled ??= "an end-of-turn auto-play from a hand that changed in a way not simulated";
            return new[] { s };
        }
        var cards = pick(hand);
        if (cards.Count == 0) return new[] { s };
        var result = new List<SimState>();
        foreach (var card in cards)
        {
            var c = s.Clone();
            c.Weight = s.Weight / cards.Count;
            if (k < 0) c.AutoPlayed++;
            var cp = k < 0 ? c.P : c.MutableTeammate(k);
            cp.AutoHand = cp.AutoHand!.Where(x => x.Id != card.Id).ToList();
            if (card.Plays.Count == 0)
            {
                c.AutoPlaysUnknown = true;
                c.Unmodelled ??= $"{card.Name} auto-played at end of turn: its play isn't simulated";
                if (k < 0) c.AutoPicks = [..c.AutoPicks, new SimPick(card.Id, -1)];
                result.Add(c);
                continue;
            }
            var plays = card.Plays.Count == 1 && card.Plays[0].TargetId < 0
                ? card.Plays
                : card.Plays.Where(p => c.Enemy(p.TargetId) is { Alive: true, Hittable: true }).ToList();
            if (plays.Count == 0)
            {
                result.Add(c); // no target: it goes to the discard pile unplayed (CardCmd.AutoPlay)
                continue;
            }
            foreach (var play in plays)
            {
                var t = plays.Count == 1 ? c : c.Clone();
                t.Weight = c.Weight / plays.Count;
                if (k < 0) t.AutoPicks = [..t.AutoPicks, new SimPick(card.Id, play.TargetId)];
                var sub = new BranchSet(t, nested: true);
                ApplyPlay(sub, play);
                result.AddRange(sub.States);
            }
        }
        return result;
    }

    /// <summary>
    /// A player's Ethereal cards that exhaust at their turn end: without the ones Ethereal only through Hex once its caster is
    /// dead (HexPower.AfterDeath removes it, and its Ethereal goes with it; decision #85).
    /// </summary>
    private static int Exhausting(SimState s, SimPlayer p)
        => p.HexFading > 0 && p.HexFrom >= 0 && s.Enemy(p.HexFrom) is { Alive: false } ? Math.Max(0, p.Fading - p.HexFading) : p.Fading;

    /// <summary>
    /// Your side's turn ends (CombatManager.EndPlayerTurnPhaseOne/Two, decompile v0.113.1):
    ///  1. Hook.BeforeSideTurnEnd: every listener's VeryEarly (Orichalcum checks your block), then Early (Plating, Pael's Eye
    ///     exhausting your hand), then BeforeSideTurnEnd (Hailstorm, The Bomb, Cloak Clasp, Ripple Basin, Screaming Flagon, Stone
    ///     Calendar, Obsidian Vice...), in listener order: players in player order, their powers then their relics.
    ///  2. DoTurnEnd, each player in player order: orb passives (front first), Ethereal cards exhaust (Feel No Pain, Charon's
    ///     Ashes, Forgotten Soul, Dark Embrace react to each), then cards in hand act (Burn, Decay, Regret).
    ///  3. Hook.AfterSideTurnEnd (Constrict, Magic Bomb, Demise, Consuming Shadow, Parrying Shield, Lunar Pastry), then
    ///     AfterSideTurnEndLate (Disintegration, Insatiable Dominion).
    /// </summary>
    private static void EndOfTurn(BranchSet b)
    {
        b.Do(s => s.EndOfTurnNow = true);
        HookStep(b, nameof(SimModel.BeforeSideTurnEndVeryEarly), (m, o, x) => Who.IsPlayer(o) ? m.BeforeSideTurnEndVeryEarly(x, o) : new[] { x });
        HookStep(b, nameof(SimModel.BeforeSideTurnEndEarly), (m, o, x) => Who.IsPlayer(o) ? m.BeforeSideTurnEndEarly(x, o) : new[] { x });
        ExplosionCheck(b);
        HookStep(b, nameof(SimModel.BeforeSideTurnEnd), (m, o, x) => Who.IsPlayer(o) ? m.BeforeSideTurnEnd(x, o) : new[] { x });
        ExplosionCheck(b);

        // 2. Each player's DoTurnEnd, in player order.
        var teammates = b.States.Count > 0 ? b.States[0].Teammates.Count : 0;
        var before = b.States.Count > 0 ? Math.Min(b.States[0].TeammatesBeforeMe, teammates) : 0;
        void TurnEnd(int k)
        {
            if (k < 0) OrbPassives(b, s => s.P, -1);
            else OrbPassives(b, s => s.Teammates[k], k, mutable: s => s.MutableTeammate(k));
            ExplosionCheck(b); // kills by orb passives
            var player = k < 0 ? Who.You : Who.Teammate(k);
            b.Each(s => PlayerOf(s, player) is { } p && Exhausting(s, p) is > 0 and var n && TakesPart(s, player) ? ProcCardEvent(s, CardEvent.Exhausted, n, player, indirect: false) : new[] { s });
            b.Do(s => CardsActInHand(s, player));
            ExplosionCheck(b);
        }
        for (var k = 0; k < before; k++) TurnEnd(k);
        TurnEnd(-1);
        for (var k = before; k < teammates; k++) TurnEnd(k);

        // 3. AfterSideTurnEnd (Constrict, Magic Bomb, Demise, Consuming Shadow, Parrying Shield, Lunar Pastry), then
        // AfterSideTurnEndLate (Disintegration, Insatiable Dominion).
        HookStep(b, nameof(SimModel.AfterSideTurnEnd), (m, o, x) => Who.IsPlayer(o) ? m.AfterSideTurnEnd(x, o) : new[] { x });
        ExplosionCheck(b); // kills by Consuming Shadow's evokes, Parrying Shield, Black Hole
        HookStep(b, nameof(SimModel.AfterSideTurnEndLate), (m, o, x) => Who.IsPlayer(o) ? m.AfterSideTurnEndLate(x, o) : new[] { x });

        // Deaths at the end of your turn run their hooks before the enemies act (arrows coming back: Twine's block).
        b.Do(AllyDeaths);
        if (b.States.Any(s => s.Pending.Count > 0)) b.Each(DrainPending);
        b.Do(s =>
        {
            s.EndOfTurnNow = false;
            s.BlockAtEnemyTurn = s.P.Block;
            foreach (var e in s.Enemies) e.HpAtTurnEnd = Math.Max(0, e.Hp);
        });
    }

    // ------------------------------------------------------------------ enemy turn

    /// <summary>
    /// Enemies react to an ally's death as it happens (AfterDeath): Ravenous is stunned (and gains
    /// Strength for later turns), Crab Rage gains Strength (its hits) and block, once.
    /// </summary>
    /// <summary>
    /// Your Focus changes: each orb's values move by what Focus does to its base value
    /// (FocusPower.ModifyOrbValue: base + Focus, not below 0), so other modifiers on it stay.
    /// Lightning 3/8, Frost 2/5, Dark's passive 6 (its evoke is what it stored), Glass from its base
    /// (evoke twice the passive). Plasma doesn't use Focus.
    /// </summary>
    private static void ChangeFocus(SimState s, int delta)
    {
        int Shift(int value, int baseValue) => value + Math.Max(0, baseValue + s.P.Focus + delta) - Math.Max(0, baseValue + s.P.Focus);
        foreach (var o in s.P.OwnOrbs())
        {
            switch (o.Kind)
            {
                case OrbKind.Lightning: o.Passive = Shift(o.Passive, 3); o.Evoke = Shift(o.Evoke, 8); break;
                case OrbKind.Frost: o.Passive = Shift(o.Passive, 2); o.Evoke = Shift(o.Evoke, 5); break;
                case OrbKind.Dark: o.Passive = Shift(o.Passive, 6); break;
                case OrbKind.Glass:
                    o.Passive = Shift(o.Passive, o.GlassBase);
                    o.GlassBonus += delta;
                    o.Evoke = o.Passive * 2;
                    break;
            }
        }
        s.P.Focus += delta;
    }

    private static void StripBlock(SimState s, StripBlockAction sb)
    {
        if (s.Enemy(s.CurrentPlay?.Target ?? -1) is not { Alive: true } e) return;
        if (e.Block > 0)
        {
            e.Block = 0;
            e.BlockBroken = true;
            foreach (var (m, o) in Listeners(s)) m.AfterBlockBroken(s, o, e.Id);
        }
        if (sb.RemoveArtifact && PowersOf(s, e.Id).Has("ArtifactPower")) SetPowers(s, e.Id, PowersOf(s, e.Id).Without("ArtifactPower"));
    }

    private static void AllyDeaths(SimState s)
    {
        Explosions(s);
        // AfterDeath, once for each enemy that has died (Crab Rage, Ravenous as models).
        foreach (var dead in s.Enemies)
        {
            if (dead.Alive || dead.DeathHandled || !dead.StartedAlive) continue;
            dead.DeathHandled = true;
            foreach (var (m, o) in Listeners(s)) m.AfterDeath(s, o, dead.Id);
        }
        foreach (var e in s.Enemies)
        {
            if (!e.Alive || !s.Enemies.Any(o => o != e && o.StartedAlive && !o.Alive)) continue;
            if (e.HitsAfterAllyDies != null)
            {
                e.Hits = e.HitsAfterAllyDies;
                e.HitsAfterAllyDies = null;
                e.Block += e.CrabRageBlock;
            }
        }
    }

    private static void EnemyTurn(SimState s)
    {
        AllyDeaths(s); // deaths at your end of turn
        s.FullBlockNeeded = new Dictionary<int, int>(); // per branch (shared until now)
        s.EnemyTurnNow = true; // Inferno and the like react only on their owner's side's turn
        // Every enemy's poison ticks as the enemies' turn starts (PoisonPower.AfterSideTurnStart, before ExecuteEnemyTurn), stunned
        // or not, and Hardened Shell's budget resets then. Its deaths run their hooks before any enemy moves (Ravenous stuns the
        // other Corpse Slug; Crab Rage).
        var poisoned = new HashSet<int>();
        foreach (var e in s.Enemies)
        {
            if (!e.Alive) continue;
            e.ShellLeft = e.ShellPerTurn;
            var poison = Math.Min(e.Poison, e.ShellLeft);
            e.Hp -= poison;
            if (e.ShellLeft != int.MaxValue) e.ShellLeft -= poison;
            if (e.Alive) continue;
            poisoned.Add(e.Id);
            e.DiedBeforeActing = true;
            if (HitCount(e) > 0) s.SkippedPoisoned++;
            Explosions(s);
        }
        if (poisoned.Count > 0) AllyDeaths(s);
        foreach (var e in s.Enemies)
        {
            if (poisoned.Contains(e.Id)) continue;
            if (!e.Alive)
            {
                e.DiedBeforeActing = true;
                if (HitCount(e) > 0) s.SkippedEndOfTurn++;
                continue;
            }
            if (e.Stunned || HitCount(e) == 0) continue; // a stunned enemy skips its move
            var enemyBlock = e.Barricade ? e.Block : 0;

            var landed = 0;
            var hitsLanded = 0;
            var dead = false;
            void Retaliate(int amount)
            {
                if (amount <= 0) return;
                amount = Math.Min(amount, e.HitCap);
                var absorbed = Math.Min(enemyBlock, amount);
                enemyBlock -= absorbed;
                var toHp = Math.Min(amount - absorbed, Math.Max(0, e.ShellLeft));
                if (e.ShellLeft != int.MaxValue) e.ShellLeft -= toHp;
                e.Hp -= toHp;
                if (e.Hp <= 0) dead = true;
            }

            // One player takes one hit (who: -1 you, k teammate k), the same for every player (decision #93): Thorns before it
            // lands (BeforeDamageReceived; the hit still lands if that kills), Wuppy first while it lives (WuppysLoyaltyPower.
            // ModifyPreBlockDamageTarget: before block; only the overkill goes on), block, Osty (Die For You), HP, then Flame
            // Barrier and Reflect. Yours also decides Imbalanced and feeds the breakdown.
            void TakesHit(int who, int amount)
            {
                var p = Mutable(s, who);
                if (p.Thorns > 0) Retaliate(p.Thorns);
                if (p.WuppyHp > 0 && amount > 0)
                {
                    var toWuppy = Math.Min(p.WuppyHp, amount);
                    p.WuppyHp -= toWuppy;
                    p.WuppyTaken += toWuppy; // not their Incoming Damage (landed): it never reaches them
                    amount -= toWuppy;
                    if (amount == 0)
                    {
                        if (who >= 0) return;
                        // DamageResult.WasFullyBlocked: no HP lost and you had block (untouched).
                        if (hitsLanded == 0) e.FirstHitFullyBlocked = s.P.Block > 0;
                        hitsLanded++;
                        return;
                    }
                }
                if (who < 0)
                {
                    if (hitsLanded == 0 && e.Imbalanced) s.FullBlockNeeded[e.Id] = s.BlockDemand + amount;
                    s.BlockDemand += amount;
                }
                var blockBefore = p.Block;
                p.Incoming += amount;
                var blocked = Math.Min(p.Block, amount);
                p.Block -= blocked;
                var through = amount - blocked;
                var toOsty = p.OstyShields ? OstyLoses(s, who < 0 ? Who.You : Who.Teammate(who), through) : 0;
                p = Mutable(s, who);
                p.OstyTaken += toOsty;
                p.Through += through - toOsty;
                var lost = HpLoss(s, who, through - toOsty, e.Id, SimProps.Move);
                Lose(s, who, lost);
                PlayerDamaged(s, who, e.Id, SimProps.Move, blocked, lost);
                if (who < 0)
                {
                    // DamageResult.WasFullyBlocked: you had block and no damage got through after the AfterOsty modifiers (Tungsten
                    // Rod, Buffer...). Block only drops during an attack, so the first hit decides it.
                    if (hitsLanded == 0) e.FirstHitFullyBlocked = amount > 0 && (blocked > 0 || blockBefore > 0) && toOsty == 0 && lost == 0;
                    landed += amount;
                    hitsLanded++;
                }
                p = Mutable(s, who);
                if (!dead && p.FlameBarrier > 0) Retaliate(p.FlameBarrier);
                if (!dead && p.Reflect && blocked > 0) Retaliate(blocked);
            }
            // A teammate takes each hit in their turn order (CreatureCmd.Damage over every living player: a hit still reaches
            // everyone even if one player's Thorns kills the attacker during it), through the game's damage math for them.
            void TeammateTakesHit(int k, int h, int mine)
            {
                if (s.Teammates[k].Hp <= 0) return;
                var amount = e.Intent != null ? IntentDamage(s, e, h, Who.Teammate(k))
                    : k < e.TeammateHits.Count && h < e.TeammateHits[k].Count ? e.TeammateHits[k][h] : mine;
                TakesHit(k, amount);
            }
            var before = Math.Min(s.TeammatesBeforeMe, s.Teammates.Count);
            var hitIndex = -1;
            for (var hit = 0; hit < HitCount(e); hit++)
            {
                hitIndex++;
                // The hit on you: the intent through the damage pipeline as it lands, or the final number.
                var perHit = e.Intent != null ? IntentDamage(s, e, hit, Who.You) : e.Hits[hit];
                if (dead)
                {
                    s.CancelledHits++;
                    continue;
                }
                for (var k = 0; k < before; k++) TeammateTakesHit(k, hitIndex, perHit);
                TakesHit(-1, perHit);
                for (var k = before; k < s.Teammates.Count; k++) TeammateTakesHit(k, hitIndex, perHit);
            }
            if (landed > 0) if (s.Log) s.Blockable.Add((hitsLanded > 1 ? $"{e.Name} ({hitsLanded} hits)" : e.Name, landed));
            // An enemy killed during the enemy turn (your Thorns, Flame Barrier or Reflect hitting back during its attack): its death
            // hooks run before the next enemy moves (ExplosivePower and RavenousPower.AfterDeath: the other Corpse Slug is stunned,
            // Crab Rage hits harder). Poison deaths run theirs at the turn's start, above.
            if (s.Enemies.Any(o => o.StartedAlive && !o.Alive && !o.DeathHandled)) AllyDeaths(s);
        }
    }
}
