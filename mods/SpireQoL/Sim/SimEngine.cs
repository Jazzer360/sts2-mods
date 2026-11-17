namespace SpireQoL.Sim;

/// <summary>The branches still possible after the simulation, with helpers for guarantees and the worst case.</summary>
public sealed class SimOutcome
{
    public List<SimState> Branches = new();
    /// <summary>Too many branches: results are from the explored ones only, so no guarantees.</summary>
    public bool Truncated;

    /// <summary>After truncation: the run where random hits land on nobody (an upper bound on damage to you).</summary>
    public SimState? SafeWorst;

    /// <summary>
    /// The simulator's work, the same on every machine (the performance tests limit these, not time):
    /// steps applied to the branch set, branches stepped over all steps, and state copies (the safe run's included).
    /// </summary>
    public int Steps, Stepped, Copies;
    /// <summary>The most copies one step made (at most about StepCopyBudget).</summary>
    public int MaxStepCopies;

    public SimState Worst => SafeWorst ?? Branches.OrderByDescending(b => b.Taken).ThenByDescending(b => b.OstyTaken).First();
    public int TakenMin => Branches.Min(b => b.Taken);
    /// <summary>Teammate k's HP lost: the worst branch (the safe run included) and the best.</summary>
    public int TeammateTakenMax(int k) => Math.Max(Branches.Max(b => b.Teammates[k].Taken), SafeWorst?.Teammates[k].Taken ?? 0);
    public int TeammateTakenMin(int k) => Branches.Min(b => b.Teammates[k].Taken);
    /// <summary>What teammate k's Osty takes: the worst branch (the safe run included) and the best.</summary>
    public int TeammateOstyTakenMax(int k) => Math.Max(Branches.Max(b => b.Teammates[k].OstyTaken), SafeWorst?.Teammates[k].OstyTaken ?? 0);
    public int TeammateOstyTakenMin(int k) => Branches.Min(b => b.Teammates[k].OstyTaken);
    /// <summary>Your Parrying Shield fires in every branch (none dropped) / in some branch (the safe run included).</summary>
    public bool ParryingShieldInAll => !Truncated && Branches.All(b => b.ParryingShieldFired);
    public bool ParryingShieldInAny => Branches.Any(b => b.ParryingShieldFired) || SafeWorst?.ParryingShieldFired == true;
    /// <summary>Teammate k in the branch where they lose the most HP (the safe run included): their Incoming Damage.</summary>
    public SimPlayer TeammateWorst(int k)
    {
        var worst = Branches.Select(b => b.Teammates[k]).OrderByDescending(t => t.Taken).First();
        return SafeWorst?.Teammates[k] is { } safe && safe.Taken > worst.Taken ? safe : worst;
    }
    public int TakenMax => Math.Max(Branches.Max(b => b.Taken), SafeWorst?.Taken ?? 0);
    public int OstyTakenMin => Branches.Min(b => b.OstyTaken);
    public int OstyTakenMax => Math.Max(Branches.Max(b => b.OstyTaken), SafeWorst?.OstyTaken ?? 0);

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
    public bool DeadInAll(int id) => !Truncated && Branches.All(b => b.Enemy(id) is { DiedBeforeActing: true });
    public bool DeadInAny(int id) => Branches.Any(b => b.Enemy(id) is { DiedBeforeActing: true });
    /// <summary>The chance it dies before acting (NaN when branches ran out: unknown).</summary>
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

    public const int MaxBranches = 128;

    /// <summary>
    /// State copies one step may make (each hit, orb, block gain: SimEngine steps). Past it, the step's
    /// random choices follow their first target and the run is out of branches (damage to you then comes
    /// from the safe run). Bounds one step whatever chains inside it (Beacon of Hope sharing block with
    /// Juggernaut on every player, Shatter): a simulation costs at most about steps × this.
    /// </summary>
    public const int StepCopyBudget = 2048;

    /// <summary>Branches kept after running out (each then follows one outcome per step).</summary>
    public const int SampleAfterTruncation = 32;

