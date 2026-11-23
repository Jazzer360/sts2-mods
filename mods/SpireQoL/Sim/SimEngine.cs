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
        if (Branches.Count == 0) return new SimChance(Low: 0, Truncated ? 1 : 0, Seen: false);
        bool Known(SimState state) => (state.Unmodelled == null && !Truncated) || (lasting && happens(state));
        var seen = Branches.Any(branch => Known(branch) && happens(branch));
        var unknown = Branches.Where(branch => !Known(branch)).Sum(branch => branch.Weight);
        var total = Branches.Sum(branch => branch.Weight);
        if (total <= 0) return new SimChance(Low: 0, High: 0, seen);
        var chance = Branches.Where(branch => Known(branch) && happens(branch)).Sum(branch => branch.Weight) / total;
        return new SimChance(chance, Math.Min(1, chance + unknown / total), seen);
    }

    /// <summary>
    /// The chance the play's log holds something (a warning tab's corner: decision #159), from every branch merged into each (Fired) up
    /// to some (FiredAny). A model that fired stays fired, so a branch the simulation couldn't follow past that point counts it (a dev
    /// review: Neow's Fury's hit on Personal Hive showed 0%?, its pick from your discard pile not modelled after the Dazed were made).
    /// </summary>
    public SimChance LoggedChance(Func<NamedCounts, bool> fired) =>
        new(Chance(branch => fired(branch.Fired), lasting: true).Low, Chance(branch => fired(branch.FiredAny), lasting: true).High, Seen: true);

    // ---- the four questions every query below asks, each over the branches

    /// <summary>It holds in every branch, and the search finished (one stopped at a cap guarantees nothing it didn't reach).</summary>
    public bool InEvery(Func<SimState, bool> holds) => !Truncated && Branches.All(holds);

    /// <summary>It holds in some branch (a possibility: warnings need only one).</summary>
    public bool InSome(Func<SimState, bool> holds) => Branches.Any(holds);

    /// <summary>The smallest value over the branches (the guarantee for something good).</summary>
    public int Fewest(Func<SimState, int> valueOf) => Branches.Min(valueOf);

    /// <summary>The largest value over the branches (the worst case for something bad).</summary>
    public int Most(Func<SimState, int> valueOf) => Branches.Max(valueOf);

    /// <summary>Some branch has something not modelled (decision #92): what's shown carries the ?.</summary>
    /// <remarks>By index: the forecast's widgets read it every frame, and LINQ's Any boxed the list's enumerator each time (decision #177).</remarks>
    public bool AnyUnmodelled
    {
        get
        {
            for (var i = 0; i < Branches.Count; i++)
                if (Branches[i].Unmodelled != null) return true;
            return false;
        }
    }

    /// <summary>Why, over the branches: the gaps to model.</summary>
    public IReadOnlyList<string> UnmodelledReasons => Branches.Where(branch => branch.Unmodelled != null).Select(branch => branch.Unmodelled!).Distinct().ToList();

    /// <summary>The chance the enemy dies before it acts.</summary>
    /// <remarks>A search stopped at a cap before the enemy turn (decision #107): an enemy already dead there counts (no revive comes
    /// before it acts), so the range's floor is the deaths already settled.</remarks>
    public SimChance DeadChanceRange(int enemyId) => Chance(branch => branch.Enemy(enemyId) is { DiedBeforeActing: true } || (Truncated && !branch.EnemyTurnNow && branch.Enemy(enemyId) is { Alive: false, StartedAlive: true }), lasting: true);

    /// <summary>The chance the enemy turn kills you (nothing left to save you).</summary>
    public SimChance YouDieChance() => Chance(branch => branch.Died);

    /// <summary>
    /// The simulator's work, the same on every machine (the performance tests limit these, not time): steps applied to the
    /// branch set, branches stepped over all steps, state copies, the most copies one step made, and the most states held at once.
    /// </summary>
    public int Steps, Stepped, Copies, MaxStepCopies, PeakHeld;

    /// <summary>
    /// The branch shown as the turn's outcome: the most HP you lose, then Osty; among those, the least block you'd have (a roll
    /// can decide your end-of-turn block: I Am Invincible on top of a draw pile whose order isn't known), so what's shown never
    /// counts block you might not get.
    /// </summary>
    public SimState Worst => Branches.OrderByDescending(branch => branch.Taken).ThenByDescending(branch => branch.OstyTaken).ThenBy(branch => branch.BlockAtEnemyTurn).First();
    public int TakenMin => Fewest(branch => branch.Taken);
    public int TakenMax => Most(branch => branch.Taken);
    public int OstyTakenMin => Fewest(branch => branch.OstyTaken);
    public int OstyTakenMax => Most(branch => branch.OstyTaken);
    /// <summary>Teammate k's HP lost: the worst branch and the best.</summary>
    public int TeammateTakenMax(int teammateIndex) => Most(branch => branch.Teammates[teammateIndex].Taken);
    public int TeammateTakenMin(int teammateIndex) => Fewest(branch => branch.Teammates[teammateIndex].Taken);
    /// <summary>What teammate k's Osty takes: the worst branch and the best.</summary>
    public int TeammateOstyTakenMax(int teammateIndex) => Most(branch => branch.Teammates[teammateIndex].OstyTaken);
    public int TeammateOstyTakenMin(int teammateIndex) => Fewest(branch => branch.Teammates[teammateIndex].OstyTaken);
    /// <summary>Your Parrying Shield fires in every branch (the search finished) / in some branch.</summary>
    public bool ParryingShieldInAll => InEvery(branch => branch.ParryingShieldFired);
    public bool ParryingShieldInAny => InSome(branch => branch.ParryingShieldFired);
    /// <summary>Teammate k in the branch where they lose the most HP: their Incoming Damage.</summary>
    public SimPlayer TeammateWorst(int teammateIndex) => Branches.Select(branch => branch.Teammates[teammateIndex]).OrderByDescending(player => player.Taken).First();

    /// <summary>Enemies the play itself kills (before your end of turn) in some branch / in every branch.</summary>
    public HashSet<int> KilledByPlayInAny = new();
    public HashSet<int> KilledByPlayInAll = new();
    /// <summary>Enemies that gain block from Curl Up, Sneaky or Skittish because of the play, in some branch.</summary>
    public HashSet<int> BlockReactionInAny = new();
    /// <summary>The play breaks some enemy's block in every branch (Hand Drill).</summary>
    public bool PlayBreaksBlockInAll;
    /// <summary>Block gains during the play (each sets off Juggernaut): the most over branches.</summary>
    public int PlayBlockGainsMax;

    /// <summary>Stunned during your turn (Burrowed, Flutter, Plow, Shriek) and alive when its turn comes, in every branch.</summary>
    public bool StunnedInAll(int enemyId) => InEvery(branch => branch.Enemy(enemyId) is { StunnedThisTurn: true, DiedBeforeActing: false });
    public bool GambitInAll(int enemyId) => InEvery(branch => branch.Enemy(enemyId) is { GambitTriggered: true });
    public bool SlipperyInAny(int enemyId) => InSome(branch => branch.Enemy(enemyId) is { SlipperyTriggered: true });
    /// <summary>The play kills some enemy in every branch (not always the same one).</summary>
    public bool PlayKillsInAll;
    /// <summary>The play's biggest powered hit (blocked plus HP lost), in the branch where it's smallest (Mighty Wings).</summary>
    public int PlayBiggestHitMin;
    /// <summary>The play's HP loss of yours, in the branch where it's smallest (SimState.PlayHpReached, PlayHpLosses, PlayHpLost).</summary>
    public int PlayHpReachedMin, PlayHpLossesMin, PlayHpLostMin;
    /// <summary>What fired during the play (its log): the counts in every branch (smallest), and in some branch (largest).</summary>
    public NamedCounts PlayFiredEvery = NamedCounts.Empty, PlayFiredAny = NamedCounts.Empty;
    /// <summary>The first hit of the card's attack on each enemy (enemy enemyId: damage), the same in every branch; empty when it isn't.</summary>
    public IReadOnlyDictionary<int, int> PlayFirstHits = System.Collections.Immutable.ImmutableSortedDictionary<int, int>.Empty;

    /// <summary>The enemy is dead before it acts in every branch.</summary>
    public bool DeadInAll(int enemyId) => InEvery(branch => branch.Enemy(enemyId) is { DiedBeforeActing: true }); // a death that happened stands, modelled past it or not
    public bool DeadInAny(int enemyId) => InSome(branch => branch.Enemy(enemyId) is { DiedBeforeActing: true });
    /// <summary>The chance it dies before acting (NaN when the search stopped at its cap: unknown).</summary>
    public double DeadChance(int enemyId)
    {
        if (Truncated) return double.NaN;
        var total = Branches.Sum(branch => branch.Weight);
        return total <= 0 ? double.NaN : Branches.Where(branch => branch.Enemy(enemyId) is { DiedBeforeActing: true }).Sum(branch => branch.Weight) / total;
    }

    /// <summary>Damage instances through its block during your turn: fewest and most over branches.</summary>
    public int UnblockedMin(int enemyId) => Fewest(branch => branch.Enemy(enemyId)?.UnblockedInstances ?? 0);
    public int UnblockedMax(int enemyId) => Most(branch => branch.Enemy(enemyId)?.UnblockedInstances ?? 0);

    /// <summary>The same, counted until your end of turn is over (not its turn): what the outcome audit sees in your end of turn.</summary>
    public int UnblockedAtTurnEndMin(int enemyId) => Fewest(branch => branch.Enemy(enemyId)?.UnblockedAtTurnEnd ?? 0);
    public int UnblockedAtTurnEndMax(int enemyId) => Most(branch => branch.Enemy(enemyId)?.UnblockedAtTurnEnd ?? 0);

    public bool FirstHitBlockedInAll(int enemyId) => InEvery(branch => branch.Enemy(enemyId) is { FirstHitFullyBlocked: true });
    public bool FirstHitBlockedInAny(int enemyId) => InSome(branch => branch.Enemy(enemyId) is { FirstHitFullyBlocked: true });

    /// <summary>Its HP when your end of turn is over: lowest and highest over branches.</summary>
    public int HpAtTurnEndMin(int enemyId) => Fewest(branch => branch.Enemy(enemyId)?.HpAtTurnEnd ?? 0);
    public int HpAtTurnEndMax(int enemyId) => Most(branch => branch.Enemy(enemyId)?.HpAtTurnEnd ?? 0);
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
    public static SimOutcome Run(SimState start, SimPlay? play = null) => Run(start, play == null ? null : SimScript.Of(play), work: null);

    public static SimOutcome Run(SimState start, SimScript play) => Run(start, play, work: null);

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
        var branches = new BranchSet(first);
        var aliveAtStart = start.Enemies.Where(enemy => enemy.Alive).Select(enemy => enemy.Id).ToList();
        SimOutcome? outcome = null;
        try
        {
            try
            {
                if (play != null) RunScript(branches, play);
                // The play is over in every branch: its own results are exact from here, even if the end of turn hits the cap.
                outcome = PlayResults(branches.States, start, aliveAtStart, complete: true);
                AutoPostPlay(branches);
                EndOfTurn(branches);
                branches.Do(EnemyTurn);
            }
            catch (SimCapped)
            {
                // Stopped at a cap: the branches where it stopped, each part-way through (BranchSet keeps them whole).
                outcome ??= PlayResults(branches.States, start, aliveAtStart, complete: false);
                outcome.Truncated = true;
                outcome.CapHit = work.CapHit ?? "the work cap";
            }
            outcome.Branches = Detached(branches.States);
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
        var killedPerBranch = states.Select(branch => aliveAtStart.Where(id => branch.Enemy(id) is not { Alive: true }).ToHashSet()).ToList();
        // Enemies that gained block from Curl Up, Sneaky or Skittish because of the play, in some branch.
        var reacted = states.SelectMany(branch => branch.Enemies.Where(enemy => enemy.ReactedWithBlock).Select(enemy => enemy.Id)).ToHashSet();
        // Some enemy's block broken by the play, in every branch (Hand Drill).
        var breaksBlock = complete && states.All(branch => branch.Enemies.Any(enemy => enemy.BlockBroken && start.Enemy(enemy.Id) is { BlockBroken: false }));
        // Block gains during the play (each sets off Juggernaut).
        var gains = states.Select(branch => branch.You.BlockGains - start.You.BlockGains).ToList();
        int PlayMin(Func<SimState, int> valueOf) => !complete || states.Count == 0 ? 0 : states.Min(valueOf);
        // The play's log over its branches: in every branch (each merged one keeps its smallest), and in some branch.
        var firedEvery = !complete || states.Count == 0 ? NamedCounts.Empty
            : states.Skip(1).Aggregate(states[0].Fired, (acc, branch) => SimState.MergeLog(acc, branch.Fired, every: true));
        var firedAny = states.Aggregate(NamedCounts.Empty, (acc, branch) => SimState.MergeLog(acc, branch.FiredAny, every: false));
        // The first hit of the card's attack on each enemy, when nothing random came before it (the damage-formula audit).
        var firstHits = complete && states.Count > 0 && states.All(branch => ReferenceEquals(branch.FirstHits, states[0].FirstHits) || branch.FirstHits.SequenceEqual(states[0].FirstHits)) ? states[0].FirstHits : System.Collections.Immutable.ImmutableSortedDictionary<int, int>.Empty;
        return new SimOutcome
        {
            KilledByPlayInAny = killedPerBranch.SelectMany(killed => killed).ToHashSet(),
            KilledByPlayInAll = !complete || killedPerBranch.Count == 0
                ? new HashSet<int>()
                : killedPerBranch.Skip(1).Aggregate(new HashSet<int>(killedPerBranch[0]), (acc, killed) => { acc.IntersectWith(killed); return acc; }),
            PlayKillsInAll = complete && killedPerBranch.Count > 0 && killedPerBranch.All(killed => killed.Count > 0),
            BlockReactionInAny = reacted,
            PlayBreaksBlockInAll = breaksBlock,
            PlayBlockGainsMax = gains.Count > 0 ? gains.Max() : 0,
            PlayBiggestHitMin = PlayMin(branch => branch.PlayBiggestHit),
            PlayHpReachedMin = PlayMin(branch => branch.PlayHpReached),
            PlayHpLossesMin = PlayMin(branch => branch.PlayHpLosses),
            PlayHpLostMin = PlayMin(branch => branch.PlayHpLost),
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
        foreach (var state in states)
            foreach (var branch in f(state))
                Merge(next, seen, branch);
        return next;
    }

    /// <summary>Adds a branch to a step's results, or merges it into the identical one there (both shares of the rolls).</summary>
    private static void Merge(List<SimState> next, Dictionary<long, SimState> seen, SimState state)
    {
        var key = state.Key();
        if (seen.TryGetValue(key, out var same))
        {
            same.Weight += state.Weight;
            same.MergeResults(state); // not in the key: the guarantee is the smallest
            return;
        }
        seen[key] = state;
        next.Add(state);
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

        public BranchSet(SimState state, bool nested = false)
        {
            States = new List<SimState> { state };
            _nested = nested;
        }

        private SimWork? Work => States.Count > 0 ? States[0].Work : null;

        private void Count()
        {
            if (Work is { } work) { work.StartStep(); work.Steps++; work.Stepped += States.Count; }
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
                    foreach (var state in step(parent)) mine.Add(state);
                }
                catch (SimCapped) when (!_nested)
                {
                    // The work cap, part-way through this branch: it stays as far as it got (what happened in it stands), its children go.
                    parent.Weight = weight;
                    Stop(next, i);
                }
                foreach (var state in mine) Merge(next, seen, state);
                if (work != null && !work.Hold(next.Count + States.Count)) Stop(next, i + 1);
            }
            States = next;
            work?.Hold(next.Count);
        }

        /// <summary>The search stops here: what's stepped and every branch from <paramref name="from"/> on not yet stepped.</summary>
        private void Stop(List<SimState> next, int from)
        {
            if (Work is { } work) work.CapHit ??= "the work cap";
            next.AddRange(States.Skip(from));
            States = next;
            throw new SimCapped();
        }

        public void Do(Action<SimState> step)
        {
            Count();
            foreach (var state in States) step(state);
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
    internal static int Damage(SimState state, SimEnemy enemy, int amount, int? dealer = Who.You, SimProps props = SimProps.Unpowered, SimCard? card = null)
    {
        if (amount <= 0 || !enemy.Alive) return 0;
        var listeners = Listeners(state);
        if (!AnyListensToDamage(listeners)) return PlainDamage(state, enemy, amount, dealer, props);
        var ctx = new DamageCtx(dealer, enemy.Id, props, card, state.CurrentPlay);
        foreach (var (model, o) in listeners) amount = (int)Math.Min(amount, model.ModifyDamageCap(state, o, ctx));
        foreach (var (simModel, o) in listeners) simModel.BeforeDamageReceived(state, o, ctx, amount);
        if (!enemy.Alive) return 0;
        var hadBlock = enemy.Block > 0;
        var absorbed = props.HasFlag(SimProps.Unblockable) ? 0 : Math.Min(enemy.Block, amount);
        enemy.Block -= absorbed;
        if (hadBlock && enemy.Block == 0)
        {
            enemy.BlockBroken = true;
            foreach (var (model, o) in Listeners(state)) model.AfterBlockBroken(state, o, enemy.Id, dealer);
        }
        var hpBefore = enemy.Hp;
        var toHp = 0;
        if (amount - absorbed > 0)
        {
            toHp = Math.Max(0, (int)HpLossPipeline(state, ctx, amount - absorbed));
            enemy.Hp -= toHp;
            if (toHp > 0)
            {
                enemy.UnblockedInstances++; // UnblockedDamage is 0 when Hardened Shell stops it
            }
        }
        state.AttackDealt += absorbed + toHp;
        var hit = new DamageEvent(dealer, enemy.Id, props, card, absorbed + Math.Min(toHp, Math.Max(0, hpBefore)), absorbed, toHp, hpBefore > 0 && enemy.Hp <= 0);
        if (props.IsPoweredAttack() && dealer is Who.You or Who.YourOsty) BiggestHit(state, hit.Total);
        foreach (var (m, o) in Listeners(state)) m.AfterDamageGiven(state, o, hit);
        foreach (var (m, o) in Listeners(state)) m.AfterDamageReceived(state, o, hit);
        return toHp;
    }

    private static bool AnyListensToDamage(List<(SimModel Model, int Owner)> listeners)
    {
        foreach (var (model, _) in listeners) if (model.ListensToDamage) return true;
        return false;
    }

    /// <summary>Damage with no models listening (most fights): block, then HP; nothing else can react.</summary>
    private static int PlainDamage(SimState state, SimEnemy enemy, int amount, int? dealer, SimProps props)
    {
        var hadBlock = enemy.Block > 0;
        var absorbed = props.HasFlag(SimProps.Unblockable) ? 0 : Math.Min(enemy.Block, amount);
        enemy.Block -= absorbed;
        if (hadBlock && enemy.Block == 0) enemy.BlockBroken = true;
        var hpBefore = enemy.Hp;
        var toHp = amount - absorbed;
        if (toHp > 0)
        {
            enemy.Hp -= toHp;
            enemy.UnblockedInstances++;
        }
        else toHp = 0;
        state.AttackDealt += absorbed + toHp;
        if (props.IsPoweredAttack() && dealer is Who.You or Who.YourOsty) BiggestHit(state, absorbed + Math.Min(toHp, Math.Max(0, hpBefore)));
        return toHp;
    }

    /// <summary>
    /// Hook.ModifyHpLost*: every listener's BeforeOsty, BeforeOstyLate, AfterOsty, then AfterOstyLate term; a listener that
    /// changes the (whole-number) amount gets AfterModifyingHpLost (it flashes).
    /// </summary>
    internal static decimal HpLossPipeline(SimState state, DamageCtx ctx, decimal amount)
    {
        var listeners = Listeners(state);
        var changed = new List<(SimModel Model, int Owner)>();
        void Phase(Func<SimModel, int, decimal, decimal> term)
        {
            foreach (var (model, o) in listeners)
            {
                var before = amount;
                amount = term(model, o, amount);
                if (decimal.Truncate(before) != decimal.Truncate(amount)) changed.Add((model, o));
            }
        }
        Phase((model, owner, running) => model.ModifyHpLostBeforeOsty(state, owner, ctx, running));
        Phase((model, owner, running) => model.ModifyHpLostBeforeOstyLate(state, owner, ctx, running));
        Phase((model, owner, running) => model.ModifyHpLostAfterOsty(state, owner, ctx, running));
        Phase((model, owner, running) => model.ModifyHpLostAfterOstyLate(state, owner, ctx, running));
        foreach (var (simModel, o) in changed) simModel.AfterModifyingHpLost(state, o);
        return amount;
    }

    internal static void Stun(SimEnemy enemy)
    {
        enemy.Stunned = true;
        enemy.StunnedThisTurn = true;
    }

    /// <summary>
    /// A powered hit of yours or Osty's: its DamageResult.TotalDamage (blocked plus unblocked, which stops at the HP it had: the
    /// rest is overkill) for the Mighty Wings tab, and the legacy count of 30+ hits (SimPlay.BigHitThreshold).
    /// </summary>
    private static void BiggestHit(SimState state, int total)
    {
        if (total > state.PlayBiggestHit) state.PlayBiggestHit = total;
    }

    /// <summary>One hit on a random living enemy: a branch per possible target.</summary>
    private static IEnumerable<SimState> HitRandom(SimState state, Func<SimEnemy, int> amount, Action<SimState, SimEnemy>? then = null)
    {
        void Hit(SimState branch, SimEnemy enemy)
        {
            Damage(branch, enemy, amount(enemy));
            then?.Invoke(branch, enemy);
        }
        var targets = state.Targetable.Select(enemy => enemy.Id).ToList();
        if (targets.Count == 0)
        {
            yield return state;
            yield break;
        }
        if (targets.Count == 1)
        {
            Hit(state, state.Enemy(targets[0])!);
            yield return state;
            yield break;
        }
        foreach (var id in targets)
        {
            var branch = state.Clone();
            branch.Weight = state.Weight / targets.Count;
            Hit(branch, branch.Enemy(id)!);
            yield return branch;
        }
    }

    /// <summary>Unpowered damage from you to every hittable enemy (orbs, procs), each its own amount.</summary>
    private static void HitAll(SimState state, Func<SimEnemy, int> amount)
    {
        foreach (var enemy in state.Targetable.ToList()) Damage(state, enemy, amount(enemy));
    }

    /// <summary>Gaining block (amount &gt; 0) sets off Juggernaut on a random enemy.</summary>
    /// <param name="player">Who gains it (Who.You, Who.Teammate(k)).</param>
    private static IEnumerable<SimState> GainBlock(SimState state, int amount, string? log, int player = Who.You)
    {
        if (amount <= 0) return new[] { state };
        var simPlayer = state.MutablePlayer(player);
        var yours = player == Who.You;
        simPlayer.Block += amount;
        simPlayer.BlockGains++;
        if (!yours) simPlayer.BlockGained += amount;
        if (yours && log != null && state.Log) state.EndOfTurnBlock.Add((log, amount));
        IEnumerable<SimState> states = new[] { state };
        if (simPlayer.Juggernaut > 0)
        {
            var juggernaut = simPlayer.Juggernaut;
            if (yours && log != null && state.Log) state.EndOfTurnDamage.Add(("Juggernaut", juggernaut));
            states = HitRandom(state, _ => juggernaut, then: (branch, _) => branch.LogFired(PowerId.Juggernaut.Name, juggernaut, player));
        }
        // Beacon of Hope: half (whole block only) to every other living player; theirs can pass half of
        // that on once (the giver's is held while it gives, and the giver still gets block back).
        if (simPlayer.BeaconOfHope && !simPlayer.BeaconGiving && amount / 2 >= 1) states = Then(states, branch => ShareBlock(branch, player, amount / 2));
        return states;
    }

    private static IEnumerable<SimState> ShareBlock(SimState state, int giver, int half)
    {
        state.MutablePlayer(giver).BeaconGiving = true;
        IEnumerable<SimState> states = new[] { state };
        foreach (var other in state.PlayerIds)
        {
            if (other == giver || !state.TakesPart(other)) continue;
            states = Then(states, branch => GainBlock(branch, half, other == Who.You ? "Beacon of Hope" : null, other));
        }
        return states.Select(branch =>
        {
            branch.MutablePlayer(giver).BeaconGiving = false;
            return branch;
        }).ToList();
    }

    /// <param name="player">Whose orb it is: their Thunder hits again, and a teammate's Frost blocks for them, not you.</param>
    private static IEnumerable<SimState> Evoke(SimState state, SimOrb orb, int player = Who.You)
    {
        // An evoke at your end of turn (Consuming Shadow) is in the breakdown with the orbs' passives: its block is what the forecast
        // check compares with the block the game gave, its damage what the forecast log lists.
        var listed = state.Log && state.EndOfTurnNow && player == Who.You;
        switch (orb.Kind)
        {
            // Thunder hits the evoke's target again if it's still alive.
            case OrbKind.Lightning:
                if (listed && state.Targetable.Any()) state.EndOfTurnDamage.Add(("Lightning (evoked)", orb.Evoke));
                return HitRandom(state, _ => orb.Evoke, then: (branch, enemy) => { var thunder = branch.Player(player)!.Thunder; if (thunder > 0 && enemy.Alive) { Damage(branch, enemy, thunder); branch.LogFired(PowerId.Thunder.Name, thunder, player); } });
            case OrbKind.Frost:
                // During a play: its block, for the play audit (an orb the play channels and pushes out too: Chill into full slots).
                if (state.CurrentPlay != null && !state.EndOfTurnNow && orb.Evoke > 0) state.LogFired(SimKeys.FrostEvokeBlock, orb.Evoke, player);
                return GainBlock(state, orb.Evoke, listed ? "Frost (evoked)" : null, player);
            case OrbKind.Dark:
            {
                var weakest = state.Targetable.OrderBy(enemy => enemy.Hp).FirstOrDefault();
                if (listed && weakest != null) state.EndOfTurnDamage.Add(("Dark (evoked)", orb.Evoke));
                if (weakest != null) Damage(state, weakest, orb.Evoke);
                return new[] { state };
            }
            case OrbKind.Glass:
                if (listed && state.Targetable.Any()) state.EndOfTurnDamage.Add(("Glass (evoked, all)", orb.Evoke));
                HitAll(state, _ => orb.Evoke);
                return new[] { state };
            default:
                return new[] { state };
        }
    }

    /// <summary>
    /// HP a player loses from <paramref name="amount"/> past their block: the HP-loss pipeline (Tungsten Rod, Beating Remnant,
    /// Buffer, Intangible...). Apply it, then call PlayerDamaged.
    /// </summary>
    private static int HpLoss(SimState state, int player, int amount, int? dealer, SimProps props)
    {
        if (amount <= 0) return 0;
        if (Listeners(state).Count == 0) return amount;
        return Math.Max(0, (int)HpLossPipeline(state, new DamageCtx(dealer, player, props, state.CurrentPlay?.Card, state.CurrentPlay), amount));
    }

    /// <summary>After a player took damage (their HP already lower): AfterDamageGiven and AfterDamageReceived (Beating Remnant counts, Inferno...).</summary>
    private static void PlayerDamaged(SimState state, int player, int? dealer, SimProps props, int blocked, int lost)
    {
        if (blocked <= 0 && lost <= 0) return;
        var listeners = Listeners(state);
        if (listeners.Count == 0) return;
        var hit = new DamageEvent(dealer, player, props, state.CurrentPlay?.Card, blocked + lost, blocked, lost, Killed: false);
        foreach (var (model, o) in listeners) model.AfterDamageGiven(state, o, hit);
        foreach (var (simModel, o) in Listeners(state)) simModel.AfterDamageReceived(state, o, hit);
    }

    /// <summary>Counts HP loss of yours for the play's HP-loss tabs (SimState.PlayHpReached...): <paramref name="reaching"/> got
    /// past your block to the HP-loss modifiers, <paramref name="lost"/> is what you lost after them.</summary>
    private static void PlayHp(SimState state, int reaching, int lost)
    {
        if (reaching > 0) state.PlayHpReached++;
        if (lost <= 0) return;
        state.PlayHpLosses++;
        state.PlayHpLost += lost;
    }

    /// <summary>
    /// Blockable damage to a player: their block, then HP loss; Unpowered unless said. The same for every player (decision #93);
    /// yours also feeds the breakdown, Imbalanced's threshold and your play's HP-loss tabs.
    /// </summary>
    private static void LandBlockable(SimState state, string name, int amount, int player = Who.You, int? dealer = null, SimProps props = SimProps.Unpowered)
    {
        if (amount <= 0) return;
        var simPlayer = state.MutablePlayer(player);
        var yours = player == Who.You;
        simPlayer.Incoming += amount;
        if (yours)
        {
            state.BlockDemand += amount;
            if (state.Log) state.Blockable.Add((name, amount));
        }
        var blocked = Math.Min(simPlayer.Block, amount);
        simPlayer.Block -= blocked;
        simPlayer.Through += amount - blocked;
        var lost = HpLoss(state, player, amount - blocked, dealer, props);
        Lose(state, player, lost);
        if (yours) PlayHp(state, amount - blocked, lost);
        PlayerDamaged(state, player, dealer, props, blocked, lost);
    }

    /// <summary>An end-of-turn step's explosions, only in fights with an Explosive enemy (no extra step otherwise).</summary>
    private static void ExplosionCheck(BranchSet branches)
    {
        if (branches.States.Any(branch => branch.Enemies.Any(enemy => !enemy.Exploded && (enemy.Explosive > 0 || enemy.ExplosiveOnTeammates != null)))) branches.Do(branch => Explosions(branch));
    }

    /// <summary>
    /// Explosive (ExplosivePower.AfterDeath): an enemy holding it that died since the last check damages every living
    /// player, blocked by the block they have then. Runs where deaths are handled (AllyDeaths) and after each
    /// end-of-turn step, so the block it meets is the block at that point.
    /// </summary>
    private static void Explosions(SimState state)
    {
        foreach (var enemy in state.Enemies)
        {
            if (enemy.Alive || enemy.Exploded || !enemy.StartedAlive || (enemy.Explosive <= 0 && enemy.ExplosiveOnTeammates == null)) continue;
            enemy.Exploded = true;
            if (state.You.Hp > 0) LandBlockable(state, enemy.Name + " Explosive", enemy.Explosive, dealer: enemy.Id);
            for (var teammateIndex = 0; teammateIndex < state.Teammates.Count; teammateIndex++)
            {
                if (state.Teammates[teammateIndex].Hp <= 0) continue;
                var amount = enemy.ExplosiveOnTeammates is { } on && teammateIndex < on.Count ? on[teammateIndex] : enemy.Explosive;
                LandBlockable(state, enemy.Name + " Explosive", amount, Who.Teammate(teammateIndex), enemy.Id);
            }
        }
    }

    /// <summary>Damage to every hittable enemy (Inferno and the like: Unpowered, their block first), dealt by <paramref name="dealer"/>.</summary>
    internal static void DamageAllEnemies(SimState state, int amount, int dealer)
    {
        foreach (var enemy in state.Targetable.ToList()) Damage(state, enemy, amount, dealer);
    }

    /// <summary>
    /// A player loses HP (after block and the HP-loss modifiers). Taken counts it all; their HP, when tracked, goes down, and
    /// at 0 something prevents death if it can (CreatureCmd.Kill → Hook.ShouldDie: Fairy in a Bottle first, then
    /// ShouldDieLate: Lizard Tail), healing them from 0; the rest of that hit is lost. With nothing left, they die. The same
    /// for every player, with their own potions and relics (decision #93).
    /// </summary>
    private static void Lose(SimState state, int player, int lost)
    {
        var simPlayer = state.MutablePlayer(player);
        simPlayer.Taken += lost;
        if (lost <= 0 || simPlayer.Hp == int.MaxValue || simPlayer.Died) return;
        simPlayer.Hp -= lost;
        if (simPlayer.Hp > 0) return;
        if (simPlayer.Fairies > 0)
        {
            simPlayer.Fairies--;
            simPlayer.FairiesUsed++;
            simPlayer.Hp = simPlayer.FairyHeal;
        }
        else if (simPlayer.LizardTailHeal > 0)
        {
            simPlayer.Hp = simPlayer.LizardTailHeal;
            simPlayer.LizardTailHeal = 0;
            simPlayer.LizardTailUsed = true;
        }
        else
        {
            simPlayer.Hp = 0;
            simPlayer.Died = true;
        }
    }

    /// <summary>Damage no block can stop, to a player (Regret, Beckon, Bad Luck): straight to the HP-loss pipeline.</summary>
    private static void LandUnblockable(SimState state, string name, int amount, int player = Who.You, int? dealer = null, SimProps props = SimProps.Unblockable | SimProps.Unpowered)
    {
        if (amount <= 0) return;
        var yours = player == Who.You;
        if (yours && state.Log) state.Unblockable.Add((name, amount));
        var lost = HpLoss(state, player, amount, dealer, props);
        Lose(state, player, lost);
        state.MutablePlayer(player).UnblockableTaken += lost;
        if (yours) PlayHp(state, amount, lost);
        PlayerDamaged(state, player, dealer, props, blocked: 0, lost);
    }

    // ------------------------------------------------------------------ card play

    /// <summary>A script on its own (the state right after it), for tests of plays.</summary>
    public static void RunScriptTo(SimState state, SimScript script, out List<SimState> branches, out bool truncated)
    {
        var otherBranches = Scratch(state, out var work);
        truncated = Capped(work, () => RunScript(otherBranches, script));
        branches = Detached(otherBranches.States);
    }

    /// <summary>This play on its own: the state right after it resolves, per branch.</summary>
    public static void ApplyPlayTo(SimState state, SimPlay play, out List<SimState> branches, out bool truncated)
    {
        var otherBranches = Scratch(state, out var work);
        truncated = Capped(work, () => ApplyPlay(otherBranches, play));
        branches = Detached(otherBranches.States);
    }

    /// <summary>
    /// The search is over: its branches stop counting against its work (a copy made from them later, by a check or a test, is
    /// nobody's, and never meets the search's caps).
    /// </summary>
    private static List<SimState> Detached(List<SimState> states)
    {
        foreach (var state in states) state.Work = null;
        return states;
    }

    /// <summary>A branch set of its own for a script on its own (RunScriptTo, ApplyPlayTo).</summary>
    private static BranchSet Scratch(SimState state, out SimWork work)
    {
        var first = state.Clone();
        first.Work = work = new SimWork();
        return new BranchSet(first);
    }

    /// <summary>Runs <paramref name="search"/> to the end or the memory cap (true), then frees what it held.</summary>
    private static bool Capped(SimWork work, Action search)
    {
        try
        {
            search();
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

    private static void ApplyPlay(BranchSet branches, SimPlay play) => RunScript(branches, SimScript.Of(play));

    /// <summary>Runs a script's actions in order on the branch set.</summary>
    private static void RunScript(BranchSet branches, SimScript script)
    {
        foreach (var action in script.Actions) Run(branches, action);
    }

    private static void Run(BranchSet branches, SimAction action)
    {
        switch (action)
        {
            case FixedHitsAction fixedHitsAction: RunFixedHits(branches, fixedHitsAction); break;
            case HitYouAction hitYouAction: branches.Do(branch => LandBlockable(branch, hitYouAction.Name, hitYouAction.Amount)); break;
            case FirstPlayOnly firstPlayOnly: if (branches.States.Count > 0 && branches.States[0].CurrentPlay is not { PlayIndex: > 0 }) foreach (var simAction in firstPlayOnly.Actions) Run(branches, simAction); break;
            case CardResolvedAction cardResolvedAction:
                branches.Do(branch => CardResolved(branch, cardResolvedAction));
                HookStep(branches, nameof(SimModel.AfterPlayResolved), (model, owner, branch) => model.AfterPlayResolved(branch, owner, cardResolvedAction.Owner));
                break;
            case OrbAction orbAction: RunOrb(branches, orbAction); break;
            case FocusAction focusAction: branches.Do(branch => ChangeFocus(branch, focusAction.Delta)); break;
            case ShootAction: RunShoot(branches); break;
            case ImpaleAction: RunImpale(branches); break;
            case DislodgeAllAction: RunDislodgeAll(branches); break;
            case DamageAction damageAction: RunDamage(branches, damageAction); break;
            case ExecuteAction ex: RunExecute(branches, ex); break;
            case StripBlockAction stripBlockAction: branches.Do(branch => StripBlock(branch, stripBlockAction)); break;
            case TriggerPoisonAction: branches.Each(TriggerPoison); break;
            case KillOstyAction: branches.Do(branch => OstyLoses(branch, Who.You, branch.You.OstyHp)); break;
            case CardPlayAction cardPlayAction: RunCardPlay(branches, cardPlayAction); break;
            case AttackAction at: RunAttack(branches, at); break;
            case GainBlockAction gainBlockAction: RunBlock(branches, gainBlockAction); break;
            case ApplyPowerAction applyPowerAction: RunApplyPower(branches, applyPowerAction); break;
            case CardEventAction cardEventAction: RunCardEvent(branches, cardEventAction); break;
            case DrawnCardAction drawnCardAction: branches.Each(branch => RunDrawnCard(branch, drawnCardAction)); break;
            case AutoPlayFromDrawAction autoPlayFromDrawAction:
                branches.Each(branch => AutoPlayFromDraw(branch, branch.CurrentPlay?.Owner ?? Who.You, autoPlayFromDrawAction.Count, autoPlayFromDrawAction.ForceExhaust, autoPlayFromDrawAction.Upgrade));
                break;
            case ShuffleDrawPileAction shuffleDrawPileAction:
                branches.Do(branch =>
                {
                    var owner = branch.CurrentPlay?.Owner ?? Who.You;
                    if (DrawsFromPile(branch, owner)) branch.MutablePlayer(owner).Draw = branch.Player(owner)!.Draw!.ShuffledWithDiscard(shuffleDrawPileAction.HandIn);
                });
                break;
            case TakeToHandAction takeToHandAction: branches.Each(branch => RunTakeToHand(branch, takeToHandAction)); break;
            case ExhaustFromHandAction exhaustFromHandAction: branches.Each(branch => RunExhaustFromHand(branch, exhaustFromHandAction)); break;
            case LeaveHandAction leaveHandAction: branches.Do(branch => RunLeaveHand(branch, leaveHandAction)); break;
            case NotModelledAction notModelledAction: branches.Do(branch => branch.Unmodelled ??= notModelledAction.Reason); break;
            case UnreadAction unreadAction: branches.Do(branch => branch.Unmodelled ??= SimState.Unread(unreadAction.Member)); break;
            case GainEnergyAsHeldAction:
                branches.Each(branch => (branch.CurrentPlay?.Owner ?? Who.You) is var playOwner && branch.Player(playOwner) is { Energy: > 0 } player
                    ? ProcCardEvent(branch, CardEvent.EnergyGained, player.Energy, playOwner, indirect: false) : new[] { branch });
                break;
            case LoseHpAction loseHpAction: branches.Do(branch => LandUnblockable(branch, branch.CurrentPlay?.Card.Name ?? "HP cost", loseHpAction.Amount)); break;
            default: throw new NotSupportedException($"No runner for {action.GetType().Name}");
        }
        // Events raised inside its hits or deaths (an arrow knocked loose) run once the action is over.
        if (branches.States.Any(branch => !branch.Pending.IsEmpty || !branch.PendingIntoDraw.IsEmpty || !branch.PendingSpawns.IsEmpty)) branches.Each(DrainPending);
    }

    // ------------------------------------------------------------------ end of turn

    /// <summary>One player's orb passives at end of turn, front first (yours, or a teammate's: their Frost blocks for them).</summary>
    private static void OrbPassives(BranchSet branches, int player)
    {
        var maxOrbs = branches.States.Max(branch => branch.Player(player)!.Orbs.Count);
        for (var i = 0; i < maxOrbs; i++)
        {
            var index = i;
            int Triggers(SimState state)
            {
                var simPlayer = state.Player(player)!;
                return index < simPlayer.Orbs.Count ? simPlayer.Orbs[index].Triggers + (index == 0 ? simPlayer.FrontOrbBonusTriggers : 0) : 0;
            }
            var maxTriggers = branches.States.Max(Triggers);
            for (var triggerIndex = 0; triggerIndex < maxTriggers; triggerIndex++)
            {
                var trigger = triggerIndex;
                branches.Each(branch =>
                {
                    var simPlayer = branch.Player(player)!;
                    if (index >= simPlayer.Orbs.Count) return new[] { branch };
                    var orb = simPlayer.Orbs[index];
                    if (trigger >= Triggers(branch)) return new[] { branch };
                    switch (orb.Kind)
                    {
                        case OrbKind.Lightning:
                            if (branch.Log) branch.EndOfTurnDamage.Add(("Lightning", orb.Passive));
                            return HitRandom(branch, _ => orb.Passive);
                        case OrbKind.Frost:
                            return GainBlock(branch, orb.Passive, player == Who.You ? "Frost" : null, player);
                        case OrbKind.Dark:
                            branch.MutablePlayer(player).OwnOrbs()[index].Evoke += orb.Passive;
                            return new[] { branch };
                        case OrbKind.Glass:
                            // GlassOrb.Passive: if PassiveVal (base + Focus) > 0, the base drops by 1
                            // (not below 0) and every enemy takes the value from before the drop.
                            if (orb.Passive > 0)
                            {
                                orb = branch.MutablePlayer(player).OwnOrbs()[index];
                                var passive = orb.Passive;
                                if (branch.Log) branch.EndOfTurnDamage.Add(("Glass (all)", passive));
                                HitAll(branch, _ => passive);
                                orb.GlassBase = Math.Max(0, orb.GlassBase - 1);
                                orb.Passive = Math.Max(0, orb.GlassBase + orb.GlassBonus);
                                orb.Evoke = orb.Passive * 2; // GlassOrb.EvokeVal => PassiveVal * 2
                            }
                            return new[] { branch };
                        case OrbKind.Unknown:
                            // An orb the simulator doesn't know (another mod's): what its passive does isn't modelled.
                            branch.Unmodelled ??= "an orb the simulator doesn't know acts at the end of the turn";
                            return new[] { branch };
                        default:
                            return new[] { branch }; // Plasma: its energy comes at the start of your turn
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
    private static void AutoPostPlay(BranchSet branches)
    {
        static bool Any(SimPlayer player) => player.Stampede > 0 || player.Simpleton > 0 || player.ExhaustAutoPlays.Count > 0 || player.InvincibleBlock > 0 || player.Draw?.HasInvincible == true;
        if (!branches.States.Any(branch => Any(branch.You) || branch.Teammates.Any(Any))) return;
        // Your turn is over: block gained now (the auto-played card's, Daughter of the Wind's on it) is end-of-turn block in the
        // Incoming Damage breakdown, as the forecast check counts it.
        branches.Do(branch => branch.EndOfTurnNow = true);
        foreach (var player in branches.States[0].PlayerIds) AutoPostPlayOf(branches, player);
    }

    /// <summary>One player's auto-plays.</summary>
    private static void AutoPostPlayOf(BranchSet branches, int player)
    {
        var yours = player == Who.You;
        SimPlayer P(SimState state) => state.Player(player)!;
        bool Alive(SimState state) => state.TakesPart(player);
        // Stampede: a random Attack from their hand (unplayable cards aren't in AutoHand), each time.
        var stampede = branches.States.Max(branch => P(branch).Stampede);
        for (var i = 0; i < stampede; i++)
        {
            var stampedeIndex = i;
            branches.Each(branch => stampedeIndex < P(branch).Stampede && Alive(branch) ? AutoPlayFromHand(branch, player, hand => hand.Where(handCard => handCard.IsAttack).ToList()) : new[] { branch });
        }
        // Simpleton: a random card among their cheapest, each time.
        var simpleton = branches.States.Max(branch => P(branch).Simpleton);
        for (var i = 0; i < simpleton; i++)
        {
            var simpletonIndex = i;
            branches.Each(branch => simpletonIndex < P(branch).Simpleton && Alive(branch)
                ? AutoPlayFromHand(branch, player, hand => hand.Count == 0 ? hand : hand.Where(handCard => handCard.Cost == hand.Min(simHandCard => simHandCard.Cost)).ToList())
                : new[] { branch });
        }
        // I Am Invincible on top of their draw pile: its block. With your draw pile (decision #91): certain when it's known to be
        // on top, else a chance (how many of the unordered rest are copies of it).
        branches.Each(branch =>
        {
            if (P(branch).Draw is { } pile && Alive(branch)) return InvincibleFromPile(branch, player, pile);
            if (P(branch).InvincibleBlock <= 0 || !Alive(branch)) return new[] { branch };
            if (yours) branch.AutoPlayed++; // the outcome audit counts your auto-plays
            return GainBlock(branch, P(branch).InvincibleBlock, "I Am Invincible", player);
        });
        // Howl from Beyond in their exhaust pile: each copy plays (an attack on every enemy).
        var howls = branches.States.Max(branch => P(branch).ExhaustAutoPlays.Count);
        for (var i = 0; i < howls; i++)
        {
            var play = branches.States.Select(branch => P(branch).ExhaustAutoPlays).First(plays => plays.Count > i)[i];
            if (!branches.States.Any(Alive)) break;
            if (yours) branches.Do(branch => branch.AutoPlayed++);
            ApplyPlay(branches, play);
        }
    }

    /// <summary>
    /// I Am Invincible (IAmInvincible.AfterAutoPostPlayPhaseEntered): a copy that's the top card of your draw pile plays itself,
    /// gaining its block, then goes to the discard pile. The known top decides it; else the top is any card of the rest, each
    /// equally likely: a branch where it's a copy (their share), and one where it isn't (nothing happens; which card it is
    /// stays unknown, close enough for the rest of the turn).
    /// </summary>
    private static IEnumerable<SimState> InvincibleFromPile(SimState state, int player, SimDrawPile pile)
    {
        IEnumerable<SimState> Play(SimState branch, SimDrawCard card, SimDrawPile after)
        {
            branch.MutablePlayer(player).Draw = after.WithDiscarded(card);
            if (player == Who.You) branch.AutoPlayed++;
            return GainBlock(branch, card.InvincibleBlock, "I Am Invincible", player);
        }
        if (pile.Top.Count > 0)
            return pile.Top[0].InvincibleBlock > 0 ? Play(state, pile.Top[0], pile.WithoutTop()) : new[] { state };
        var total = pile.RestCount;
        var copies = pile.Rest.Select((group, index) => (group, index)).Where(candidate => candidate.group.Card.InvincibleBlock > 0).ToList();
        if (total == 0 || copies.Count == 0) return new[] { state };
        var result = new List<SimState>();
        var other = total - copies.Sum(copy => copy.group.Count);
        foreach (var (copy, i) in copies)
        {
            var branch = state.Clone();
            branch.Weight = state.Weight * copy.Count / total;
            result.AddRange(Play(branch, copy.Card, pile.WithoutOneOf(i)));
        }
        if (other > 0)
        {
            var branch = state.Clone();
            branch.Weight = state.Weight * other / total;
            result.Add(branch);
        }
        return result;
    }

    /// <summary>
    /// One auto-play from a player's hand: a branch per card <paramref name="pick"/> allows
    /// (CombatCardSelection.NextItem: each equally likely), and per hittable target for a card aimed at one (SimPlay.TargetId). A card that changes the hand (it draws or makes cards) still plays; later auto-plays in
    /// that branch aren't predicted (CardResolved). One with no play built ends them too.
    /// </summary>
    private static IEnumerable<SimState> AutoPlayFromHand(SimState state, int player, Func<List<SimHandCard>, List<SimHandCard>> pick)
    {
        var yours = player == Who.You;
        var hand = state.Player(player)!.AutoHand;
        if (hand == null) return new[] { state };
        if (state.AutoPlaysUnknown)
        {
            // A pick is due from a hand the simulator lost track of (a card made or brought back cards, one drawn can't be played).
            state.Unmodelled ??= "an end-of-turn auto-play from a hand that changed in a way not simulated";
            return new[] { state };
        }
        var cards = pick(hand);
        if (cards.Count == 0) return new[] { state };
        var result = new List<SimState>();
        foreach (var card in cards)
        {
            var branch = state.Clone();
            branch.Weight = state.Weight / cards.Count;
            if (yours) branch.AutoPlayed++;
            var simPlayer = branch.MutablePlayer(player);
            simPlayer.AutoHand = simPlayer.AutoHand!.Where(handCard => handCard.Id != card.Id).ToList();
            if (card.Plays.Count == 0)
            {
                branch.AutoPlaysUnknown = true;
                branch.Unmodelled ??= $"{card.Name} auto-played at end of turn: its play isn't simulated";
                if (yours) branch.AutoPicks = [..branch.AutoPicks, new SimPick(card.Key, Target: Who.None)];
                result.Add(branch);
                continue;
            }
            var plays = card.Plays.Count == 1 && card.Plays[0].TargetId < 0
                ? card.Plays
                : card.Plays.Where(play => branch.Enemy(play.TargetId) is { Alive: true, Hittable: true }).ToList();
            if (plays.Count == 0)
            {
                result.Add(branch); // no target: it goes to the discard pile unplayed (CardCmd.AutoPlay)
                continue;
            }
            foreach (var play in plays)
            {
                var copy = plays.Count == 1 ? branch : branch.Clone();
                copy.Weight = branch.Weight / plays.Count;
                if (yours) copy.AutoPicks = [..copy.AutoPicks, new SimPick(card.Key, play.TargetId)];
                var sub = new BranchSet(copy, nested: true);
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
    private static int Exhausting(SimState state, SimPlayer player)
        => player.HexFading > 0 && player.HexFrom >= 0 && state.Enemy(player.HexFrom) is { Alive: false } ? Math.Max(0, player.Fading - player.HexFading) : player.Fading;

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
    private static void EndOfTurn(BranchSet branches)
    {
        branches.Do(branch => branch.EndOfTurnNow = true);
        HookStep(branches, nameof(SimModel.BeforeSideTurnEndVeryEarly), (model, owner, branch) => Who.IsPlayer(owner) ? model.BeforeSideTurnEndVeryEarly(branch, owner) : new[] { branch });
        HookStep(branches, nameof(SimModel.BeforeSideTurnEndEarly), (model, owner, branch) => Who.IsPlayer(owner) ? model.BeforeSideTurnEndEarly(branch, owner) : new[] { branch });
        ExplosionCheck(branches);
        HookStep(branches, nameof(SimModel.BeforeSideTurnEnd), (model, owner, branch) => Who.IsPlayer(owner) ? model.BeforeSideTurnEnd(branch, owner) : new[] { branch });
        ExplosionCheck(branches);

        // 2. Each player's DoTurnEnd, in player order.
        foreach (var player in branches.States[0].PlayerIds)
        {
            OrbPassives(branches, player);
            ExplosionCheck(branches); // kills by orb passives
            branches.Each(branch => branch.Player(player) is { } simPlayer && Exhausting(branch, simPlayer) is > 0 and var exhausting && branch.TakesPart(player) ? ProcCardEvent(branch, CardEvent.Exhausted, exhausting, player, indirect: false) : new[] { branch });
            branches.Do(branch => CardsActInHand(branch, player));
            ExplosionCheck(branches);
        }

        // 3. AfterSideTurnEnd (Constrict, Magic Bomb, Demise, Consuming Shadow, Parrying Shield, Lunar Pastry), then
        // AfterSideTurnEndLate (Disintegration, Insatiable Dominion).
        HookStep(branches, nameof(SimModel.AfterSideTurnEnd), (model, owner, branch) => Who.IsPlayer(owner) ? model.AfterSideTurnEnd(branch, owner) : new[] { branch });
        ExplosionCheck(branches); // kills by Consuming Shadow's evokes, Parrying Shield, Black Hole
        HookStep(branches, nameof(SimModel.AfterSideTurnEndLate), (model, owner, branch) => Who.IsPlayer(owner) ? model.AfterSideTurnEndLate(branch, owner) : new[] { branch });

        // Deaths at the end of your turn run their hooks before the enemies act (arrows coming back: Twine's block).
        branches.Do(AllyDeaths);
        if (branches.States.Any(branch => !branch.Pending.IsEmpty || !branch.PendingIntoDraw.IsEmpty || !branch.PendingSpawns.IsEmpty)) branches.Each(DrainPending);
        branches.Do(branch =>
        {
            branch.EndOfTurnNow = false;
            branch.BlockAtEnemyTurn = branch.You.Block;
            foreach (var enemy in branch.Enemies) { enemy.HpAtTurnEnd = Math.Max(0, enemy.Hp); enemy.UnblockedAtTurnEnd = enemy.UnblockedInstances; }
        });
    }

    // ------------------------------------------------------------------ enemy turn

    /// <summary>
    /// Your Focus changes: each orb's values move by what Focus does to its base value
    /// (FocusPower.ModifyOrbValue: base + Focus, not below 0), so other modifiers on it stay.
    /// Lightning 3/8, Frost 2/5, Dark's passive 6 (its evoke is what it stored), Glass from its base
    /// (evoke twice the passive). Plasma doesn't use Focus.
    /// </summary>
    private static void ChangeFocus(SimState state, int delta)
    {
        int Shift(int value, int baseValue) => value + Math.Max(0, baseValue + state.You.Focus + delta) - Math.Max(0, baseValue + state.You.Focus);
        foreach (var orb in state.You.OwnOrbs())
        {
            switch (orb.Kind)
            {
                case OrbKind.Lightning: orb.Passive = Shift(orb.Passive, baseValue: 3); orb.Evoke = Shift(orb.Evoke, baseValue: 8); break;
                case OrbKind.Frost: orb.Passive = Shift(orb.Passive, baseValue: 2); orb.Evoke = Shift(orb.Evoke, baseValue: 5); break;
                case OrbKind.Dark: orb.Passive = Shift(orb.Passive, baseValue: 6); break;
                case OrbKind.Glass:
                    orb.Passive = Shift(orb.Passive, orb.GlassBase);
                    orb.GlassBonus += delta;
                    orb.Evoke = orb.Passive * 2;
                    break;
            }
        }
        state.You.Focus += delta;
    }

    private static void StripBlock(SimState state, StripBlockAction stripBlockAction)
    {
        if (state.Enemy(state.CurrentPlay?.Target ?? Who.None) is not { Alive: true } enemy) return;
        if (enemy.Block > 0)
        {
            enemy.Block = 0;
            enemy.BlockBroken = true;
            foreach (var (model, o) in Listeners(state)) model.AfterBlockBroken(state, o, enemy.Id, state.CurrentPlay?.Owner);
        }
        if (stripBlockAction.RemoveArtifact && PowersOf(state, enemy.Id).Has(PowerId.Artifact)) SetPowers(state, enemy.Id, PowersOf(state, enemy.Id).Without(PowerId.Artifact));
    }

    /// <summary>
    /// Enemies react to an ally's death as it happens (AfterDeath): Ravenous is stunned (and gains
    /// Strength for later turns), Crab Rage gains Strength (its hits) and block, once.
    /// </summary>
    private static void AllyDeaths(SimState state)
    {
        Explosions(state);
        // AfterDeath, once for each enemy that has died (Crab Rage, Ravenous as models). A main enemy's death kills the enemies left
        // when every one of them is a Minion (CreatureCmd.Kill: isPrimaryEnemy, teammates all IsSecondaryEnemy), whose deaths are
        // handled the same way, in turn.
        bool handled;
        do
        {
            handled = false;
            var mainDied = false;
            foreach (var dead in state.Enemies)
            {
                if (dead.Alive || dead.DeathHandled || !dead.StartedAlive) continue;
                dead.DeathHandled = true;
                handled = true;
                if (!PowersOf(state, dead.Id).Has(PowerId.Minion)) mainDied = true;
                foreach (var (model, o) in Listeners(state)) model.AfterDeath(state, o, dead.Id);
            }
            var left = state.Enemies.Where(enemy => enemy.Alive).ToList();
            if (mainDied && left.Count > 0 && left.All(enemy => PowersOf(state, enemy.Id).Has(PowerId.Minion)))
                foreach (var minion in left) minion.Hp = 0;
        } while (handled);
        foreach (var enemy in state.Enemies)
        {
            if (!enemy.Alive || !state.Enemies.Any(simEnemy => simEnemy != enemy && simEnemy.StartedAlive && !simEnemy.Alive)) continue;
            if (enemy.HitsAfterAllyDies != null)
            {
                enemy.Hits = enemy.HitsAfterAllyDies;
                enemy.HitsAfterAllyDies = null;
                enemy.Block += enemy.CrabRageBlock;
            }
        }
    }

    private static void EnemyTurn(SimState state)
    {
        AllyDeaths(state); // deaths at your end of turn
        state.EnemyTurnNow = true; // Inferno and the like react only on their owner's side's turn
        // Every enemy's poison ticks as the enemies' turn starts (PoisonPower.AfterSideTurnStart, before ExecuteEnemyTurn), stunned
        // or not, and Hardened Shell's budget resets then. The tick is damage (PoisonPower.Trigger: CreatureCmd.Damage, Unblockable |
        // Unpowered, no dealer): the HP-loss pipeline caps it (Hardened Shell, Slippery) and the damage hooks hear it (Shriek stuns at
        // its threshold, Asleep wakes, Slumber and Plow count it). Its deaths run their hooks before any enemy moves (Ravenous stuns
        // the other Corpse Slug; Crab Rage).
        var poisoned = new HashSet<int>();
        foreach (var enemy in state.Enemies.ToList())
        {
            if (!enemy.Alive) continue;
            enemy.ShellLeft = enemy.ShellPerTurn;
            if (enemy.Poison > 0) Damage(state, enemy, enemy.Poison, dealer: null, SimProps.Unblockable | SimProps.Unpowered);
            if (enemy.Alive) continue;
            poisoned.Add(enemy.Id);
            enemy.DiedBeforeActing = true;
            if (HitCount(enemy) > 0) state.SkippedPoisoned++;
            Explosions(state);
        }
        if (poisoned.Count > 0) AllyDeaths(state);
        foreach (var enemy in state.Enemies)
        {
            if (poisoned.Contains(enemy.Id)) continue;
            if (!enemy.Alive)
            {
                enemy.DiedBeforeActing = true;
                if (HitCount(enemy) > 0) state.SkippedEndOfTurn++;
                continue;
            }
            if (enemy.Stunned || HitCount(enemy) == 0) continue; // a stunned enemy skips its move
            var enemyBlock = enemy.Barricade ? enemy.Block : 0;

            var landed = 0;
            var hitsLanded = 0;
            var dead = false;
            void Retaliate(int amount)
            {
                if (amount <= 0) return;
                amount = Math.Min(amount, enemy.HitCap);
                var absorbed = Math.Min(enemyBlock, amount);
                enemyBlock -= absorbed;
                var toHp = Math.Min(amount - absorbed, Math.Max(0, enemy.ShellLeft));
                if (enemy.ShellLeft != int.MaxValue) enemy.ShellLeft -= toHp;
                enemy.Hp -= toHp;
                if (enemy.Hp <= 0) dead = true;
            }

            // One player takes one hit, the same for every player (decision #93): Thorns before it
            // lands (BeforeDamageReceived; the hit still lands if that kills), Wuppy first while it lives (WuppysLoyaltyPower.
            // ModifyPreBlockDamageTarget: before block; only the overkill goes on), block, Osty (Die For You), HP, then Flame
            // Barrier and Reflect. Yours also decides Imbalanced and feeds the breakdown.
            void TakesHit(int player, int amount)
            {
                var yours = player == Who.You;
                var simPlayer = state.MutablePlayer(player);
                if (simPlayer.Thorns > 0) Retaliate(simPlayer.Thorns);
                if (simPlayer.WuppyHp > 0 && amount > 0)
                {
                    var toWuppy = Math.Min(simPlayer.WuppyHp, amount);
                    simPlayer.WuppyHp -= toWuppy;
                    simPlayer.WuppyTaken += toWuppy; // not their Incoming Damage (landed): it never reaches them
                    amount -= toWuppy;
                    if (amount == 0)
                    {
                        if (!yours) return;
                        // DamageResult.WasFullyBlocked: no HP lost and you had block (untouched).
                        if (hitsLanded == 0) enemy.FirstHitFullyBlocked = state.You.Block > 0;
                        hitsLanded++;
                        return;
                    }
                }
                if (yours)
                {
                    state.BlockDemand += amount;
                }
                var blockBefore = simPlayer.Block;
                simPlayer.Incoming += amount;
                var blocked = Math.Min(simPlayer.Block, amount);
                simPlayer.Block -= blocked;
                var through = amount - blocked;
                var toOsty = simPlayer.OstyShields ? OstyLoses(state, player, through) : 0;
                simPlayer = state.MutablePlayer(player);
                simPlayer.OstyTaken += toOsty;
                simPlayer.Through += through - toOsty;
                var lost = HpLoss(state, player, through - toOsty, enemy.Id, SimProps.Move);
                Lose(state, player, lost);
                PlayerDamaged(state, player, enemy.Id, SimProps.Move, blocked, lost);
                if (yours)
                {
                    // DamageResult.WasFullyBlocked: you had block and no damage got through after the AfterOsty modifiers (Tungsten
                    // Rod, Buffer...). Block only drops during an attack, so the first hit decides it.
                    if (hitsLanded == 0) enemy.FirstHitFullyBlocked = amount > 0 && (blocked > 0 || blockBefore > 0) && toOsty == 0 && lost == 0;
                    landed += amount;
                    hitsLanded++;
                }
                simPlayer = state.MutablePlayer(player);
                if (!dead && simPlayer.FlameBarrier > 0) Retaliate(simPlayer.FlameBarrier);
                if (!dead && simPlayer.Reflect && blocked > 0) Retaliate(blocked);
            }
            // A teammate takes each hit in their turn order (CreatureCmd.Damage over every living player: a hit still reaches
            // everyone even if one player's Thorns kills the attacker during it), through the game's damage math for them.
            void TeammateTakesHit(int teammateIndex, int hitIndex, int mine)
            {
                if (state.Teammates[teammateIndex].Hp <= 0) return;
                var amount = enemy.Intent != null ? IntentDamage(state, enemy, hitIndex, Who.Teammate(teammateIndex))
                    : teammateIndex < enemy.TeammateHits.Count && hitIndex < enemy.TeammateHits[teammateIndex].Count ? enemy.TeammateHits[teammateIndex][hitIndex] : mine;
                TakesHit(Who.Teammate(teammateIndex), amount);
            }
            var players = state.PlayerIds;
            for (var hit = 0; hit < HitCount(enemy); hit++)
            {
                // The hit on you: the intent through the damage pipeline as it lands, or the final number.
                var perHit = enemy.Intent != null ? IntentDamage(state, enemy, hit, Who.You) : enemy.Hits[hit];
                if (dead)
                {
                    state.CancelledHits++;
                    continue;
                }
                foreach (var player in players)
                {
                    if (player == Who.You) TakesHit(player, perHit);
                    else TeammateTakesHit(Who.TeammateIndex(player), hit, perHit);
                }
            }
            if (landed > 0) if (state.Log) state.Blockable.Add((hitsLanded > 1 ? $"{enemy.Name} ({hitsLanded} hits)" : enemy.Name, landed));
            // An enemy killed during the enemy turn (your Thorns, Flame Barrier or Reflect hitting back during its attack): its death
            // hooks run before the next enemy moves (ExplosivePower and RavenousPower.AfterDeath: the other Corpse Slug is stunned,
            // Crab Rage hits harder). Poison deaths run theirs at the turn's start, above.
            if (state.Enemies.Any(simEnemy => simEnemy.StartedAlive && !simEnemy.Alive && !simEnemy.DeathHandled)) AllyDeaths(state);
        }
    }
}
