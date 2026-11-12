namespace SpireQoL.Sim;

/// <summary>A card play, as the simulator applies it (built by SimAdapter from PlayFacts and the proc rules).</summary>
public sealed class SimPlay
{
    public enum Target { None, Single, All, Random }

    public string Name = "";
    public Target HitTarget = Target.None;
    /// <summary>For Target.Single: the enemy aimed at.</summary>
    public int TargetId = -1;
    /// <summary>Damage of one hit on each enemy (after the game's damage math for that enemy).</summary>
    public Dictionary<int, int> HitDamage = new();
    /// <summary>Hits per target (Single/All), or random hits in total (Random).</summary>
    public int Hits;

    public int BlockAmount;
    public int BlockGains;

    public List<OrbOp> OrbOps = new();

    public int AddPlating, AddJuggernaut, AddFeelNoPain, AddThorns, AddFlameBarrier;
    /// <summary>Poison applied to the target (Single) or to every enemy (All).</summary>
    public int Poison;
    public bool PoisonAll;
    /// <summary>Poison applied to a random enemy, RandomPoisonTimes times (Bouncing Flask).</summary>
    public int RandomPoison, RandomPoisonTimes;

    /// <summary>
    /// Enemies whose intent damage the play changes (Weak it applies): their hits afterwards, per enemy id.
    /// </summary>
    public Dictionary<int, List<int>> SetHits = new();
    /// <summary>The same for teammates: enemy id → hits on each teammate (by teammate index) afterwards.</summary>
    public Dictionary<int, List<List<int>>> SetTeammateHits = new();

    /// <summary>Times the card plays (Sneaky reacts to each).</summary>
    public int Plays = 1;
    /// <summary>Focus it gives or takes after its hits (Defragment, Hyperbeam...): your orbs' values change with it.</summary>
    public int FocusDelta;

    public int HandDelta = -1;
    /// <summary>It kills your Osty (Sacrifice, Bone Shards: CreatureCmd.Kill on Osty).</summary>
    public bool KillsOsty;
    public int FadingDelta;
    public bool IsAttack;

    /// <summary>Damage procs it sets off (relics and powers), in order.</summary>
    public List<ProcHit> Procs = new();
    /// <summary>Block procs it sets off: one entry per block gain.</summary>
    public List<int> ProcBlock = new();
}

public sealed class ProcHit
{
    public SimPlay.Target Spread;
    public int TargetId = -1;
    public Dictionary<int, int> Damage = new();
    public int Times = 1;
}

public sealed class OrbOp
{
    public enum Kind { EvokeFront, EvokeAllTwice, Channel, Slots }