    public static SimOutcome Run(SimState start, SimPlay? play = null) => Run(start, null, play);

    public static SimOutcome Run(SimState start, SimScript play) => Run(start, (IReadOnlyList<SimScript>?)null, play);

    public static SimOutcome Run(SimState start, IReadOnlyList<SimPlay>? leadIn, SimPlay? play) =>
        Run(start, leadIn?.Select(SimScript.Of).ToList(), play == null ? null : SimScript.Of(play));

    /// <summary>
    /// The plays still resolving or queued (leadIn, in resolution order) first, then this card, then the
    /// end of the turn and the enemy turn (decision #27). The card's own results (kills, block breaks, block
    /// gains, reactions) are measured from each branch's state after the lead-in.
    /// </summary>
    public static SimOutcome Run(SimState start, IReadOnlyList<SimScript>? leadIn, SimScript? play)
    {
        var work = new SimWork();
        var first = start.Clone();
        first.Work = work;
        var b = new BranchSet(first);
        if (leadIn is { Count: > 0 })
        {
            foreach (var queued in leadIn) RunScript(b, queued);
            b.Do(s =>
            {
                s.PlayBase = new SimState.PlayBaseline(
                    s.Enemies.Where(e => e.Alive).Select(e => e.Id).ToHashSet(),
                    s.Enemies.Where(e => e.BlockBroken).Select(e => e.Id).ToHashSet(),
                    s.P.BlockGains);
                foreach (var e in s.Enemies) e.ReactedWithBlock = false;
                s.PlayBiggestHit = 0;
                s.PlayHpReached = s.PlayHpLosses = s.PlayHpLost = 0;
                s.Fired = s.FiredAny = PowerSet.Empty;
                s.FirstHits = PowerSet.Empty;
            });
        }
        if (play != null) RunScript(b, play);
        // Kills during the play itself (its hits, orbs, procs, Juggernaut), for kill-reactive tabs.
        var aliveAtStart = start.Enemies.Where(e => e.Alive).Select(e => e.Id).ToList();
        var killedPerBranch = b.States.Select(s => (s.PlayBase?.Alive ?? (IEnumerable<int>)aliveAtStart).Where(id => s.Enemy(id) is not { Alive: true }).ToHashSet()).ToList();
        // Enemies that gained block from Curl Up, Sneaky or Skittish because of the play, in some branch.
        var reacted = b.States.SelectMany(s => s.Enemies.Where(e => e.ReactedWithBlock).Select(e => e.Id)).ToHashSet();
        // Some enemy's block broken by the play, in every branch (Hand Drill).
        var breaksBlock = !b.Truncated && b.States.All(s => s.Enemies.Any(e => e.BlockBroken && !(s.PlayBase?.Broken.Contains(e.Id) ?? start.Enemy(e.Id) is not { BlockBroken: false })));
        // Block gains during the play (each sets off Juggernaut).
        var gains = b.States.Select(s => s.P.BlockGains - (s.PlayBase?.BlockGains ?? start.P.BlockGains)).ToList();
        var playTruncated = b.Truncated;
        var biggestHitMin = b.Truncated || b.States.Count == 0 ? 0 : b.States.Min(s => s.PlayBiggestHit);
        int PlayMin(Func<SimState, int> f) => b.Truncated || b.States.Count == 0 ? 0 : b.States.Min(f);
        var hpReached = PlayMin(s => s.PlayHpReached);
        var hpLosses = PlayMin(s => s.PlayHpLosses);
        var hpLost = PlayMin(s => s.PlayHpLost);
        // The play's log over its branches: in every branch (each merged one keeps its smallest), and in some branch.
        var firedEvery = b.Truncated || b.States.Count == 0 ? PowerSet.Empty
            : b.States.Skip(1).Aggregate(b.States[0].Fired, (acc, x) => SimState.MergeLog(acc, x.Fired, every: true));
        var firedAny = b.States.Aggregate(PowerSet.Empty, (acc, x) => SimState.MergeLog(acc, x.FiredAny, every: false));
        // The first hit of the card's attack on each enemy, when nothing random came before it (the damage-formula audit).
        var firstHits = b.States.Count > 0 && b.States.All(x => ReferenceEquals(x.FirstHits, b.States[0].FirstHits) || x.FirstHits.Items.AsSpan().SequenceEqual(b.States[0].FirstHits.Items)) ? b.States[0].FirstHits : PowerSet.Empty;
        AutoPostPlay(b);
        EndOfTurn(b);
        b.Do(EnemyTurn);
        var outcome = new SimOutcome
        {
            Branches = b.States,
            Truncated = b.Truncated,
            KilledByPlayInAny = killedPerBranch.SelectMany(k => k).ToHashSet(),
            KilledByPlayInAll = playTruncated || killedPerBranch.Count == 0
                ? new HashSet<int>()
                : killedPerBranch.Skip(1).Aggregate(new HashSet<int>(killedPerBranch[0]), (acc, k) => { acc.IntersectWith(k); return acc; }),
            PlayKillsInEveryBranch = !playTruncated && killedPerBranch.Count > 0 && killedPerBranch.All(k => k.Count > 0),
            BlockReactionInAny = reacted,
            PlayBreaksBlockInEveryBranch = breaksBlock,
            PlayBlockGainsMax = gains.Count > 0 ? gains.Max() : 0,
            PlayBiggestHitMin = biggestHitMin,
            PlayHpReachedMin = hpReached,
            PlayHpLossesMin = hpLosses,
            PlayHpLostMin = hpLost,
            PlayFiredEvery = firedEvery,
            PlayFiredAny = firedAny,
            PlayFirstHits = firstHits,
        };
        if (b.Truncated)
        {
            // Too many branches to explore: nothing is guaranteed, and damage to you comes from a run
            // where random hits land on nobody, so no attacker is ever saved by one (never an underestimate).
            var safeStart = start.Clone();
            safeStart.Work = work;
            safeStart.NoRandomHits = true;
            var safe = new BranchSet(safeStart);
            if (leadIn != null) foreach (var queued in leadIn) RunScript(safe, queued);
            if (play != null) RunScript(safe, play);
            AutoPostPlay(safe);
            EndOfTurn(safe);
            safe.Do(EnemyTurn);
            outcome.SafeWorst = safe.States[0];

        }
        work.EndStep();
        outcome.Steps = work.Steps;
        outcome.Stepped = work.Stepped;
        outcome.Copies = work.Copies;
        outcome.MaxStepCopies = work.MaxStepCopies;
        return outcome;
    }

