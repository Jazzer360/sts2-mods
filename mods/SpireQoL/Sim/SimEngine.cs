namespace SpireQoL.Sim;

/// <summary>An enemy's intent against you right after a play (SimOutcome.IntentsAfterPlay): a hit's damage before and after, and its hits.</summary>
public sealed record IntentAfterPlay(int Before, int Least, int Most, int Hits, bool DeadInAll, bool DeadInSome)
{
    /// <summary>The play changes what the intent shows: a hit's damage, or the enemy dies in every branch.</summary>
    public bool Changed => DeadInAll || Least != Before || Most != Before;
}

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
    /// <summary>A payout decided where it's paid (SimState.Payouts: decision #239) held in every branch (the search finished) / in some branch.</summary>
    public bool PaidInEvery(string model, int? owner) => InEvery(branch => branch.Payouts.Has(SimKeys.Logged(model, owner)));
    public bool PaidInSome(string model, int? owner) => InSome(branch => branch.Payouts.Has(SimKeys.Logged(model, owner)));
    /// <summary>Teammate k in the branch where they lose the most HP: their Incoming Damage.</summary>
    public SimPlayer TeammateWorst(int teammateIndex) => Branches.Select(branch => branch.Teammates[teammateIndex]).OrderByDescending(player => player.Taken).First();

    /// <summary>
    /// Each attacking enemy's intent against you right after the play, before your end of turn (Play Preview): a hit's damage as the
    /// game would show it then (its Weak, Strength, your Vulnerable...), least and most over the play's branches, before the play,
    /// and whether the play kills it in every branch or some. Empty when the play's search didn't finish.
    /// </summary>
    public IReadOnlyDictionary<int, IntentAfterPlay> IntentsAfterPlay = new Dictionary<int, IntentAfterPlay>();

    /// <summary>Enemies the play itself kills (before your end of turn) in some branch / in every branch.</summary>
    public HashSet<int> KilledByPlayInAny = new();
    public HashSet<int> KilledByPlayInAll = new();
    /// <summary>The play breaks some enemy's block in every branch (Hand Drill).</summary>
    public bool PlayBreaksBlockInAll;
    /// <summary>Block gains during the play (each sets off Juggernaut): the most over branches.</summary>
    public int PlayBlockGainsMax;

    /// <summary>Stunned during your turn (Burrowed, Flutter, Plow, Shriek) and alive when its turn comes, in every branch.</summary>
    public bool StunnedInAll(int enemyId) => InEvery(branch => branch.Enemy(enemyId) is { StunnedThisTurn: true, DiedBeforeActing: false });
    /// <summary>A power on it killed it during your turn (KillStep: The Gambit) in every branch.</summary>
    public bool KilledByItsPowerInAll(int enemyId) => InEvery(branch => branch.Enemy(enemyId) is { KilledByItsPower: true });
    /// <summary>A model acted (its acting log or its code's flash: SimState.FiredAny, FlashedAny) in some branch, over the whole turn (its owner: SimKeys.Logged).</summary>
    public bool ActedInAny(string model, int? owner) => InSome(branch => Acted(branch, model, owner));

    /// <summary>A model acted in this branch, over the whole turn so far (its acting log or its code's flash).</summary>
    public static bool Acted(SimState branch, string model, int? owner) => branch.FiredAny.Has(SimKeys.Logged(model, owner)) || branch.FlashedAny.Has(SimKeys.Logged(model, owner));
    /// <summary>The play kills some enemy in every branch (not always the same one).</summary>
    public bool PlayKillsInAll;
    /// <summary>The play's biggest powered hit (blocked plus HP lost), in the branch where it's smallest (Mighty Wings).</summary>
    public int PlayBiggestHitMin;
    /// <summary>The play's HP loss of yours, in the branch where it's smallest (SimState.PlayHpReached, PlayHpLosses, PlayHpLost).</summary>
    public int PlayHpReachedMin, PlayHpLossesMin, PlayHpLostMin;
    /// <summary>What fired during the play (its log): the counts in every branch (smallest), and in some branch (largest).</summary>
    public NamedCounts PlayFiredEvery = NamedCounts.Empty, PlayFiredAny = NamedCounts.Empty;
    /// <summary>Where read models' code flashed during the play (SimState.Flashed, decision #199): in every branch, and in some.</summary>
    public NamedCounts PlayFlashedEvery = NamedCounts.Empty, PlayFlashedAny = NamedCounts.Empty;
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

    /// <summary>
    /// A read model's own Flash() ran (SimState.Flashed, decision #199), over the whole run, in every branch: what the forecast audit holds a power
    /// that acts in the enemy turn to (Imbalanced's, on its code's test).
    /// </summary>
    public bool FlashedInAll(string model, int? owner) => InEvery(branch => branch.Flashed.Has(SimKeys.Logged(model, owner)));
    /// <summary>A read model's own Flash() ran in some branch (SimState.FlashedAny).</summary>
    public bool FlashedInAny(string model, int? owner) => InSome(branch => branch.FlashedAny.Has(SimKeys.Logged(model, owner)));

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
        MarkUnread(first);
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
                EnemyTurnStart(branches);
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
        var killedPerBranch = states.Select(branch => aliveAtStart.Where(id => branch.Enemy(id) is not { Died: false }).ToHashSet()).ToList();
        // Enemies that gained block from Curl Up, Sneaky or Skittish because of the play, in some branch.
        // Some enemy's block broken by the play, in every branch (Hand Drill).
        var breaksBlock = complete && states.All(branch => branch.Enemies.Any(enemy => enemy.BlockBroken && start.Enemy(enemy.Id) is { BlockBroken: false }));
        // Block gains during the play (each sets off Juggernaut).
        var gains = states.Select(branch => branch.You.BlockGains - start.You.BlockGains).ToList();
        int PlayMin(Func<SimState, int> valueOf) => !complete || states.Count == 0 ? 0 : states.Min(valueOf);
        // The play's log over its branches: in every branch (each merged one keeps its smallest), and in some branch.
        var firedEvery = !complete || states.Count == 0 ? NamedCounts.Empty
            : states.Skip(1).Aggregate(states[0].Fired, (acc, branch) => SimState.MergeLog(acc, branch.Fired, every: true));
        var firedAny = states.Aggregate(NamedCounts.Empty, (acc, branch) => SimState.MergeLog(acc, branch.FiredAny, every: false));
        var flashedEvery = !complete || states.Count == 0 ? NamedCounts.Empty
            : states.Skip(1).Aggregate(states[0].Flashed, (acc, branch) => SimState.MergeLog(acc, branch.Flashed, every: true));
        var flashedAny = states.Aggregate(NamedCounts.Empty, (acc, branch) => SimState.MergeLog(acc, branch.FlashedAny, every: false));
        // The first hit of the card's attack on each enemy, when nothing random came before it (the damage-formula audit).
        var firstHits = complete && states.Count > 0 && states.All(branch => ReferenceEquals(branch.FirstHits, states[0].FirstHits) || branch.FirstHits.SequenceEqual(states[0].FirstHits)) ? states[0].FirstHits : System.Collections.Immutable.ImmutableSortedDictionary<int, int>.Empty;
        return new SimOutcome
        {
            KilledByPlayInAny = killedPerBranch.SelectMany(killed => killed).ToHashSet(),
            KilledByPlayInAll = !complete || killedPerBranch.Count == 0
                ? new HashSet<int>()
                : killedPerBranch.Skip(1).Aggregate(new HashSet<int>(killedPerBranch[0]), (acc, killed) => { acc.IntersectWith(killed); return acc; }),
            PlayKillsInAll = complete && killedPerBranch.Count > 0 && killedPerBranch.All(killed => killed.Count > 0),
            PlayBreaksBlockInAll = breaksBlock,
            PlayBlockGainsMax = gains.Count > 0 ? gains.Max() : 0,
            PlayBiggestHitMin = PlayMin(branch => branch.PlayBiggestHit),
            PlayHpReachedMin = PlayMin(branch => branch.PlayHpReached),
            PlayHpLossesMin = PlayMin(branch => branch.PlayHpLosses),
            PlayHpLostMin = PlayMin(branch => branch.PlayHpLost),
            PlayFiredEvery = firedEvery,
            PlayFiredAny = firedAny,
            PlayFlashedEvery = flashedEvery,
            PlayFlashedAny = flashedAny,
            PlayFirstHits = firstHits,
            IntentsAfterPlay = complete ? IntentsAfter(states, start) : new Dictionary<int, IntentAfterPlay>(),
        };
    }

    /// <summary>
    /// Each attacking enemy's hit on you as its intent reads right after the play, in every branch (SimOutcome.IntentsAfterPlay):
    /// the intent's own numbers through the damage math (IntentDamage), as the game recalculates the intent it shows.
    /// </summary>
    private static Dictionary<int, IntentAfterPlay> IntentsAfter(List<SimState> states, SimState start)
    {
        var result = new Dictionary<int, IntentAfterPlay>();
        if (states.Count == 0) return result;
        foreach (var enemy in start.Enemies)
        {
            if (!enemy.Alive || enemy.Intent is not { Count: > 0 }) continue;
            var before = IntentDamage(start, enemy, hit: 0, Who.You);
            int? least = null, most = null;
            var dead = 0;
            foreach (var branch in states)
            {
                if (branch.Enemy(enemy.Id) is not { Alive: true } after)
                {
                    dead++;
                    continue;
                }
                var amount = IntentDamage(branch, after, hit: 0, Who.You);
                least = least == null ? amount : Math.Min(least.Value, amount);
                most = most == null ? amount : Math.Max(most.Value, amount);
            }
            result[enemy.Id] = new IntentAfterPlay(before, least ?? 0, most ?? 0, enemy.Intent.Count, DeadInAll: dead == states.Count, DeadInSome: dead > 0);
        }
        return result;
    }

    // ------------------------------------------------------------------ branching

    /// <summary>
    /// Chains a branching effect inside one step (Juggernaut on each block gain Beacon of Hope shares,
    /// Forgotten Soul after Feel No Pain): identical results merge, as between steps.
    /// </summary>
    internal static List<SimState> Then(IEnumerable<SimState> states, Func<SimState, IEnumerable<SimState>> f)
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
        /// <summary>Branches set aside while a WhenAction's actions run on the others (they're held too).</summary>
        public int Aside;
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
                if (work != null && !work.Hold(next.Count + States.Count + Aside)) Stop(next, i + 1);
            }
            States = next;
            work?.Hold(next.Count + Aside);
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
    /// Damage to an enemy (CreatureCmd.Damage, its hooks in SimPipelines), in the game's order: the damage caps (Hard to Kill,
    /// Intangible: the damage math applies them, amounts given as final numbers get them here, the cap flashing when it changes
    /// one), what the hit does worked out (its block's share, the HP-loss pipeline: Hardened Shell, Slippery, The Boot...), BeforeDamageReceived
    /// (Thorns hits the dealer back; v0.114.0 tells it once the results are worked out, as CreatureCmd.GetDamageResults does), the results applied: its
    /// block, its HP, AfterBlockBroken when its block broke (Burrowed, Hand Drill), then AfterDamageGiven and AfterDamageReceived
    /// (Envenom, Mighty Wings, Asleep, Slumber, Plow, Shriek, Flutter, The Gambit...), even when its block took it all. Returns the HP
    /// it lost (overkill included).
    /// </summary>
    /// <param name="dealer">Who deals it (you by default; Who.YourOsty for Osty's hits).</param>
    /// <param name="props">The damage's props: a card's hit is a powered Move; procs, orbs and the like are Unpowered.</param>
    internal static int Damage(SimState state, SimEnemy enemy, int amount, int? dealer = Who.You, SimProps props = SimProps.Unpowered, SimCard? card = null)
    {
        if (amount <= 0 || !enemy.Alive) return 0;
        var listeners = Listeners(state);
        if (!AnyListensToDamage(listeners)) return PlainDamage(state, enemy, amount, dealer, props);
        var ctx = new DamageCtx(dealer, enemy.Id, props, card, state.CurrentPlay);
        // The smallest cap (Hook.ModifyDamage's last term); the one that set it flashes when it lowers the amount (AfterModifyingDamageAmount).
        var cap = decimal.MaxValue;
        (SimModel Model, int Owner)? capper = null;
        foreach (var (model, o) in listeners)
        {
            var term = model.ModifyDamageCap(state, o, ctx);
            if (term < cap) (cap, capper) = (term, (model, o));
        }
        if (amount > cap)
        {
            amount = (int)cap;
            capper!.Value.Model.AfterModifyingDamageAmount(state, capper.Value.Owner, card);
        }
        // What the hit does, worked out first (beta, v0.114.0: CreatureCmd.GetDamageResults, a dry run): its block's share and the HP the pipeline
        // leaves; then Hook.BeforeDamageReceived (Thorns hits the dealer back), then the results applied as worked out (ApplyDamageResults). Main
        // (MainRule.ReceivedBeforeTheResults): BeforeDamageReceived first, then the same worked out on what it left.
        var receivedFirst = ReceivedFirst(state);
        if (receivedFirst)
        {
            foreach (var (simModel, o) in listeners) simModel.BeforeDamageReceived(state, o, ctx, amount);
            if (!enemy.Alive) return 0;
        }
        var hadBlock = enemy.Block > 0;
        var absorbed = props.HasFlag(SimProps.Unblockable) ? 0 : Math.Min(enemy.Block, amount);
        var notes = default(HpLossNotes);
        var toHp = amount - absorbed > 0 ? Math.Max(0, (int)HpLossFor(state, ctx, amount - absorbed, out notes)) : 0;
        if (!receivedFirst)
        {
            foreach (var (simModel, o) in listeners) simModel.BeforeDamageReceived(state, o, ctx, amount);
            if (!enemy.Alive) return 0;
        }
        enemy.Block -= Math.Min(enemy.Block, absorbed);
        var blockBroken = hadBlock && absorbed > 0 && enemy.Block == 0;
        if (blockBroken) enemy.BlockBroken = true;
        TellHpLoss(state, notes);
        var hpBefore = enemy.Hp;
        if (toHp > 0)
        {
            enemy.Hp -= toHp;
            enemy.UnblockedInstances++; // UnblockedDamage is 0 when Hardened Shell stops it
        }
        state.AttackDealt += absorbed + toHp;
        // After the HP loss, before the damage hooks (the game's results loop in CreatureCmd.Damage). A hit that killed it is told to its
        // own powers too: the game kills it after these hooks.
        if (blockBroken) foreach (var (model, o) in ListenersWith(state, enemy.Id)) model.AfterBlockBroken(state, o, enemy.Id, dealer);
        // DamageResult.WasFullyBlocked (CreatureCmd.GetDamageResults): not Unblockable, block before it or block that took some, no HP past the pipeline.
        var fullyBlocked = !props.HasFlag(SimProps.Unblockable) && (hadBlock || absorbed > 0) && toHp == 0;
        var hit = new DamageEvent(dealer, enemy.Id, props, card, absorbed + Math.Min(toHp, Math.Max(0, hpBefore)), absorbed, toHp, hpBefore > 0 && enemy.Hp <= 0, fullyBlocked);
        if (props.IsPoweredAttack() && dealer is Who.You or Who.YourOsty) BiggestHit(state, hit.Total);
        foreach (var (m, o) in ListenersWith(state, enemy.Id)) m.AfterDamageGiven(state, o, hit);
        foreach (var (m, o) in ListenersWith(state, enemy.Id)) m.AfterDamageReceived(state, o, hit);
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
    /// The models whose HP-loss terms changed a damage's (whole-number) amount, to be told as its results are applied (CreatureCmd.ApplyDamageResults,
    /// v0.114.0: after Hook.BeforeDamageReceived): BeforeOsty's (Hook.AfterModifyingHpLostBeforeOsty: Hardened Shell) and AfterOsty's
    /// (AfterModifyingHpLostAfterOsty: most flash, Buffer loses a stack). Null: none.
    /// </summary>
    internal readonly record struct HpLossNotes(List<(SimModel Model, int Owner)>? BeforeOsty, List<(SimModel Model, int Owner)>? AfterOsty);

    /// <summary>
    /// Hook.ModifyHpLost* as main's CreatureCmd.Damage runs it (MainRule.ReceivedBeforeTheResults), each change told as it's made: every listener's
    /// BeforeOsty then BeforeOstyLate term, the models that changed it there told (AfterModifyingHpLostBeforeOsty: Hardened Shell), then every AfterOsty
    /// and AfterOstyLate term and theirs (AfterModifyingHpLostAfterOsty: most flash, Buffer loses a stack).
    /// </summary>
    internal static decimal HpLossPipeline(SimState state, DamageCtx ctx, decimal amount)
    {
        var listeners = Listeners(state);
        List<(SimModel Model, int Owner)>? changed = null;
        amount = HpLossPhase(state, ctx, amount, listeners, afterOsty: false, ref changed);
        TellHpLoss(state, new HpLossNotes(changed, null));
        changed = null;
        amount = HpLossPhase(state, ctx, amount, listeners, afterOsty: true, ref changed);
        TellHpLoss(state, new HpLossNotes(null, changed));
        return amount;
    }

    /// <summary>
    /// The HP a damage's <paramref name="amount"/> past block comes to (Hook.ModifyHpLost*, every listener's BeforeOsty, BeforeOstyLate, AfterOsty and
    /// AfterOstyLate terms in order), with nothing told: beta's CreatureCmd.GetDamageResults dry run (v0.114.0), the models that changed it in
    /// <paramref name="notes"/> for when the results are applied (<see cref="TellHpLoss"/>).
    /// </summary>
    internal static decimal HpLossAmount(SimState state, DamageCtx ctx, decimal amount, out HpLossNotes notes)
    {
        var listeners = Listeners(state);
        List<(SimModel Model, int Owner)>? beforeOsty = null, afterOsty = null;
        amount = HpLossPhase(state, ctx, amount, listeners, afterOsty: false, ref beforeOsty);
        amount = HpLossPhase(state, ctx, amount, listeners, afterOsty: true, ref afterOsty);
        notes = new HpLossNotes(beforeOsty, afterOsty);
        return amount;
    }

    /// <summary>One half of the HP-loss terms (BeforeOsty and BeforeOstyLate, or AfterOsty and AfterOstyLate), the models that changed the amount kept.</summary>
    private static decimal HpLossPhase(SimState state, DamageCtx ctx, decimal amount, List<(SimModel Model, int Owner)> listeners, bool afterOsty, ref List<(SimModel Model, int Owner)>? changed)
    {
        for (var late = 0; late < 2; late++)
            foreach (var (model, o) in listeners)
            {
                var before = amount;
                amount = (afterOsty, late) switch
                {
                    (false, 0) => model.ModifyHpLostBeforeOsty(state, o, ctx, amount),
                    (false, _) => model.ModifyHpLostBeforeOstyLate(state, o, ctx, amount),
                    (true, 0) => model.ModifyHpLostAfterOsty(state, o, ctx, amount),
                    _ => model.ModifyHpLostAfterOstyLate(state, o, ctx, amount),
                };
                if (decimal.Truncate(before) != decimal.Truncate(amount)) (changed ??= new()).Add((model, o));
            }
        return amount;
    }

    /// <summary>Main's CreatureCmd.Damage tells BeforeDamageReceived and each HP-loss change before working out the results (MainRule.ReceivedBeforeTheResults).</summary>
    private static bool ReceivedFirst(SimState state) => (state.MainRules & MainRule.ReceivedBeforeTheResults) != 0;

    /// <summary>
    /// The HP a damage past block comes to as the running game's CreatureCmd.Damage works it out: beta's dry run, its changes in <paramref name="notes"/>
    /// to tell as the results are applied; main's pipeline, its changes told as they're made (<paramref name="notes"/> empty).
    /// </summary>
    internal static decimal HpLossFor(SimState state, DamageCtx ctx, decimal amount, out HpLossNotes notes)
    {
        notes = default;
        return ReceivedFirst(state) ? HpLossPipeline(state, ctx, amount) : HpLossAmount(state, ctx, amount, out notes);
    }

    /// <summary>The models that changed a damage's HP loss told, as its results are applied (Hook.AfterModifyingHpLostBeforeOsty, then AfterOsty).</summary>
    internal static void TellHpLoss(SimState state, HpLossNotes notes)
    {
        if (notes.BeforeOsty != null) foreach (var (simModel, o) in notes.BeforeOsty) simModel.AfterModifyingHpLost(state, o, afterOsty: false);
        if (notes.AfterOsty != null) foreach (var (simModel, o) in notes.AfterOsty) simModel.AfterModifyingHpLost(state, o, afterOsty: true);
    }

    internal static void Stun(SimEnemy enemy)
    {
        enemy.Stunned = true;
        enemy.StunnedThisTurn = true;
    }

    /// <summary>
    /// Its next move set (MonsterModel.SetMoveImmediate, forced: NextMove is that move, in place of a stunned one too, so a stun earlier in the turn
    /// no longer stands): its hits and its steps are that move's (Waterfall Giant's AboutToBlow: none), what its intent showed gone.
    /// </summary>
    internal static void SetMove(SimEnemy enemy, SimMove move)
    {
        enemy.Stunned = false;
        enemy.StunnedThisTurn = false;
        enemy.Hits = move.Hits.ToList();
        enemy.TeammateHits = new List<List<int>>();
        enemy.Intent = null;
        enemy.Move = move.Actions;
        enemy.IntendsToAttack = move.Hits.Count > 0;
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
    private static IEnumerable<SimState> HitRandom(SimState state, Func<SimEnemy, int> amount, Action<SimState, SimEnemy>? then = null, int? dealer = Who.You, SimProps props = SimProps.Unpowered)
    {
        void Hit(SimState branch, SimEnemy enemy)
        {
            Damage(branch, enemy, amount(enemy), dealer, props);
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
    internal static IEnumerable<SimState> GainBlock(SimState state, int amount, string? log, int player = Who.You)
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

    /// <summary>
    /// What a damage past a player's block does to their HP and their Osty's, as CreatureCmd.GetDamageResults works it out (decompile v0.114.0; main's
    /// CreatureCmd.Damage the same, each phase told as it's made): Hook.ModifyHpLost's BeforeOsty phase on the player; then, for a powered attack while
    /// their Osty lives with Die For You (Hook.ModifyUnblockedDamageTarget: DieForYouPower), the AfterOsty phase with Osty as the target and his HP
    /// taking it (Creature.LoseHpInternal), what he can't take (his overkill) through the AfterOsty phase again on the player; otherwise the AfterOsty
    /// phase on the player. The models whose phase changed the amount are kept to be told as the results are applied (Hook.AfterModifyingHpLostBeforeOsty,
    /// then AfterOsty for the first AfterOsty phase, before the player's HP, and for the overkill's, after it), or told at once on main.
    /// </summary>
    private readonly record struct PlayerHpLoss(int ToOsty, bool OstyKilled, int Lost, List<(SimModel Model, int Owner)>? BeforeOsty,
        List<(SimModel Model, int Owner)>? FirstAfterOsty, List<(SimModel Model, int Owner)>? OverkillAfterOsty);

    private static PlayerHpLoss HpLossOnPlayer(SimState state, List<(SimModel Model, int Owner)> listeners, int player, int through, bool redirect, int ostyHp,
        int? dealer, SimProps props, SimCard? card, bool tellNow)
    {
        if (through <= 0) return default;
        if (listeners.Count == 0)
        {
            var taken = redirect ? Math.Min(ostyHp, through) : 0;
            return new PlayerHpLoss(taken, redirect && through >= ostyHp, through - taken, null, null, null);
        }
        var play = card == null ? null : state.CurrentPlay;
        var onPlayer = new DamageCtx(dealer, player, props, card, play);
        List<(SimModel Model, int Owner)>? beforeOsty = null, firstAfter = null, overkillAfter = null;
        void Tell(ref List<(SimModel Model, int Owner)>? changed, bool afterOsty)
        {
            if (!tellNow || changed == null) return;
            TellHpLoss(state, afterOsty ? new HpLossNotes(null, changed) : new HpLossNotes(changed, null));
            changed = null;
        }
        var amount = HpLossPhase(state, onPlayer, through, listeners, afterOsty: false, ref beforeOsty);
        Tell(ref beforeOsty, afterOsty: false);
        if (!redirect)
        {
            amount = HpLossPhase(state, onPlayer, amount, listeners, afterOsty: true, ref firstAfter);
            Tell(ref firstAfter, afterOsty: true);
            return new PlayerHpLoss(0, false, Math.Max(0, (int)amount), beforeOsty, firstAfter, null);
        }
        var onOsty = onPlayer with { Target = OstyId(player) };
        var toOsty = (int)Math.Clamp(HpLossPhase(state, onOsty, amount, listeners, afterOsty: true, ref firstAfter), 0m, 999999999m);
        Tell(ref firstAfter, afterOsty: true);
        var killed = toOsty >= ostyHp;
        var overkill = killed ? toOsty - ostyHp : 0;
        var lost = 0;
        if (overkill > 0)
        {
            lost = Math.Max(0, (int)HpLossPhase(state, onPlayer, overkill, listeners, afterOsty: true, ref overkillAfter));
            Tell(ref overkillAfter, afterOsty: true);
        }
        return new PlayerHpLoss(Math.Min(toOsty, ostyHp), killed, lost, beforeOsty, firstAfter, overkillAfter);
    }

    /// <summary>Hook.BeforeDamageReceived of a damage to a player (Thorns hits the dealer back, read from its code; the damage still lands).</summary>
    private static void TellReceived(SimState state, List<(SimModel Model, int Owner)> listeners, int player, int amount, SimProps props, int? dealer, SimCard? card)
    {
        if (listeners.Count == 0) return;
        var damage = new DamageCtx(dealer, player, props, card, card == null ? null : state.CurrentPlay);
        foreach (var (model, owner) in listeners) model.BeforeDamageReceived(state, owner, damage, amount);
    }

    /// <summary>
    /// After a damage landed on a player (their HP already lower): AfterDamageGiven and AfterDamageReceived (Beating Remnant counts, Flame Barrier and
    /// Reflect hit back...), told of the player's result whatever it came to: CreatureCmd.Damage tells every result, one Wuppy, Osty or the HP-loss
    /// pipeline left at 0 included (its results loop). The fight's log has it already (RecordDamageReceived).
    /// </summary>
    private static void PlayerDamaged(SimState state, int player, int? dealer, SimProps props, int blocked, int lost, SimCard? card, bool fullyBlocked)
    {
        var listeners = Listeners(state);
        if (listeners.Count == 0) return;
        var hit = new DamageEvent(dealer, player, props, card, blocked + lost, blocked, lost, Killed: false, fullyBlocked);
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

    /// <summary>What a damage did as it landed on a player (CreatureCmd.Damage's results for them): its amount past the pre-block redirect (Wuppy), their block's, Osty's and their HP's.</summary>
    internal readonly record struct PlayerHit(int Amount, int Blocked, int ToOsty, int Lost);

    /// <summary>
    /// CreatureCmd.Damage landing on a player once its amount is worked out, for every damage to a player (decision #219), in the game's order:
    /// what it does worked out first (v0.114.0: CreatureCmd.GetDamageResults), then Hook.BeforeDamageReceived (Thorns hits the dealer back, read from its
    /// code; the damage still lands as worked out), then the results applied: the creature Hook.ModifyPreBlockDamageTarget names first (Wuppy's
    /// Loyalty, read from its code: a powered attack while Wuppy lives; only what it doesn't take goes on), their block unless it's Unblockable
    /// (Creature.DamageBlockInternal), the one Hook.ModifyUnblockedDamageTarget names for what got past it (Die For You's Osty, read from its code), the
    /// HP-loss pipeline, their HP, then the hit's reactions (Flame Barrier and Reflect hit back, read from their code). The same for every player
    /// (decision #93). <paramref name="label"/> names it in Incoming Damage's breakdown, and yours feeds your play's HP-loss tabs; null for an enemy's
    /// attack in the enemy turn, which names its hits by its enemy.
    /// </summary>
    internal static PlayerHit Lands(SimState state, string? label, int player, int amount, SimProps props, int? dealer, SimCard? card)
    {
        var listeners = Listeners(state);
        var yours = player == Who.You;
        var unblockable = props.HasFlag(SimProps.Unblockable);
        // What the hit does, worked out first (beta, v0.114.0: CreatureCmd.GetDamageResults, a dry run): Wuppy's share, the block's, Osty's, the HP
        // the pipeline leaves; then Hook.BeforeDamageReceived (Thorns hits the dealer back), then the results applied as worked out
        // (ApplyDamageResults). Main (MainRule.ReceivedBeforeTheResults): BeforeDamageReceived first, then the same worked out on what it left.
        var receivedFirst = ReceivedFirst(state);
        if (receivedFirst) TellReceived(state, listeners, player, amount, props, dealer, card);
        var before = state.Player(player)!;
        var blockBefore = before.Block;
        // Hook.ModifyPreBlockDamageTarget: a creature that takes the damage whole before the player's block (Wuppy's Loyalty, read from its code), its
        // HP as Creature.LoseHpInternal takes it; only what it can't take (its overkill) goes on.
        var preBlock = RedirectTarget(state, listeners, preBlock: true, player, amount, props, dealer, card);
        var toPreBlock = 0;
        var preBlockKilled = false;
        var past = amount;
        if (preBlock != Who.None)
        {
            var petHp = PetHp(state, preBlock);
            toPreBlock = Math.Min(petHp, amount);
            preBlockKilled = petHp > 0 && amount >= petHp;
            past = preBlockKilled ? amount - petHp : 0;
        }
        var blocked = unblockable ? 0 : Math.Min(before.Block, past);
        var through = past - blocked;
        // Hook.ModifyUnblockedDamageTarget: who takes what got past the block (Die For You's Osty, read from its code).
        var receiver = RedirectTarget(state, listeners, preBlock: false, player, through, props, dealer, card);
        var redirect = receiver != player;
        var hpLoss = HpLossOnPlayer(state, Listeners(state), player, through, redirect, redirect ? PetHp(state, receiver) : 0, dealer, props, card, tellNow: receivedFirst);
        var lost = hpLoss.Lost;
        if (!receivedFirst) TellReceived(state, listeners, player, amount, props, dealer, card);
        var simPlayer = state.MutablePlayer(player);
        if (toPreBlock > 0)
        {
            simPlayer.WuppyHp = Math.Max(0, simPlayer.WuppyHp - toPreBlock);
            simPlayer.WuppyTaken += toPreBlock; // not their Incoming Damage: it never reaches them
        }
        if (unblockable)
        {
            if (yours && label != null && state.Log) state.Unblockable.Add((label, past));
        }
        else
        {
            simPlayer.Incoming += past;
            if (yours)
            {
                state.BlockDemand += past;
                if (label != null && state.Log) state.Blockable.Add((label, past));
            }
        }
        simPlayer.Block -= Math.Min(simPlayer.Block, blocked);
        // The results applied (beta's ApplyDamageResults): the BeforeOsty phase's changes told, Osty's HP, the first AfterOsty phase's, the player's HP,
        // the overkill's AfterOsty phase's (main told each as it was made).
        TellHpLoss(state, new HpLossNotes(hpLoss.BeforeOsty, null));
        var toOsty = OstyDrops(state, player, hpLoss.ToOsty);
        TellHpLoss(state, new HpLossNotes(null, hpLoss.FirstAfterOsty));
        simPlayer = state.MutablePlayer(player);
        simPlayer.OstyTaken += toOsty;
        if (!unblockable) simPlayer.Through += through - toOsty;
        Lose(state, player, lost);
        TellHpLoss(state, new HpLossNotes(null, hpLoss.OverkillAfterOsty));
        if (unblockable) state.MutablePlayer(player).UnblockableTaken += lost;
        if (yours && label != null) PlayHp(state, through - toOsty, lost);
        // Each result in the fight's log, then each told to the hooks (the results loop): beta's results are the player's, the pre-block redirect's
        // (Wuppy's), then Osty's (DamageResults.AllResults: the original, the pre-block redirect, the unblocked redirect); main's Osty's, then the
        // player's (main has no pre-block redirect).
        var ostyFirst = receivedFirst && redirect;
        if (ostyFirst) RecordOstyHit(state, player, props, toOsty);
        RecordDamageReceived(state, player, props, lost);
        if (preBlock != Who.None) RecordDamageReceived(state, preBlock, props, toPreBlock);
        if (redirect && !ostyFirst) RecordOstyHit(state, player, props, toOsty);
        if (ostyFirst) TellOstyHit(state, player, dealer, props, card, toOsty, hpLoss.OstyKilled);
        // DamageResult.WasFullyBlocked: not Unblockable, block before it or block that took some, no HP lost past the pipeline (Osty's when he takes it).
        PlayerDamaged(state, player, dealer, props, blocked, lost, card, fullyBlocked: !unblockable && (blocked > 0 || blockBefore > 0) && hpLoss.ToOsty == 0 && lost == 0);
        if (preBlock != Who.None) PetDamaged(state, preBlock, dealer, props, card, toPreBlock, preBlockKilled);
        if (redirect && !ostyFirst) TellOstyHit(state, player, dealer, props, card, toOsty, hpLoss.OstyKilled);
        return new PlayerHit(past, blocked, toOsty, lost);
    }

    /// <summary>
    /// Hook.ModifyPreBlockDamageTarget (<paramref name="preBlock"/>: every listener in turn on the target so far, from none) or
    /// Hook.ModifyUnblockedDamageTarget (from the player) for a damage to <paramref name="player"/>: the creature that takes it. Who.None for no
    /// pre-block one; the player for no unblocked one. A creature the simulator keeps no HP for (a game update's) makes the simulation unknown.
    /// </summary>
    private static int RedirectTarget(SimState state, List<(SimModel Model, int Owner)> listeners, bool preBlock, int player, int amount, SimProps props, int? dealer, SimCard? card)
    {
        var hook = preBlock ? nameof(SimModel.ModifyPreBlockDamageTarget) : nameof(SimModel.ModifyUnblockedDamageTarget);
        var target = preBlock ? Who.None : player;
        var damage = new DamageCtx(dealer, player, props, card, card == null ? null : state.CurrentPlay);
        foreach (var (model, owner) in listeners)
        {
            if (!model.Overrides(hook)) continue;
            target = preBlock ? model.ModifyPreBlockDamageTarget(state, owner, damage, target, amount) : model.ModifyUnblockedDamageTarget(state, owner, damage with { Target = target }, amount);
        }
        if (target == Who.None || target == player) return target;
        // The pets the simulator keeps take what's redirected (Wuppy before the block, Osty after it), each their player's.
        if (Who.IsPet(target) && Who.PetOwnerOf(target) == player && Who.PetKindOf(target) == (preBlock ? PetKind.Wuppy : PetKind.Osty)) return target;
        state.Unmodelled ??= $"a damage redirected to a creature the simulator doesn't follow there ({hook})";
        return preBlock ? Who.None : player;
    }

    /// <summary>
    /// A pet's own result of a damage redirected to it before its player's block (Wuppy's), told once the results are applied, as CreatureCmd.Damage's
    /// results loop tells each: AfterDamageGiven, then AfterDamageReceived unless it killed it. (Hook.AfterCurrentHpChanged with its loss: the Wuppy
    /// relic keeps its counter from it, which nothing a simulation reads.)
    /// </summary>
    private static void PetDamaged(SimState state, int pet, int? dealer, SimProps props, SimCard? card, int lost, bool killed)
    {
        if (Listeners(state).Count == 0) return;
        var hit = new DamageEvent(dealer, pet, props, card, lost, Blocked: 0, Unblocked: lost, Killed: killed);
        foreach (var (model, owner) in Listeners(state)) model.AfterDamageGiven(state, owner, hit);
        if (!killed) foreach (var (model, owner) in Listeners(state)) model.AfterDamageReceived(state, owner, hit);
        // Then its death (CreatureCmd.Kill after the results loop).
        else PetDied(state, pet);
    }

    /// <summary>Blockable damage to a player from no one (a card's damage to you: SimPlay.DamageToYou), Unpowered.</summary>
    private static void LandBlockable(SimState state, string name, int amount)
    {
        if (amount > 0) Lands(state, name, Who.You, amount, SimProps.Unpowered, dealer: null, card: null);
    }

    /// <summary>
    /// The deaths an end-of-turn step caused run their hooks before the next step (CreatureCmd.Damage ends in CreatureCmd.Kill, which runs
    /// Hook.AfterDeath as the creature dies: Explosive's damage meets the block its players have then), and what those hooks and the step's
    /// hits raised (Gremlin Horn's draw and energy) runs then too, as the game runs each command to its end before the next: a Burn drawn
    /// there is in the hand when DoTurnEnd lists the cards that act in it. Only when one is waiting (no extra step otherwise).
    /// </summary>
    private static void DeathCheck(BranchSet branches)
    {
        if (branches.States.Any(branch => branch.Enemies.Any(enemy => enemy.StartedAlive && !enemy.Alive && !enemy.DeathHandled))) branches.Do(AllyDeaths);
        if (branches.States.Any(branch => branch.HasPending)) branches.Each(DrainPending);
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
        AtZero(simPlayer, forced: false);
    }

    /// <summary>
    /// CreatureCmd.Kill of a player (The Gambit's): Creature.LoseHpInternal of all their HP (Taken counts it, as any HP they lose), then
    /// Hook.ShouldDie unless <paramref name="forced"/> (Fairy in a Bottle, then Lizard Tail, healing them from 0), else they die. Nothing on a
    /// player already dead. HP not tracked (int.MaxValue, a test's board): they die, nothing counted.
    /// </summary>
    internal static void KillPlayer(SimState state, int player, bool forced)
    {
        var simPlayer = state.MutablePlayer(player);
        if (simPlayer.Died) return;
        if (simPlayer.Hp != int.MaxValue) simPlayer.Taken += Math.Max(0, simPlayer.Hp);
        simPlayer.Hp = 0;
        AtZero(simPlayer, forced);
    }

    /// <summary>A player at 0 HP: what prevents their death if it can (Fairy in a Bottle, then Lizard Tail), unless forced; else they die.</summary>
    private static void AtZero(SimPlayer simPlayer, bool forced)
    {
        if (forced)
        {
            simPlayer.Hp = 0;
            simPlayer.Died = true;
        }
        else if (simPlayer.Fairies > 0)
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

    /// <summary>
    /// CreatureCmd.Damage of a player, the one path for every such command a card's or a model's code runs (decision #219): Hook.ModifyDamage with
    /// its caps (CalcDamage), Hook.AfterModifyingDamageAmount for each modelled source that changed the amount, then their block unless it's
    /// Unblockable (Creature.DamageBlockInternal), the HP-loss pipeline and the hit's reactions, each told the command's own dealer, props and card
    /// (its cardSource: the card for an HP cost, none for a hook's): Lands. <paramref name="fired"/>: the model whose command it is, logged with the
    /// amount before the sources that changed it are told (its hook acts, then the command runs). <paramref name="label"/> names it in Incoming
    /// Damage's breakdown. Returns the damage after the math.
    /// </summary>
    internal static int DamagePlayer(SimState state, string label, int player, decimal baseDamage, SimProps props, int? dealer, SimCard? card, (string Name, int Owner)? fired = null)
    {
        var damage = new DamageCtx(dealer, player, props, card, card == null ? null : state.CurrentPlay);
        var modifiers = Listeners(state).Count == 0 ? null : new List<(SimModel Model, int Owner)>();
        var amount = CalcDamage(state, baseDamage, damage, captured: null, modifiers);
        if (fired is { } source) state.LogFired(source.Name, amount, source.Owner);
        if (modifiers != null) foreach (var (model, owner) in modifiers) model.AfterModifyingDamageAmount(state, owner, card);
        if (amount <= 0) return amount;
        Lands(state, label, player, amount, props, dealer, card);
        return amount;
    }

    /// <summary>The props every card's HP cost passes (CreatureCmd.Damage of its owner: Bloodletting's, Offering's, Corrupted's): LoseHpAction's.</summary>
    internal const SimProps HpCostProps = SimProps.Unblockable | SimProps.Unpowered | SimProps.Move;

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
        MarkUnread(first);
        return new BranchSet(first);
    }

    /// <summary>
    /// A power or relic on the board whose hooks a game update made unreadable (SimModels.UnreadReason): the simulation can't follow what it
    /// does, so it's unknown, with why (decision #92), never exact without it.
    /// </summary>
    private static void MarkUnread(SimState state)
    {
        if (state.Unmodelled != null) return;
        foreach (var id in state.PlayerIds)
        {
            var player = state.Player(id)!;
            foreach (var entry in player.Powers.Items) if (SimModels.UnreadReason(entry.Name.Name) is { } why) { state.Unmodelled = why; return; }
            foreach (var entry in player.Relics.Items) if (SimModels.UnreadReason(entry.Name.Name) is { } why) { state.Unmodelled = why; return; }
        }
        foreach (var enemy in state.Enemies)
            foreach (var entry in enemy.Powers.Items) if (SimModels.UnreadReason(entry.Name.Name) is { } why) { state.Unmodelled = why; return; }
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
            case ModelCommandAction command: branches.Each(branch => RunModelCommand(branch, command.Power, command.Method)); break;
            case TriggerPoisonAction: branches.Each(branch => RunModelCommand(branch, PowerId.Poison.Name, "Trigger")); break;
            case KillOstyAction: branches.Do(branch => OstyLoses(branch, Who.You, branch.You.OstyHp)); break;
            case SummonAction summonAction: RunSummon(branches, summonAction); break;
            case HealAction healAction: RunHeal(branches, healAction); break;
            case CardPlayAction cardPlayAction: RunCardPlay(branches, cardPlayAction); break;
            case AttackAction at: RunAttack(branches, at); break;
            case GainBlockAction gainBlockAction: RunBlock(branches, gainBlockAction); break;
            case ApplyPowerAction applyPowerAction: RunApplyPower(branches, applyPowerAction); break;
            case CardEventAction cardEventAction: RunCardEvent(branches, cardEventAction); break;
            case DrawnCardAction drawnCardAction: branches.Each(branch => RunDrawnCard(branch, drawnCardAction)); break;
            case AutoPlayFromDrawAction autoPlayFromDrawAction:
                branches.Each(branch => AutoPlayFromDraw(branch, branch.CurrentPlay?.Owner ?? Who.You, CountOf(branch, autoPlayFromDrawAction.CountFrom, autoPlayFromDrawAction.Count), autoPlayFromDrawAction.ForceExhaust, autoPlayFromDrawAction.Upgrade));
                break;
            case ShuffleDrawPileAction shuffleDrawPileAction:
                // CardPileCmd.Shuffle: the discard pile into the draw pile, then AfterShuffle (Whittling Knife, Biiig Hug); one only when it's
                // needed (a draw pile shorter than it looks at, a discard pile to shuffle in: ShuffleIfNecessary, Ki Chant's) waits for that.
                branches.Each(branch =>
                {
                    var owner = branch.CurrentPlay?.Owner ?? Who.You;
                    if (!DrawsFromPile(branch, owner)) return new[] { branch };
                    var pile = branch.Player(owner)!.Draw!;
                    if (shuffleDrawPileAction.WhenShorterThan > 0 && (pile.Count >= shuffleDrawPileAction.WhenShorterThan || pile.DiscardCount == 0)) return new[] { branch };
                    branch.MutablePlayer(owner).Draw = pile.ShuffledWithDiscard(shuffleDrawPileAction.HandIn);
                    return ProcCardEvent(branch, CardEvent.Shuffled, count: 1, owner);
                });
                break;
            case TakeToHandAction takeToHandAction: branches.Each(branch => RunTakeToHand(branch, takeToHandAction)); break;
            case ExhaustFromHandAction exhaustFromHandAction: branches.Each(branch => RunExhaustFromHand(branch, exhaustFromHandAction)); break;
            case LeaveHandAction leaveHandAction: branches.Do(branch => RunLeaveHand(branch, leaveHandAction)); break;
            case TakeFromHandAction takeFromHandAction: RunTakeFromHand(branches, takeFromHandAction); break;
            case NotModelledAction notModelledAction: branches.Do(branch => branch.Unmodelled ??= notModelledAction.Reason); break;
            case UnreadAction unreadAction: branches.Do(branch => branch.Unmodelled ??= SimState.Unread(unreadAction.Member)); break;
            case GainEnergyAsHeldAction:
                branches.Each(branch => (branch.CurrentPlay?.Owner ?? Who.You) is var playOwner && branch.Player(playOwner) is { Energy: > 0 } player
                    ? ProcCardEvent(branch, CardEvent.EnergyGained, player.Energy, playOwner, indirect: false) : new[] { branch });
                break;
            case WhenAction whenAction: RunWhen(branches, whenAction); break;
            case AskAction askAction: RunAsk(branches, askAction); break;
            case LoseHpAction loseHpAction: branches.Do(branch => LoseHp(branch, branch.CurrentPlay?.Card.Name ?? "HP cost", loseHpAction.Amount, branch.CurrentPlay?.Owner ?? Who.You, branch.CurrentPlay?.Card)); break;
            default: throw new NotSupportedException($"No runner for {action.GetType().Name}");
        }
        // Events raised inside its hits or deaths (an arrow knocked loose) run once the action is over.
        if (branches.States.Any(branch => branch.HasPending)) branches.Each(DrainPending);
    }

    // ------------------------------------------------------------------ end of turn

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
        DeathCheck(branches);
        HookStep(branches, nameof(SimModel.BeforeSideTurnEnd), (model, owner, branch) => Who.IsPlayer(owner) ? model.BeforeSideTurnEnd(branch, owner) : new[] { branch });
        DeathCheck(branches);

        // 2. Each player's DoTurnEnd, in player order.
        foreach (var player in branches.States[0].PlayerIds)
        {
            OrbPassives(branches, player);
            DeathCheck(branches); // kills by orb passives
            branches.Each(branch => branch.Player(player) is { } simPlayer && Exhausting(branch, simPlayer) is > 0 and var exhausting && branch.TakesPart(player) ? ProcCardEvent(branch, CardEvent.Exhausted, exhausting, player, indirect: false, causedByEthereal: true) : new[] { branch });
            branches.Do(branch => CardsActInHand(branch, player));
            DeathCheck(branches);
        }

        // 3. AfterSideTurnEnd (Constrict, Magic Bomb, Demise, Consuming Shadow, Parrying Shield, Lunar Pastry), then
        // AfterSideTurnEndLate (Disintegration, Insatiable Dominion).
        HookStep(branches, nameof(SimModel.AfterSideTurnEnd), (model, owner, branch) => Who.IsPlayer(owner) ? model.AfterSideTurnEnd(branch, owner) : new[] { branch });
        DeathCheck(branches); // kills by Consuming Shadow's evokes, Parrying Shield, Black Hole
        HookStep(branches, nameof(SimModel.AfterSideTurnEndLate), (model, owner, branch) => Who.IsPlayer(owner) ? model.AfterSideTurnEndLate(branch, owner) : new[] { branch });

        // Deaths at the end of your turn run their hooks before the enemies act (arrows coming back: Twine's block).
        branches.Do(AllyDeaths);
        if (branches.States.Any(branch => branch.HasPending)) branches.Each(DrainPending);
        branches.Do(branch =>
        {
            branch.EndOfTurnNow = false;
            branch.BlockAtEnemyTurn = branch.You.Block;
            foreach (var enemy in branch.Enemies) { enemy.HpAtTurnEnd = Math.Max(0, enemy.Hp); enemy.UnblockedAtTurnEnd = enemy.UnblockedInstances; }
        });
    }

    // ------------------------------------------------------------------ enemy turn

    /// <summary>
    /// Hook.ShouldClearBlock of <paramref name="creature"/> (Creature.ClearBlock as its side's turn starts): cleared unless a listener says no
    /// (Barricade, Burrowed, read from their code). A preventer's AfterPreventingBlockClear (Blur's, Sturdy Clamp's: a player's) isn't told: no
    /// enemy's listener answers it.
    /// </summary>
    internal static bool ClearsBlock(SimState state, int creature)
    {
        foreach (var (model, owner) in Listeners(state))
            if (!model.ShouldClearBlock(state, owner, creature)) return false;
        return true;
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
    /// An enemy its own death's code brought back (SetHpStep on its dead owner), as CreatureCmd.Kill goes on after AfterDeath: its powers come off but
    /// the ones whose ShouldPowerBeRemovedAfterOwnerDeath is false (Creature.RemoveAllPowersAfterDeath, whatever its HP), and it's in the fight again.
    /// </summary>
    private static void Revive(SimState state, SimEnemy enemy)
    {
        enemy.Revived = true;
        var powers = enemy.Powers;
        foreach (var entry in powers.Items)
            if (SimModels.Get(entry.Name.Name)?.StaysOnDeadOwner(state, enemy.Id) != true) powers = powers.Without(entry.Name);
        enemy.Powers = powers;
    }

    /// <summary>
    /// CombatManager.IsEnding (IsCombatEnding): no primary enemy alive (one without a power whose OwnerIsSecondaryEnemy is true: Minion's) and no
    /// listener's Hook.ShouldStopCombatFromEnding (Infested's, Stock's, Steam Eruption's: their deaths bring creatures in). An enemy that died during
    /// its own move keeps its powers until the move is over (CreatureCmd.Kill removes them only when it isn't performing one), so they're asked too
    /// (<paramref name="performer"/>).
    /// </summary>
    internal static bool FightEnding(SimState state, int? performer = null)
    {
        foreach (var enemy in state.Enemies)
            if (enemy.Alive && !PowersOf(state, enemy.Id).Has(PowerId.Minion)) return false;
        foreach (var (model, owner) in Listeners(state))
            if (model.Overrides(nameof(SimModel.ShouldStopCombatFromEnding)) && model.ShouldStopCombatFromEnding(state, owner)) return false;
        if (performer is { } id && state.Enemy(id) is { Alive: false })
            foreach (var (model, owner) in ListenersWith(state, id))
                if (owner == id && model.Overrides(nameof(SimModel.ShouldStopCombatFromEnding)) && model.ShouldStopCombatFromEnding(state, owner)) return false;
        return true;
    }

    /// <summary>
    /// Enemies react to an ally's death as it happens (AfterDeath): Ravenous is stunned (and gains
    /// Strength for later turns), Crab Rage gains Strength (its hits) and block, once.
    /// </summary>
    private static void AllyDeaths(SimState state)
    {
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
                // Hook.ShouldCreatureBeRemovedFromCombatAfterDeath, asked as it dies (CreatureCmd.Kill, before AfterDeath): a listener may keep it in the fight.
                string? keeper = null;
                foreach (var (model, o) in ListenersWith(state, dead.Id))
                    if (model.Overrides(nameof(SimModel.ShouldCreatureBeRemovedFromCombatAfterDeath)) && !model.ShouldCreatureBeRemovedFromCombatAfterDeath(state, o, dead.Id)
                        || o == dead.Id && model.StaysOnDeadOwner(state, o))
                        keeper ??= model.Name;
                // Its own powers hear its death (CreatureCmd.Kill runs AfterDeath before RemoveAllPowersAfterDeath); no other dead one's do.
                foreach (var (model, o) in ListenersWith(state, dead.Id)) model.AfterDeath(state, o, dead.Id);
                // Brought back by its own death's code (Steam Eruption's TriggerAboutToBlowState: SetHpStep), it's in the fight again with the powers
                // that stay on their dead owner (CreatureCmd.Kill's RemoveAllPowersAfterDeath runs after AfterDeath, whatever its HP: their
                // ShouldPowerBeRemovedAfterOwnerDeath false), which listen as any living enemy's do. A dead enemy kept in the fight (read from its code)
                // isn't followed, nor a power that stays on it: the simulator's dead enemy has no listening powers.
                if (dead.Alive) Revive(state, dead);
                else if (keeper != null) state.Unmodelled ??= $"{keeper} keeps a dead enemy in the fight or stays on it: what it does there isn't followed";
            }
            var left = state.Enemies.Where(enemy => enemy.Alive).ToList();
            if (mainDied && left.Count > 0 && left.All(enemy => PowersOf(state, enemy.Id).Has(PowerId.Minion)))
                foreach (var minion in left) minion.Hp = 0;
        } while (handled);
    }

    /// <summary>
    /// An enemy's attack's hook in the enemy turn (Hook.BeforeAttack, Hook.AfterAttack: AttackCommand.Execute), every listener in the game's
    /// order, on the one state the enemy turn runs on: a listener that would branch there is an error, never a guess.
    /// </summary>
    private static void EnemyAttackHook(SimState state, string hook, AttackInfo attack)
    {
        if (!Listens(state, hook)) return;
        foreach (var (model, owner) in Listeners(state).ToArray())
        {
            if (!model.Overrides(hook)) continue;
            if (Who.IsEnemy(owner) && state.Enemy(owner) is not { Alive: true }) continue; // its powers went at its death
            var after = hook == nameof(SimModel.BeforeAttack) ? model.BeforeAttack(state, owner, attack) : model.AfterAttack(state, owner, attack);
            foreach (var branch in after)
                if (!ReferenceEquals(branch, state)) throw new InvalidOperationException($"{model.Name}: {hook} branched in the enemy turn");
        }
    }

    /// <summary>
    /// A power an enemy's move applies (PowerCmd.Apply: CanReceivePowers, then Artifact), on each creature it names that's alive now. Its
    /// AfterPowerApplied listeners in the simulator all react to their own owner's applications, never an enemy's.
    /// </summary>
    private static void MovePowers(SimState state, int enemy, MovePowerAction movePowerAction)
    {
        foreach (var to in movePowerAction.To)
        {
            var alive = Who.IsPlayer(to) ? state.Player(to) is { Hp: > 0, Died: false } : state.Enemy(to) is { Alive: true };
            if (alive) ApplyPower(state, enemy, to, movePowerAction.Power, movePowerAction.Amount, movePowerAction.Debuff);
        }
    }

    /// <summary>
    /// The enemies' side's turn starts (CombatManager.StartTurn, the one turn start a simulation reaches): the deaths at your end of turn are
    /// handled, it's the enemies' turn, then every listener's BeforeSideTurnStart and AfterSideTurnStart (then Late), its participants the
    /// living enemies: Hardened Shell's budget resets (its BeforeSideTurnStart), each enemy's poison ticks (PoisonPower.AfterSideTurnStart, read
    /// from its code) in its place among the listeners, as the game's listener order has it.
    /// </summary>
    private static void EnemyTurnStart(BranchSet branches)
    {
        string[] hooks = { nameof(SimModel.BeforeSideTurnStart), nameof(SimModel.AfterSideTurnStart), nameof(SimModel.AfterSideTurnStartLate) };
        if (!branches.States.Any(branch => hooks.Any(hook => Listens(branch, hook)))) return; // EnemyTurn starts it
        branches.Do(StartTheEnemiesTurn);
        HookStep(branches, nameof(SimModel.BeforeSideTurnStart), (model, owner, branch) => model.BeforeSideTurnStart(branch, owner));
        HookStep(branches, nameof(SimModel.AfterSideTurnStart), (model, owner, branch) => model.AfterSideTurnStart(branch, owner));
        HookStep(branches, nameof(SimModel.AfterSideTurnStartLate), (model, owner, branch) => model.AfterSideTurnStartLate(branch, owner));
    }

    private static void StartTheEnemiesTurn(SimState state)
    {
        AllyDeaths(state); // deaths at your end of turn
        state.EnemyTurnNow = true; // Inferno and the like react only on their owner's side's turn
        TurnCountsStartOver(state); // nothing of the players' turn happened "this turn" now (decision #191)
    }

    private static void EnemyTurn(SimState state)
    {
        if (!state.EnemyTurnNow) StartTheEnemiesTurn(state);
        AllyDeaths(state); // deaths in the enemies' turn start
        // The enemies the turn's start killed never act: each one's poison ticked then (PoisonPower.AfterSideTurnStart, read from its code, in
        // the game's listener order: EnemyTurnStart), stunned or not, after Hardened Shell's BeforeSideTurnStart reset its budget. Each tick is
        // damage (PoisonPower.Trigger: CreatureCmd.Damage, Unblockable | Unpowered, no dealer): the HP-loss pipeline caps it (Hardened Shell,
        // Slippery) and the damage hooks hear it (Shriek stuns at its threshold, Asleep wakes, Slumber and Plow count it); its deaths ran their
        // hooks as they happened, before any enemy moves (Ravenous stuns the other Corpse Slug; Crab Rage).
        HashSet<int>? poisoned = null;
        foreach (var enemy in state.Enemies)
        {
            if (enemy.Alive || enemy.HpAtTurnEnd <= 0 || enemy.DiedBeforeActing) continue;
            (poisoned ??= new HashSet<int>()).Add(enemy.Id);
            enemy.DiedBeforeActing = true;
            if (HitCount(enemy) > 0) state.SkippedPoisoned++;
        }
        // Each enemy's block this turn (CombatManager.StartTurn clears it unless a listener's ShouldClearBlock says no: Creature.ClearBlock, Barricade's
        // and Burrowed's read from their code; its moves' block adds to it): what the damage hitting it back during the turn meets. What your turn left
        // them (the outcome reads it) comes back once the turn is over; its count of hits through block goes on through the turn, as the wake rules
        // want (SimEnemy.UnblockedInstances).
        var turnEnd = new (int Block, bool BlockBroken)[state.Enemies.Count];
        for (var i = 0; i < state.Enemies.Count; i++)
        {
            var enemy = state.Enemies[i];
            turnEnd[i] = (enemy.Block, enemy.BlockBroken);
            enemy.Block = (ClearsBlock(state, enemy.Id) ? 0 : enemy.Block) + enemy.MoveBlock;
        }
        foreach (var enemy in state.Enemies)
        {
            if (poisoned?.Contains(enemy.Id) == true) continue;
            if (!enemy.Alive)
            {
                enemy.DiedBeforeActing = true;
                if (HitCount(enemy) > 0) state.SkippedEndOfTurn++;
                continue;
            }
            if (enemy.Stunned || HitCount(enemy) == 0 && enemy.Move == null) continue; // a stunned enemy skips its move

            var landed = 0;
            var hitsLanded = 0;
            // One player takes one hit (CreatureCmd.Damage of each living player: decision #93), through the one damage on a player (Lands):
            // Thorns before it lands, Wuppy, block, Osty, HP, then Flame Barrier and Reflect, each read from its code, hitting the enemy back
            // through the damage on it (its block this turn, the read caps, the HP-loss pipeline, its damage hooks). Yours also feeds the breakdown.
            void TakesHit(int player, int amount)
            {
                var hit = Lands(state, label: null, player, amount, SimProps.Move, enemy.Id, card: null);
                if (player != Who.You) return;
                landed += hit.Amount;
                hitsLanded++;
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
            // Its attack (AttackCommand.Execute): none at all once it's dead; else Hook.BeforeAttack, its hits until it dies (CreatureCmd.Damage
            // of a dead dealer deals nothing), Hook.AfterAttack (an enemy's Vigor keeps the attack and is spent by it). A monster's attack has no
            // card (no ModelSource) and its props are a Move.
            void Attack(int firstHit, int hits)
            {
                AttackInfo? attack = null;
                if (enemy.Alive)
                {
                    state.AttackSerial++;
                    state.CurrentAttack = state.AttackSerial;
                    attack = new AttackInfo(state.AttackSerial, enemy.Id, SimProps.Move, Card: null);
                    EnemyAttackHook(state, nameof(SimModel.BeforeAttack), attack);
                }
                for (var hit = firstHit; hit < Math.Min(firstHit + hits, HitCount(enemy)); hit++)
                {
                    // The hit on you: the intent through the damage pipeline as it lands, or the final number.
                    var perHit = enemy.Intent != null ? IntentDamage(state, enemy, hit, Who.You) : enemy.Hits[hit];
                    if (!enemy.Alive)
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
                if (attack == null) return;
                EnemyAttackHook(state, nameof(SimModel.AfterAttack), attack);
                state.CurrentAttack = 0;
            }
            if (enemy.Move == null) Attack(firstHit: 0, HitCount(enemy));
            else
            {
                // Its move as its code runs it (decision #195, MonsterModel.PerformMove): each step in turn, to the move's end even when your
                // Thorns killed it during its attack (nothing in PerformMove or PowerCmd.Apply stops there), while the fight goes on.
                void MoveActions(IReadOnlyList<SimAction> actions)
                {
                    foreach (var action in actions)
                    {
                        // The fight is ending (PowerCmd.Apply and the rest stop on CombatManager.IsEnding): no primary enemy alive and nothing
                        // holding the end back (Stock on the Axebot your Thorns killed during its attack: its next Axebot comes in).
                        if (FightEnding(state, enemy.Id)) return;
                        switch (action)
                        {
                            case MoveAttackAction moveAttackAction: Attack(moveAttackAction.FirstHit, moveAttackAction.Hits); break;
                            case MovePowerAction movePowerAction: MovePowers(state, enemy.Id, movePowerAction); break;
                            case MoveBlockAction moveBlockAction:
                                foreach (var to in moveBlockAction.To)
                                {
                                    if (state.Enemy(to) is not { Alive: true } gainer) continue;
                                    gainer.MoveBlock += moveBlockAction.Amount;
                                    gainer.Block += moveBlockAction.Amount;
                                }
                                break;
                            // CreatureCmd.Stun of itself (Creature.StunInternal: nothing once it's dead): its next move is the stunned move.
                            case MoveStunAction when enemy.Alive: Stun(enemy); break;
                            // A step on its own state (Bowlbug Rock's IsOffBalance: decision #271), asked of it as the step runs.
                            case WhenAction whenAction:
                                var holds = Holds(state, whenAction.Condition, enemy.Id);
                                if (holds == null) state.Unmodelled ??= $"{enemy.Name}'s move acts on a test the simulator can't tell";
                                else if (holds.Value) MoveActions(whenAction.Then);
                                break;
                        }
                    }
                }
                MoveActions(enemy.Move);
            }
            if (landed > 0) if (state.Log) state.Blockable.Add((hitsLanded > 1 ? $"{enemy.Name} ({hitsLanded} hits)" : enemy.Name, landed));
            // An enemy killed during the enemy turn (your Thorns, Flame Barrier or Reflect hitting back during its attack): its death hooks ran
            // as it died (the damage on it ends in CreatureCmd.Kill); any left run before the next enemy moves. Poison deaths ran theirs at the
            // turn's start, above.
            if (state.Enemies.Any(simEnemy => simEnemy.StartedAlive && !simEnemy.Alive && !simEnemy.DeathHandled)) AllyDeaths(state);
        }
        for (var i = 0; i < state.Enemies.Count && i < turnEnd.Length; i++)
            (state.Enemies[i].Block, state.Enemies[i].BlockBroken) = turnEnd[i];
    }
}