    public Kind Op;
    /// <summary>EvokeFront: how many times the front orb is evoked (removed after the last).</summary>
    public int Times = 1;
    /// <summary>Channel: the new orb (values with your Focus).</summary>
    public SimOrb? Orb;
    /// <summary>
    /// Slots: orb slots gained (OrbCmd.AddSlots, up to 10) or lost (negative: OrbCmd.RemoveSlots, not
    /// below 0; the orbs past the new capacity are dropped from the back, not evoked).
    /// </summary>
    public int Slots;
}

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
public static class SimEngine
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

    /// <summary>
    /// The plays still resolving or queued (leadIn, in resolution order) first, then this card, then the
    /// end of the turn and the enemy turn (decision #27). The card's own results (kills, block breaks, block
    /// gains, reactions) are measured from each branch's state after the lead-in.
    /// </summary>
    public static SimOutcome Run(SimState start, IReadOnlyList<SimPlay>? leadIn, SimPlay? play)
    {
        var work = new SimWork();
        var first = start.Clone();
        first.Work = work;
        var b = new BranchSet(first);
        if (leadIn is { Count: > 0 })
        {
            foreach (var queued in leadIn) ApplyPlay(b, queued);
            b.Do(s =>
            {
                s.PlayBase = new SimState.PlayBaseline(
                    s.Enemies.Where(e => e.Alive).Select(e => e.Id).ToHashSet(),
                    s.Enemies.Where(e => e.BlockBroken).Select(e => e.Id).ToHashSet(),
                    s.P.BlockGains);
                foreach (var e in s.Enemies) e.ReactedWithBlock = false;
            });
        }
        if (play != null) ApplyPlay(b, play);
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
        };
        if (b.Truncated)
        {
            // Too many branches to explore: nothing is guaranteed, and damage to you comes from a run
            // where random hits land on nobody, so no attacker is ever saved by one (never an underestimate).
            var safeStart = start.Clone();
            safeStart.Work = work;
            safeStart.NoRandomHits = true;
            var safe = new BranchSet(safeStart);
            if (leadIn != null) foreach (var queued in leadIn) ApplyPlay(safe, queued);
            if (play != null) ApplyPlay(safe, play);
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
                    if (seen.TryGetValue(key, out var same)) same.Weight += r.Weight;
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

    /// <summary>Damage to an enemy during your turn: its block, then HP (Hardened Shell caps HP lost).</summary>
    /// <param name="powered">A powered attack (your card's hits): Flutter and The Gambit react only to those.</param>
    /// <param name="minimumHpLoss">The Boot: a powered hit that loses it at least 1 HP loses it at least this much.</param>
    private static int Damage(SimEnemy e, int amount, bool powered = false, int minimumHpLoss = 0)
    {
        if (amount <= 0 || !e.Alive) return 0;
        amount = Math.Min(amount, e.HitCap);
        var hadBlock = e.Block > 0;
        var absorbed = Math.Min(e.Block, amount);
        e.Block -= absorbed;
        if (hadBlock && e.Block == 0) e.BlockBroken = true;
        if (hadBlock && e.Block == 0 && e.Burrowed)
        {
            // BurrowedPower.AfterBlockBroken: stunned; the power goes and takes all its block (AfterRemoved).
            e.Burrowed = false;
            e.Barricade = false;
            Stun(e);
        }
        var through = amount - absorbed;
        if (through <= 0) return 0;
        var toHp = Math.Min(through, Math.Max(0, e.ShellLeft));
        if (e.Slippery > 0 && toHp >= 1)
        {
            // SlipperyPower.ModifyHpLostAfterOsty: at most 1 HP per hit; one charge per hit.
            toHp = 1;
            e.Slippery--;
            e.SlipperyTriggered = true;
        }
        // TheBoot.ModifyHpLostAfterOstyLate: last in the game's HP-loss order (after Hardened Shell and Slippery).
        if (powered && toHp >= 1 && toHp < minimumHpLoss) toHp = minimumHpLoss;
        if (e.ShellLeft != int.MaxValue) e.ShellLeft -= toHp;
        e.Hp -= toHp;
        if (toHp <= 0) return 0;
        e.UnblockedInstances++; // UnblockedDamage is 0 when Hardened Shell stops it
        if (e.Alive && e.StunAtHp > 0 && e.Hp <= e.StunAtHp)
        {
            // Plow / Shriek: any damage through that leaves it at or below the threshold.
            e.StunAtHp = 0;
            Stun(e);
        }
        if (powered && e.Alive && e.Flutter > 0 && --e.Flutter == 0) Stun(e);
        if (powered && e.Alive && e.Gambit)
        {
            e.Gambit = false;
            e.GambitTriggered = true;
            e.Hp = 0; // TheGambitPower: it dies
        }
        return toHp;
    }

    private static void Stun(SimEnemy e)
    {
        e.Stunned = true;
        e.StunnedThisTurn = true;
    }

    /// <summary>
    /// One hit of your card's attack (a powered attack from a card): marks it for Curl Up and Skittish,
    /// and Envenom poisons it when damage gets through (EnvenomPower.AfterDamageGiven; Snecko Skull adds).
    /// </summary>
    private static void CardHit(SimState s, SimEnemy e, int amount)
    {
        if (amount <= 0 || !e.Alive) return;
        // ThornsPower.BeforeDamageReceived: it hits you back before the hit lands (the hit still lands).
        if (e.Thorns > 0) LandBlockable(s, e.Name + " Thorns", e.Thorns);
        e.HitByCard = true;
        var lost = Damage(e, amount, powered: true, minimumHpLoss: s.P.BootMinimum);
        if (lost <= 0) return;
        e.ThroughByCard = true;
        if (s.P.Envenom > 0 && e.Alive) e.AddPoison(s.P.Envenom + s.P.PoisonBonus);
    }

    /// <summary>One hit on a random living enemy: a branch per possible target.</summary>
    private static IEnumerable<SimState> HitRandom(SimState s, Func<SimEnemy, int> amount, bool card = false, Action<SimState, SimEnemy>? then = null)
    {
        void Hit(SimState st, SimEnemy e)
        {
            if (card) CardHit(st, e, amount(e));
            else Damage(e, amount(e));
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

    /// <summary>Poison on a random living enemy: a branch per possible target (none with NoRandomHits: never overstates).</summary>
    private static IEnumerable<SimState> PoisonRandom(SimState s, int amount)
    {
        var targets = s.NoRandomHits ? new List<int>() : s.Targetable.Select(e => e.Id).ToList();
        if (targets.Count <= 1 || !Branch(s))
        {
            if (targets.Count >= 1) s.Enemy(targets[0])!.AddPoison(amount);
            yield return s;
            yield break;
        }
        foreach (var id in targets)
        {
            var c = s.Clone();
            c.Weight = s.Weight / targets.Count;
            c.Enemy(id)!.AddPoison(amount);
            yield return c;
        }
    }

    private static void HitAll(SimState s, Func<SimEnemy, int> amount, bool card = false)
    {
        foreach (var e in s.Targetable.ToList())
        {
            if (card) CardHit(s, e, amount(e));
            else Damage(e, amount(e));
        }
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
            r = HitRandom(s, _ => j);
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
            case OrbKind.Lightning: return HitRandom(s, _ => orb.Evoke, then: (st, e) => { var t = thunder ?? st.P.Thunder; if (t > 0 && e.Alive) Damage(e, t); });
            case OrbKind.Frost: return GainBlock(s, orb.Evoke, null, who);
            case OrbKind.Dark:
            {
                var weakest = s.Targetable.OrderBy(e => e.Hp).FirstOrDefault();
                if (weakest != null) Damage(weakest, orb.Evoke);
                return new[] { s };
            }
            case OrbKind.Glass:
                HitAll(s, _ => orb.Evoke);
                return new[] { s };
            default:
                return new[] { s };
        }
    }

    private static int HpLoss(SimState s, int amount, ref int remnantLeft) => HpLoss(s.P, amount, ref remnantLeft);

    private static int HpLoss(SimPlayer p, int amount, ref int remnantLeft)
    {
        if (amount <= 0) return 0;
        amount = Math.Max(0, amount - p.TungstenRod);
        amount = Math.Min(amount, remnantLeft);
        if (amount > 0 && p.Buffer > 0)
        {
            p.Buffer--;
            return 0; // a hit Buffer stops doesn't use Beating Remnant's budget (it counts UnblockedDamage)
        }
        if (remnantLeft != int.MaxValue) remnantLeft -= amount;
        return amount;
    }

    private static void LandBlockable(SimState s, string name, int amount, int who = -1)
    {
        if (amount <= 0) return;
        if (who >= 0)
        {
            var t = s.MutableTeammate(who);
            t.Incoming += amount;
            var tb = Math.Min(t.Block, amount);
            t.Block -= tb;
            t.Through += amount - tb;
            TeammateHpLoss(t, amount - tb);
            return;
        }
        s.BlockDemand += amount;
        if (s.Log) s.Blockable.Add((name, amount));
        var blocked = Math.Min(s.P.Block, amount);
        s.P.Block -= blocked;
        s.DamageThrough += amount - blocked;
        var left = s.P.RemnantLeftPlayerTurn;
        s.Taken += HpLoss(s, amount - blocked, ref left);
        s.P.RemnantLeftPlayerTurn = left;
    }

    /// <summary>A teammate losing HP (Tungsten Rod, Buffer, Beating Remnant like yours).</summary>
    private static void TeammateHpLoss(SimPlayer t, int amount)
    {
        var left = t.RemnantLeftPlayerTurn;
        var lost = Math.Min(HpLoss(t, amount, ref left), Math.Max(0, t.Hp)); // HP stops at 0
        t.RemnantLeftPlayerTurn = left;
        t.Taken += lost;
        t.Hp -= lost;
    }

    private static void LandUnblockable(SimState s, string name, int amount, int who = -1)
    {
        if (amount <= 0) return;
        if (who >= 0)
        {
            var t = s.MutableTeammate(who);
            t.Incoming += amount;
            TeammateHpLoss(t, amount);
            return;
        }
        if (s.Log) s.Unblockable.Add((name, amount));
        var left = s.P.RemnantLeftPlayerTurn;
        s.Taken += HpLoss(s, amount, ref left);
        s.P.RemnantLeftPlayerTurn = left;
    }

    // ------------------------------------------------------------------ card play

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

    private static void ApplyPlay(BranchSet b, SimPlay p)
    {
        // The card's hits.
        int Hit(SimEnemy e) => p.HitDamage.TryGetValue(e.Id, out var d) ? d : 0;
        switch (p.HitTarget)
        {
            case SimPlay.Target.Single:
                b.Do(s =>
                {
                    var e = s.Enemy(p.TargetId);
                    if (e == null) return;
                    for (var i = 0; i < p.Hits; i++) CardHit(s, e, Hit(e));
                });
                break;
            case SimPlay.Target.All:
                b.Do(s =>
                {
                    for (var i = 0; i < p.Hits; i++) HitAll(s, Hit, card: true);
                });
                break;
            case SimPlay.Target.Random:
                for (var i = 0; i < p.Hits; i++) b.Each(s => HitRandom(s, Hit, card: true));
                break;
        }

        // Orbs it evokes and channels (channelling into full slots evokes the front orb first).
        foreach (var op in p.OrbOps)
        {
            switch (op.Op)
            {
                case OrbOp.Kind.EvokeFront:
                    for (var t = 0; t < op.Times; t++)
                    {
                        var last = t == op.Times - 1;
                        b.Each(s =>
                        {
                            if (s.P.Orbs.Count == 0) return new[] { s };
                            var orb = s.P.Orbs[0];
                            if (last) s.P.OwnOrbs().RemoveAt(0);
                            return Evoke(s, orb);
                        });
                    }
                    break;
                case OrbOp.Kind.EvokeAllTwice:
                    // Front orb first, each evoked twice (then gone). One step per evoke, so identical
                    // branches merge and the branch cap holds (one step for all of them was 4^16 copies
                    // with 4 enemies and 8 Lightning orbs).
                    for (var i = b.States.Max(s => s.P.Orbs.Count); i > 0; i--)
                    {
                        b.Each(s => s.P.Orbs.Count == 0 ? new[] { s } : Evoke(s, s.P.Orbs[0]));
                        b.Each(s =>
                        {
                            if (s.P.Orbs.Count == 0) return new[] { s };
                            var orb = s.P.Orbs[0];
                            s.P.OwnOrbs().RemoveAt(0);
                            return Evoke(s, orb);
                        });
                    }
                    break;
                case OrbOp.Kind.Slots:
                    b.Do(s =>
                    {
                        s.P.OrbCapacity = Math.Clamp(s.P.OrbCapacity + op.Slots, 0, Math.Max(MaxOrbSlots, s.P.OrbCapacity));
                        while (s.P.Orbs.Count > s.P.OrbCapacity) s.P.OwnOrbs().RemoveAt(s.P.Orbs.Count - 1);
                    });
                    break;
                case OrbOp.Kind.Channel:
                    b.Each(s =>
                    {
                        IEnumerable<SimState> r = new[] { s };
                        if (s.P.OrbCapacity == 0 && s.P.SlotOnFirstChannel) s.P.OrbCapacity = 1;
                        if (s.P.OrbCapacity > 0 && s.P.Orbs.Count >= s.P.OrbCapacity)
                        {
                            var front = s.P.Orbs[0];
                            s.P.OwnOrbs().RemoveAt(0);
                            r = Evoke(s, front);
                        }
                        return r.Select(x =>
                        {
                            if (x.P.OrbCapacity > 0) x.P.OwnOrbs().Add(op.Orb!.Clone());
                            return x;
                        }).ToList();
                    });
                    break;
            }
        }

        // Its own block: each gain sets off Juggernaut.
        for (var i = 0; i < p.BlockGains; i++) b.Each(s => GainBlock(s, p.BlockAmount, null));
        if (p.KillsOsty) b.Do(s => s.P.OstyHp = 0);

        // Powers it gives, and poison.
        b.Do(s =>
        {
            s.P.Plating += p.AddPlating;
            s.P.Juggernaut += p.AddJuggernaut;
            s.P.FeelNoPain += p.AddFeelNoPain;
            s.P.Thorns += p.AddThorns;
            s.P.FlameBarrier += p.AddFlameBarrier;
            if (p.Poison > 0)
            {
                if (p.PoisonAll)
                {
                    foreach (var e in s.Targetable) e.AddPoison(p.Poison);
                }
                else if (s.Enemy(p.TargetId) is { Alive: true } t)
                {
                    t.AddPoison(p.Poison);
                }
            }
            foreach (var (id, hits) in p.SetHits)
            {
                if (s.Enemy(id) is { Alive: true } e) e.Hits = hits; // shared, never changed in place
            }
            foreach (var (id, hits) in p.SetTeammateHits)
            {
                if (s.Enemy(id) is { Alive: true } e) e.TeammateHits = hits; // shared, never changed in place
            }
            s.P.HandCount = Math.Max(0, s.P.HandCount + p.HandDelta);
            s.P.Fading = Math.Max(0, s.P.Fading + p.FadingDelta);
            if (p.IsAttack) s.P.AttacksThisTurn++;
            AllyDeaths(s); // deaths from the card's hits
            if (p.FocusDelta != 0) ChangeFocus(s, p.FocusDelta);

            // Enemy reactions once the card is played (AfterCardPlayed; powers fire before your relics,
            // so this block is up before relic procs hit): Curl Up, Sneaky, Skittish.
            foreach (var e in s.Enemies)
            {
                if (e.Alive)
                {
                    if (e.CurlUp > 0 && e.HitByCard)
                    {
                        e.Block += e.CurlUp;
                        e.CurlUp = 0;
                        e.ReactedWithBlock = true;
                    }
                    if (e.Sneaky > 0 && p.IsAttack)
                    {
                        e.Block += e.Sneaky * Math.Max(1, p.Plays);
                        e.ReactedWithBlock = true;
                    }
                    if (e.Skittish > 0 && e.ThroughByCard)
                    {
                        e.Block += e.Skittish;
                        e.Skittish = 0;
                        e.ReactedWithBlock = true;
                    }
                }
                e.HitByCard = e.ThroughByCard = false;
            }
        });

        // Poison on random enemies (one branch per target each time).
        for (var i = 0; i < p.RandomPoisonTimes && p.RandomPoison > 0; i++) b.Each(s => PoisonRandom(s, p.RandomPoison));

        // Procs it sets off.
        foreach (var proc in p.Procs)
        {
            int Dmg(SimEnemy e) => proc.Damage.TryGetValue(e.Id, out var d) ? d : 0;
            for (var t = 0; t < proc.Times; t++)
            {
                switch (proc.Spread)
                {
                    case SimPlay.Target.Single:
                        b.Do(s =>
                        {
                            if (s.Enemy(proc.TargetId) is { } e) Damage(e, Dmg(e));
                        });
                        break;
                    case SimPlay.Target.All:
                        b.Do(s => HitAll(s, Dmg));
                        break;
                    case SimPlay.Target.Random:
                        b.Each(s => HitRandom(s, Dmg));
                        break;
                }
            }
        }
        foreach (var amount in p.ProcBlock) b.Each(s => GainBlock(s, amount, null));

        b.Do(AllyDeaths); // deaths from its procs
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

    /// <summary>Obsidian Vice: damage × cards in hand (a teammate's hand size is known; -1 unknown: none) to a random enemy.</summary>
    private static IEnumerable<SimState> ObsidianVice(SimState s, SimPlayer p, int who)
    {
        if (p.ObsidianViceDamage <= 0 || p.HandCount <= 0 || (who >= 0 && p.Hp <= 0)) return new[] { s };
        var amount = p.ObsidianViceDamage * p.HandCount;
        if (who < 0 && s.Log) s.EndOfTurnDamage.Add(($"Obsidian Vice ×{p.HandCount}", amount));
        return HitRandom(s, _ => amount);
    }

    private static void EndOfTurn(BranchSet b)
    {
        // 1. BeforeSideTurnEnd. Orichalcum checks your block before any of this lands.
        b.Do(s => s.P.OrichalcumArmed = s.P.Block <= 0);
        b.Each(s => GainBlock(s, s.P.Plating, "Plating"));
        // Hook listeners run your powers before your relics (CombatState.IterateHookListeners).
        b.Do(s =>
        {
            if (s.P.Hailstorm > 0 && s.P.Orbs.Count(o => o.Kind == OrbKind.Frost) >= s.P.HailstormFrostNeeded) AllEnemies(s, "Hailstorm", s.P.Hailstorm);
            AllEnemies(s, "The Bomb", s.P.TheBombDamage);
        });
        b.Each(s => s.P.OrichalcumArmed ? GainBlock(s, s.P.OrichalcumBlock, "Orichalcum") : new[] { s });
        b.Each(s => s.P.HandCount > 0 ? GainBlock(s, s.P.HandCount * s.P.CloakClaspPerCard, s.P.CloakClaspPerCard > 0 ? $"Cloak Clasp ×{s.P.HandCount}" : null) : new[] { s });
        b.Each(s => s.P.AttacksThisTurn == 0 ? GainBlock(s, s.P.RippleBasinBlock, "Ripple Basin") : new[] { s });
        b.Do(s =>
        {
            if (s.P.HandCount == 0) AllEnemies(s, "Screaming Flagon", s.P.ScreamingFlagonDamage);
            AllEnemies(s, "Stone Calendar", s.P.StoneCalendarDamage);
        });
        // Obsidian Vice (BeforeSideTurnEnd): its damage per card in your hand, to a random enemy (one hit).
        b.Each(s => ObsidianVice(s, s.P, -1));
        // Teammates' BeforeSideTurnEnd damage to enemies (it hits every enemy, so their order doesn't matter).
        b.Do(s =>
        {
            foreach (var t in s.Teammates)
            {
                if (t.Hailstorm > 0 && t.Orbs.Count(o => o.Kind == OrbKind.Frost) >= t.HailstormFrostNeeded) AllEnemies(s, "Hailstorm (teammate)", t.Hailstorm);
                AllEnemies(s, "The Bomb (teammate)", t.TheBombDamage);
                if (t.HandCount == 0) AllEnemies(s, "Screaming Flagon (teammate)", t.ScreamingFlagonDamage); // their hand: -1 (unknown) doesn't count
                AllEnemies(s, "Stone Calendar (teammate)", t.StoneCalendarDamage);
            }
        });
        for (var k = 0; k < (b.States.Count > 0 ? b.States[0].Teammates.Count : 0); k++)
        {
            var tk = k;
            b.Each(s => ObsidianVice(s, s.Teammates[tk], tk));
        }
        // Teammates' block at BeforeSideTurnEnd: Plating, then Orichalcum when they had no block. It
        // protects them, and their Beacon of Hope shares half of it (with you too).
        var team = b.States.Count > 0 ? b.States[0].Teammates.Count : 0;
        for (var k = 0; k < team; k++)
        {
            var tk = k;
            b.Each(s =>
            {
                var t = s.Teammates[tk];
                if (t.Hp <= 0) return new[] { s };
                var armed = t.Block <= 0;
                var r = GainBlock(s, t.Plating, null, tk);
                r = armed ? Then(r, x => GainBlock(x, x.Teammates[tk].OrichalcumBlock, null, tk)) : r;
                // Their relics, as yours: Cloak Clasp per card in their hand (its size is on screen), Ripple Basin
                // when they played no attack this turn.
                r = Then(r, x => x.Teammates[tk].HandCount > 0 ? GainBlock(x, x.Teammates[tk].HandCount * x.Teammates[tk].CloakClaspPerCard, null, tk) : new[] { x });
                return Then(r, x => x.Teammates[tk].AttacksThisTurn == 0 ? GainBlock(x, x.Teammates[tk].RippleBasinBlock, null, tk) : new[] { x });
            });
        }

        // 2. Orb passives, each player in player order (CombatManager.DoTurnEnd), front first.
        var teammates = b.States.Count > 0 ? b.States[0].Teammates.Count : 0;
        var before = b.States.Count > 0 ? Math.Min(b.States[0].TeammatesBeforeMe, teammates) : 0;
        for (var k = 0; k < before; k++)
        {
            var tk = k;
            OrbPassives(b, s => s.Teammates[tk], tk, mutable: s => s.MutableTeammate(tk));
        }
        OrbPassives(b, s => s.P, -1);
        for (var k = before; k < teammates; k++)
        {
            var tk = k;
            OrbPassives(b, s => s.Teammates[tk], tk, mutable: s => s.MutableTeammate(tk));
        }

        // 3. Ethereal cards exhaust (CardCmd.Exhaust in DoTurnEnd), then cards in hand act (DoTurnEndCards).
        var maxFading = b.States.Max(s => s.P.Fading);
        for (var i = 0; i < maxFading; i++)
        {
            var n = i;
            b.Each(s =>
            {
                if (n >= s.P.Fading) return new[] { s };
                IEnumerable<SimState> r = GainBlock(s, s.P.FeelNoPain, s.P.FeelNoPain > 0 ? "Feel No Pain" : null);
                r = r.Select(x =>
                {
                    if (x.P.CharonsAshesDamage > 0)
                    {
                        if (x.Log) x.EndOfTurnDamage.Add(("Charon's Ashes (all)", x.P.CharonsAshesDamage));
                        HitAll(x, _ => x.P.CharonsAshesDamage);
                    }
                    return x;
                }).ToList();
                return Then(r, x => x.P.ForgottenSoulDamage > 0 ? HitRandom(x, _ => x.P.ForgottenSoulDamage) : new[] { x });
            });
        }
        b.Do(s =>
        {
            foreach (var (name, amount) in s.P.HandBlockable) LandBlockable(s, name, amount);
            foreach (var (name, amount) in s.P.HandUnblockable) LandUnblockable(s, name, amount);
            for (var i = 0; i < s.P.RegretCards; i++) LandUnblockable(s, s.P.RegretCards > 1 ? $"Regret {i + 1}" : "Regret", s.P.HandCount);
        });

        // 4. Debuffs on you.
        b.Do(s =>
        {
            foreach (var (name, amount) in s.P.DebuffBlockable) LandBlockable(s, name, amount);
            foreach (var (name, amount) in s.P.DebuffUnblockable) LandUnblockable(s, name, amount);
        });
        // Teammates' cards in hand and debuffs, on them (decision #25: their hand is used here; it only
        // changes their own HP, which nothing shown depends on yet).
        b.Do(s =>
        {
            for (var k = 0; k < s.Teammates.Count; k++)
            {
                var t = s.Teammates[k];
                if (t.Hp <= 0) continue;
                foreach (var (name, amount) in t.HandBlockable) LandBlockable(s, name, amount, k);
                foreach (var (name, amount) in t.HandUnblockable) LandUnblockable(s, name, amount, k);
                for (var i = 0; i < t.RegretCards; i++) LandUnblockable(s, "Regret", Math.Max(0, t.HandCount), k);
                foreach (var (name, amount) in t.DebuffBlockable) LandBlockable(s, name, amount, k);
                foreach (var (name, amount) in t.DebuffUnblockable) LandUnblockable(s, name, amount, k);
            }
        });

        // 5. AfterSideTurnEnd: Consuming Shadow evokes (last orb first), then Parrying Shield.
        var maxShadow = b.States.Max(s => s.P.ConsumingShadow);
        for (var i = 0; i < maxShadow; i++)
        {
            var n = i;
            b.Each(s =>
            {
                if (n >= s.P.ConsumingShadow || s.P.Orbs.Count == 0) return new[] { s };
                var orb = s.P.Orbs[^1];
                s.P.OwnOrbs().RemoveAt(s.P.Orbs.Count - 1);
                return orb.Kind == OrbKind.Frost ? GainBlock(s, orb.Evoke, "Consuming Shadow (Frost)") : Evoke(s, orb);
            });
        }
        for (var k = 0; k < teammates; k++)
        {
            var tk = k;
            var maxTeamShadow = b.States.Max(s => s.Teammates[tk].ConsumingShadow);
            for (var i = 0; i < maxTeamShadow; i++)
            {
                var n = i;
                b.Each(s =>
                {
                    if (n >= s.Teammates[tk].ConsumingShadow || s.Teammates[tk].Orbs.Count == 0) return new[] { s };
                    var t = s.MutableTeammate(tk);
                    if (n >= t.ConsumingShadow || t.Orbs.Count == 0) return new[] { s };
                    var orb = t.Orbs[^1];
                    t.OwnOrbs().RemoveAt(t.Orbs.Count - 1);
                    return Evoke(s, orb, t.Thunder, who: tk);
                });
            }
        }
        // Parrying Shield (AfterSideTurnEnd): a random enemy, when your block is at least its threshold.
        b.Each(s =>
        {
            if (s.P.ParryingShieldDamage <= 0 || s.P.Block < s.P.ParryingShieldBlock) return new[] { s };
            s.ParryingShieldFired = true;
            return HitRandom(s, _ => s.P.ParryingShieldDamage);
        });
        for (var k = 0; k < teammates; k++)
        {
            var tk = k;
            b.Each(s => s.Teammates[tk] is { Hp: > 0, ParryingShieldDamage: > 0 } t && t.Block >= t.ParryingShieldBlock
                ? HitRandom(s, _ => s.Teammates[tk].ParryingShieldDamage) : new[] { s });
        }
        // AfterSideTurnEndLate: Disintegration.
        b.Do(s =>
        {
            foreach (var (name, amount) in s.P.DebuffLateBlockable) LandBlockable(s, name, amount);
            for (var k = 0; k < s.Teammates.Count; k++)
            {
                if (s.Teammates[k].Hp <= 0) continue;
                foreach (var (name, amount) in s.Teammates[k].DebuffLateBlockable) LandBlockable(s, name, amount, k);
            }
        });

        b.Do(s =>
        {
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

    private static void AllyDeaths(SimState s)
    {
        foreach (var e in s.Enemies)
        {
            if (!e.Alive || !s.Enemies.Any(o => o != e && o.StartedAlive && !o.Alive)) continue;
            if (e.Ravenous && !e.Stunned)
            {
                e.Stunned = true;
                e.StunnedThisTurn = true;
            }
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
        // Beating Remnant's budget carries over from your turn (it resets only when your turn starts).
        var remnant = s.P.RemnantLeftPlayerTurn;
        foreach (var e in s.Enemies)
        {
            if (!e.Alive)
            {
                e.DiedBeforeActing = true;
                if (e.Hits.Count > 0) s.SkippedEndOfTurn++;
                continue;
            }
            // Poison ticks at the start of its turn, stunned or not; Hardened Shell's budget resets then.
            e.ShellLeft = e.ShellPerTurn;
            var poison = Math.Min(e.Poison, e.ShellLeft);
            e.Hp -= poison;
            if (e.ShellLeft != int.MaxValue) e.ShellLeft -= poison;
            if (!e.Alive)
            {
                e.DiedBeforeActing = true;
                if (e.Hits.Count > 0) s.SkippedPoisoned++;
                continue;
            }
            if (e.Stunned || e.Hits.Count == 0) continue; // a stunned enemy skips its move
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
                var amount = k < e.TeammateHits.Count && h < e.TeammateHits[k].Count ? e.TeammateHits[k][h] : mine;
                t.Incoming += amount;
                if (t.Thorns > 0) Retaliate(t.Thorns);
                var tBlocked = Math.Min(t.Block, amount);
                t.Block -= tBlocked;
                var tThrough = amount - tBlocked;
                var tOsty = Math.Min(tThrough, t.OstyHp);
                t.OstyHp -= tOsty;
                t.OstyTaken += tOsty;
                t.Through += tThrough - tOsty;
                TeammateHpLoss(t, tThrough - tOsty);
                if (!dead && t.FlameBarrier > 0) Retaliate(t.FlameBarrier);
                if (!dead && t.Reflect && tBlocked > 0) Retaliate(tBlocked);
            }
            var before = Math.Min(s.TeammatesBeforeMe, s.Teammates.Count);
            var hitIndex = -1;
            foreach (var perHit in e.Hits)
            {
                hitIndex++;
                if (dead)
                {
                    s.CancelledHits++;
                    continue;
                }
                for (var k = 0; k < before; k++) TeammateTakesHit(k, hitIndex, perHit);
                // Thorns: before the hit lands; the hit still lands even if this kills.
                if (s.P.Thorns > 0) Retaliate(s.P.Thorns);

                if (hitsLanded == 0 && e.Imbalanced) s.FullBlockNeeded[e.Id] = s.BlockDemand + perHit;
                s.BlockDemand += perHit;
                var blockBefore = s.P.Block;
                var blocked = Math.Min(s.P.Block, perHit);
                s.P.Block -= blocked;
                var through = perHit - blocked;
                var toOsty = Math.Min(through, s.P.OstyHp);
                s.P.OstyHp -= toOsty;
                s.OstyTaken += toOsty;
                s.DamageThrough += through - toOsty;
                var lost = HpLoss(s, through - toOsty, ref remnant);
                s.Taken += lost;
                // DamageResult.WasFullyBlocked: you had block and no damage got through after the
                // AfterOsty modifiers (Tungsten Rod, Buffer...). Block only drops during an attack, so
                // the first hit decides it.
                if (hitsLanded == 0) e.FirstHitFullyBlocked = perHit > 0 && (blocked > 0 || blockBefore > 0) && toOsty == 0 && lost == 0;
                landed += perHit;
                hitsLanded++;

                if (!dead && s.P.FlameBarrier > 0) Retaliate(s.P.FlameBarrier);
                if (!dead && s.P.Reflect && blocked > 0) Retaliate(blocked);
                for (var k = before; k < s.Teammates.Count; k++) TeammateTakesHit(k, hitIndex, perHit);
            }
            e.Attacked = hitsLanded > 0;
            if (landed > 0) if (s.Log) s.Blockable.Add((hitsLanded > 1 ? $"{e.Name} ({hitsLanded} hits)" : e.Name, landed));
        }
    }
}