    // ------------------------------------------------------------------ branching

    /// <summary>
    /// Chains a branching effect inside one step (Juggernaut on each block gain Beacon of Hope shares,
    /// Forgotten Soul after Feel No Pain): identical results merge, as between steps. The step's copy
    /// budget (Branch) bounds the rest; without both, Beacon of Hope with Juggernaut on four players ran
    /// out of memory.
    /// </summary>
    private static List<SimState> Then(IEnumerable<SimState> states, Func<SimState, IEnumerable<SimState>> f)
    {
        var next = new List<SimState>();
        var seen = new Dictionary<long, SimState>();
        foreach (var x in states)
        {
            foreach (var r in f(x))
            {
                var key = r.Key();
                if (seen.TryGetValue(key, out var same))
                {
                    same.Weight += r.Weight;
                    same.MergeResults(r); // not in the key: the guarantee is the smallest
                    same.OutOfBranches |= r.OutOfBranches;
                    continue;
                }
                seen[key] = r;
                next.Add(r);
            }
        }
        return next;
    }

    private sealed class BranchSet
    {
        public List<SimState> States;
        public bool Truncated;

        public BranchSet(SimState s) => States = new List<SimState> { s };

        private void Count()
        {
            if (States.Count > 0 && States[0].Work is { } w) { w.StartStep(); w.Steps++; w.Stepped += States.Count; }
        }

        /// <summary>Applies a step that may branch, merging identical results.</summary>
        public void Each(Func<SimState, IEnumerable<SimState>> step)
        {
            Count();
            // Out of branches: nothing is guaranteed any more (damage to you comes from the safe run), so
            // each explored branch just follows one outcome of the step, without cloning the others
            // (HitRandom yields lazily). Keeps a branch-heavy fight (four Defects' Lightning) cheap.
            if (Truncated)
            {
                for (var i = 0; i < States.Count; i++)
                {
                    States[i].FirstTargetOnly = true;
                    States[i] = step(States[i]).First();
                }
                return;
            }
            var next = new List<SimState>();
            var seen = new Dictionary<long, SimState>();
            foreach (var s in States)
            {
                foreach (var r in step(s))
                {
                    // An identical branch: one state, with both shares of the rolls.
                    var key = r.Key();
                    if (seen.TryGetValue(key, out var same)) { same.Weight += r.Weight; same.MergeResults(r); }
                    else
                    {
                        seen[key] = r;
                        next.Add(r);
                    }
                    if (next.Count >= MaxBranches || r.OutOfBranches)
                    {
                        // Out of branches: keep a sample that follows one outcome per step from here
                        // (see above). Its results only feed "can happen" checks.
                        Truncated = true;
                        States = next.Take(SampleAfterTruncation).ToList();
                        return;
                    }
                }
            }
            States = next;
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
        if (s.NoRandomHits)
        {
            yield return s;
            yield break;
        }
        var targets = s.Targetable.Select(e => e.Id).ToList();
        if (targets.Count == 0)
        {
            yield return s;
            yield break;
        }
        if (targets.Count == 1 || !Branch(s))
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

    /// <summary>
    /// Whether a random choice may branch here: not once the run is out of branches, nor past this step's
    /// copy budget (then the run is out of branches from here: BranchSet truncates after the step).
    /// </summary>
    private static bool Branch(SimState s)
    {
        if (s.FirstTargetOnly) return false;
        if (s.Work is not { OverBudget: true }) return true;
        s.FirstTargetOnly = true;
        s.OutOfBranches = true;
        return false;
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
        switch (orb.Kind)
        {
            // Thunder hits the evoke's target again if it's still alive.
            case OrbKind.Lightning: return HitRandom(s, _ => orb.Evoke, then: (st, e) => { var t = thunder ?? st.P.Thunder; if (t > 0 && e.Alive) { Damage(st, e, t); st.LogFired("ThunderPower", t, who < 0 ? Who.You : Who.Teammate(who)); } });
            case OrbKind.Frost: return GainBlock(s, orb.Evoke, null, who);
            case OrbKind.Dark:
            {
                var weakest = s.Targetable.OrderBy(e => e.Hp).FirstOrDefault();
                if (weakest != null) Damage(s, weakest, orb.Evoke);
                return new[] { s };
            }
            case OrbKind.Glass:
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

    /// <summary>Blockable damage to a player (who: -1 you, k teammate k): their block, then HP loss; Unpowered unless said.</summary>
    private static void LandBlockable(SimState s, string name, int amount, int who = -1, int? dealer = null, SimProps props = SimProps.Unpowered)
    {
        if (amount <= 0) return;
        if (who >= 0)
        {
            var t = s.MutableTeammate(who);
            t.Incoming += amount;
            var tb = Math.Min(t.Block, amount);
            t.Block -= tb;
            t.Through += amount - tb;
            TeammateHpLoss(s, who, amount - tb, tb, dealer, props);
            return;
        }
        s.BlockDemand += amount;
        if (s.Log) s.Blockable.Add((name, amount));
        var blocked = Math.Min(s.P.Block, amount);
        s.P.Block -= blocked;
        s.DamageThrough += amount - blocked;
        var lost = HpLoss(s, -1, amount - blocked, dealer, props);
        YouLose(s, lost);
        PlayHp(s, amount - blocked, lost);
        PlayerDamaged(s, -1, dealer, props, blocked, lost);
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
    /// You lose HP (after block and the HP-loss modifiers). Taken counts it all; your HP, when tracked, goes down, and
    /// at 0 something prevents death if it can (CreatureCmd.Kill → Hook.ShouldDie: Fairy in a Bottle first, then
    /// ShouldDieLate: Lizard Tail), healing you from 0; the rest of that hit is lost. With nothing left, you die.
    /// </summary>
    private static void YouLose(SimState s, int lost)
    {
        s.Taken += lost;
        var p = s.P;
        if (lost <= 0 || p.Hp == int.MaxValue || s.Died) return;
        p.Hp -= lost;
        if (p.Hp > 0) return;
        if (p.Fairies > 0)
        {
            p.Fairies--;
            s.FairiesUsed++;
            p.Hp = p.FairyHeal;
        }
        else if (p.LizardTailHeal > 0)
        {
            p.Hp = p.LizardTailHeal;
            p.LizardTailHeal = 0;
            s.LizardTailUsed = true;
        }
        else
        {
            p.Hp = 0;
            s.Died = true;
        }
    }

    /// <summary>Teammate k losing HP past their block (Tungsten Rod, Buffer, Beating Remnant like yours); their HP stops at 0.</summary>
    private static void TeammateHpLoss(SimState s, int k, int amount, int blocked, int? dealer, SimProps props)
    {
        var lost = Math.Min(HpLoss(s, k, amount, dealer, props), Math.Max(0, s.Teammates[k].Hp));
        var t = s.MutableTeammate(k);
        t.Taken += lost;
        t.Hp -= lost;
        PlayerDamaged(s, k, dealer, props, blocked, lost);
    }

    private static void LandUnblockable(SimState s, string name, int amount, int who = -1, int? dealer = null, SimProps props = SimProps.Unblockable | SimProps.Unpowered)
    {
        if (amount <= 0) return;
        if (who >= 0)
        {
            var t = s.MutableTeammate(who);
            t.Incoming += amount;
            TeammateHpLoss(s, who, amount, 0, dealer, props);
            return;
        }
        if (s.Log) s.Unblockable.Add((name, amount));
        var lost = HpLoss(s, -1, amount, dealer, props);
        YouLose(s, lost);
        PlayHp(s, amount, lost);
        PlayerDamaged(s, -1, dealer, props, 0, lost);
    }

    // ------------------------------------------------------------------ card play

    /// <summary>A script on its own (the state right after it), for tests of v2 plays.</summary>
    public static void RunScriptTo(SimState s, SimScript script, out List<SimState> branches, out bool truncated)
    {
        var first = s.Clone();
        first.Work = new SimWork();
        var b = new BranchSet(first);
        RunScript(b, script);
        branches = b.States;
        truncated = b.Truncated;
    }

    public static void ApplyPlayTo(SimState s, SimPlay play, out List<SimState> branches, out bool truncated) => ApplyPlayTo(s, null, play, out branches, out truncated);

    /// <summary>The lead-in plays, then this play (the state right after it resolves).</summary>
    public static void ApplyPlayTo(SimState s, IReadOnlyList<SimPlay>? leadIn, SimPlay play, out List<SimState> branches, out bool truncated)
    {
        var first = s.Clone();
        first.Work = new SimWork();
        var b = new BranchSet(first);
        if (leadIn != null) foreach (var queued in leadIn) ApplyPlay(b, queued);
        ApplyPlay(b, play);
        branches = b.States;
        truncated = b.Truncated;
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
            case CardResolvedAction cr: b.Do(s => CardResolved(s, cr)); break;
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
    /// The auto-play phase of your end of turn (CombatManager.EndPlayerTurnPhaseOneInternal: PlayerTurnPhase.AutoPostPlay,
    /// Hook.AfterAutoPostPlayPhaseEntered), before BeforeSideTurnEnd. Hook listeners run your powers first (Stampede,
    /// Simpleton), then the cards in your piles (I Am Invincible on top of your draw pile, Howl from Beyond in your
    /// exhaust pile). Auto-played cards cost nothing (CardCmd.AutoPlay); one aimed at an enemy takes a random hittable one.
    /// Yours only: a teammate's come from their hand (decision #25).
    /// </summary>
    private static void AutoPostPlay(BranchSet b)
    {
        if (!b.States.Any(s => s.P.Stampede > 0 || s.P.Simpleton > 0 || s.P.ExhaustAutoPlays.Count > 0 || s.P.InvincibleBlock > 0)) return;
        // Stampede: a random Attack from your hand (unplayable cards aren't in AutoHand), each time.
        var stampede = b.States.Max(s => s.P.Stampede);
        for (var i = 0; i < stampede; i++)
        {
            var n = i;
            b.Each(s => n < s.P.Stampede ? AutoPlayFromHand(b, s, hand => hand.Where(c => c.IsAttack).ToList()) : new[] { s });
        }
        // Simpleton: a random card among your cheapest, each time.
        var simpleton = b.States.Max(s => s.P.Simpleton);
        for (var i = 0; i < simpleton; i++)
        {
            var n = i;
            b.Each(s => n < s.P.Simpleton
                ? AutoPlayFromHand(b, s, hand => hand.Count == 0 ? hand : hand.Where(c => c.Cost == hand.Min(x => x.Cost)).ToList())
                : new[] { s });
        }
        // I Am Invincible on top of your draw pile: its block.
        b.Each(s =>
        {
            if (s.P.InvincibleBlock <= 0) return new[] { s };
            s.AutoPlayed++;
            return GainBlock(s, s.P.InvincibleBlock, "I Am Invincible");
        });
        // Howl from Beyond in your exhaust pile: each copy plays (an attack on every enemy).
        var howls = b.States.Max(s => s.P.ExhaustAutoPlays.Count);
        for (var i = 0; i < howls; i++)
        {
            var play = b.States.Select(s => s.P.ExhaustAutoPlays).First(l => l.Count > i)[i];
            b.Do(s => s.AutoPlayed++);
            ApplyPlay(b, play);
        }
    }

    /// <summary>
    /// One auto-play from your hand: a branch per card <paramref name="pick"/> allows (CombatCardSelection.NextItem: each
    /// equally likely), and per hittable target for a card aimed at one. A card whose play isn't simulated (it changes
    /// your hand) ends what's predicted for later auto-plays in that branch.
    /// </summary>
    private static IEnumerable<SimState> AutoPlayFromHand(BranchSet outer, SimState s, Func<List<SimHandCard>, List<SimHandCard>> pick)
    {
        if (s.AutoPlaysUnknown || s.P.AutoHand == null) return new[] { s };
        var cards = pick(s.P.AutoHand);
        if (cards.Count == 0) return new[] { s };
        var result = new List<SimState>();
        foreach (var card in cards)
        {
            var c = s.Clone();
            c.Weight = s.Weight / cards.Count;
            c.AutoPlayed++;
            c.P.AutoHand = c.P.AutoHand!.Where(x => x.Id != card.Id).ToList();
            if (card.Plays.Count == 0)
            {
                c.AutoPlaysUnknown = true;
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
                var sub = new BranchSet(t);
                ApplyPlay(sub, play);
                if (sub.Truncated) outer.Truncated = true;
                result.AddRange(sub.States);
            }
        }
        return result;
    }

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
            b.Each(s => PlayerOf(s, player) is { Fading: > 0 } p && TakesPart(s, player) ? ProcCardEvent(s, CardEvent.Exhausted, p.Fading, player, indirect: false) : new[] { s });
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

            // A teammate takes each hit in their turn order (CreatureCmd.Damage over every living player:
            // a hit still reaches everyone even if one player's Thorns kills the attacker during it).
            void TeammateTakesHit(int k, int h, int mine)
            {
                if (s.Teammates[k].Hp <= 0) return;
                var t = s.MutableTeammate(k);
                var amount = e.Intent != null ? IntentDamage(s, e, h, Who.Teammate(k))
                    : k < e.TeammateHits.Count && h < e.TeammateHits[k].Count ? e.TeammateHits[k][h] : mine;
                t.Incoming += amount;
                if (t.Thorns > 0) Retaliate(t.Thorns);
                var tBlocked = Math.Min(t.Block, amount);
                t.Block -= tBlocked;
                var tThrough = amount - tBlocked;
                var tOsty = t.OstyShields ? OstyLoses(s, Who.Teammate(k), tThrough) : 0;
                t = s.Teammates[k];
                t.OstyTaken += tOsty;
                t.Through += tThrough - tOsty;
                TeammateHpLoss(s, k, tThrough - tOsty, tBlocked, e.Id, SimProps.Move);
                if (!dead && t.FlameBarrier > 0) Retaliate(t.FlameBarrier);
                if (!dead && t.Reflect && tBlocked > 0) Retaliate(tBlocked);
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
                var fullHit = perHit;
                // Thorns: before the hit lands (BeforeDamageReceived: you're still its target with Wuppy out); the hit
                // still lands even if this kills.
                if (s.P.Thorns > 0) Retaliate(s.P.Thorns);
                // Wuppy takes the hit first while it lives (WuppysLoyaltyPower.ModifyPreBlockDamageTarget: before your block);
                // only what's left over when the hit kills it (OverkillDamage) goes on to your block and you.
                if (s.P.WuppyHp > 0 && perHit > 0)
                {
                    var toWuppy = Math.Min(s.P.WuppyHp, perHit);
                    s.P.WuppyHp -= toWuppy;
                    s.WuppyTaken += toWuppy; // not your Incoming Damage (landed): it never reaches you
                    perHit -= toWuppy;
                    if (perHit == 0)
                    {
                        // DamageResult.WasFullyBlocked: no HP lost and you had block (untouched).
                        if (hitsLanded == 0) e.FirstHitFullyBlocked = s.P.Block > 0;
                        hitsLanded++;
                        for (var k = before; k < s.Teammates.Count; k++) TeammateTakesHit(k, hitIndex, fullHit);
                        continue;
                    }
                }

                if (hitsLanded == 0 && e.Imbalanced) s.FullBlockNeeded[e.Id] = s.BlockDemand + perHit;
                s.BlockDemand += perHit;
                var blockBefore = s.P.Block;
                var blocked = Math.Min(s.P.Block, perHit);
                s.P.Block -= blocked;
                var through = perHit - blocked;
                var toOsty = s.P.OstyShields ? OstyLoses(s, Who.You, through) : 0;
                s.OstyTaken += toOsty;
                s.DamageThrough += through - toOsty;
                var lost = HpLoss(s, -1, through - toOsty, e.Id, SimProps.Move);
                YouLose(s, lost);
                PlayerDamaged(s, -1, e.Id, SimProps.Move, blocked, lost);
                // DamageResult.WasFullyBlocked: you had block and no damage got through after the
                // AfterOsty modifiers (Tungsten Rod, Buffer...). Block only drops during an attack, so
                // the first hit decides it.
                if (hitsLanded == 0) e.FirstHitFullyBlocked = perHit > 0 && (blocked > 0 || blockBefore > 0) && toOsty == 0 && lost == 0;
                landed += perHit;
                hitsLanded++;

                if (!dead && s.P.FlameBarrier > 0) Retaliate(s.P.FlameBarrier);
                if (!dead && s.P.Reflect && blocked > 0) Retaliate(blocked);
                for (var k = before; k < s.Teammates.Count; k++) TeammateTakesHit(k, hitIndex, fullHit);
            }
            if (landed > 0) if (s.Log) s.Blockable.Add((hitsLanded > 1 ? $"{e.Name} ({hitsLanded} hits)" : e.Name, landed));
        }
    }
}
