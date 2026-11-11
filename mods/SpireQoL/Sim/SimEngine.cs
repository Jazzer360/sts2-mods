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

    /// <summary>Times the card plays (Sneaky reacts to each).</summary>
    public int Plays = 1;

    public int HandDelta = -1;
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
    public enum Kind { EvokeFront, EvokeAllTwice, Channel }

    public Kind Op;
    /// <summary>EvokeFront: how many times the front orb is evoked (removed after the last).</summary>
    public int Times = 1;
    /// <summary>Channel: the new orb (values with your Focus).</summary>
    public SimOrb? Orb;
}

/// <summary>The branches still possible after the simulation, with helpers for guarantees and the worst case.</summary>
public sealed class SimOutcome
{
    public List<SimState> Branches = new();
    /// <summary>Too many branches: results are from the explored ones only, so no guarantees.</summary>
    public bool Truncated;

    /// <summary>After truncation: the run where random hits land on nobody (an upper bound on damage to you).</summary>
    public SimState? SafeWorst;

    public SimState Worst => SafeWorst ?? Branches.OrderByDescending(b => b.Taken).ThenByDescending(b => b.OstyTaken).First();
    public int TakenMin => Branches.Min(b => b.Taken);
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
    public const int MaxBranches = 128;

    public static SimOutcome Run(SimState start, SimPlay? play = null)
    {
        var b = new BranchSet(start.Clone());
        if (play != null) ApplyPlay(b, play);
        // Kills during the play itself (its hits, orbs, procs, Juggernaut), for kill-reactive tabs.
        var aliveAtStart = start.Enemies.Where(e => e.Alive).Select(e => e.Id).ToList();
        var killedPerBranch = b.States.Select(s => aliveAtStart.Where(id => s.Enemy(id) is not { Alive: true }).ToHashSet()).ToList();
        // Enemies that gained block from Curl Up, Sneaky or Skittish because of the play, in some branch.
        var reacted = b.States.SelectMany(s => s.Enemies.Where(e => e.ReactedWithBlock).Select(e => e.Id)).ToHashSet();
        // Some enemy's block broken by the play, in every branch (Hand Drill).
        var breaksBlock = !b.Truncated && b.States.All(s => s.Enemies.Any(e => e.BlockBroken && start.Enemy(e.Id) is { BlockBroken: false }));
        // Block gains during the play (each sets off Juggernaut).
        var gains = b.States.Select(s => s.P.BlockGains - start.P.BlockGains).ToList();
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
            safeStart.NoRandomHits = true;
            var safe = new BranchSet(safeStart);
            if (play != null) ApplyPlay(safe, play);
            EndOfTurn(safe);
            safe.Do(EnemyTurn);
            outcome.SafeWorst = safe.States[0];
        }
        return outcome;
    }

    // ------------------------------------------------------------------ branching

    private sealed class BranchSet
    {
        public List<SimState> States;
        public bool Truncated;

        public BranchSet(SimState s) => States = new List<SimState> { s };

        /// <summary>Applies a step that may branch, merging identical results.</summary>
        public void Each(Func<SimState, IEnumerable<SimState>> step)
        {
            var next = new List<SimState>();
            var seen = new HashSet<long>();
            foreach (var s in States)
            {
                foreach (var r in step(s))
                {
                    if (!seen.Add(r.Key())) continue;
                    next.Add(r);
                    if (next.Count >= MaxBranches)
                    {
                        Truncated = true;
                        States = next;
                        return;
                    }
                }
            }
            States = next;
        }

        public void Do(Action<SimState> step)
        {
            foreach (var s in States) step(s);
        }
    }

    // ------------------------------------------------------------------ primitives

    /// <summary>Damage to an enemy during your turn: its block, then HP (Hardened Shell caps HP lost).</summary>
    /// <param name="powered">A powered attack (your card's hits): Flutter and The Gambit react only to those.</param>
    private static int Damage(SimEnemy e, int amount, bool powered = false)
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
        e.HitByCard = true;
        var lost = Damage(e, amount, powered: true);
        if (lost <= 0) return;
        e.ThroughByCard = true;
        if (s.P.Envenom > 0 && e.Alive) e.AddPoison(s.P.Envenom + s.P.PoisonBonus);
    }

    /// <summary>One hit on a random living enemy: a branch per possible target.</summary>
    private static IEnumerable<SimState> HitRandom(SimState s, Func<SimEnemy, int> amount, bool card = false)
    {
        void Hit(SimState st, SimEnemy e)
        {
            if (card) CardHit(st, e, amount(e));
            else Damage(e, amount(e));
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
        if (targets.Count == 1)
        {
            Hit(s, s.Enemy(targets[0])!);
            yield return s;
            yield break;
        }
        foreach (var id in targets)
        {
            var c = s.Clone();
            Hit(c, c.Enemy(id)!);
            yield return c;
        }
    }

    /// <summary>Poison on a random living enemy: a branch per possible target (none with NoRandomHits: never overstates).</summary>
    private static IEnumerable<SimState> PoisonRandom(SimState s, int amount)
    {
        var targets = s.NoRandomHits ? new List<int>() : s.Targetable.Select(e => e.Id).ToList();
        if (targets.Count <= 1)
        {
            if (targets.Count == 1) s.Enemy(targets[0])!.AddPoison(amount);
            yield return s;
            yield break;
        }
        foreach (var id in targets)
        {
            var c = s.Clone();
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
    private static IEnumerable<SimState> GainBlock(SimState s, int amount, string? log)
    {
        if (amount <= 0) return new[] { s };
        s.P.Block += amount;
        s.P.BlockGains++;
        if (log != null) if (s.Log) s.EndOfTurnBlock.Add((log, amount));
        if (s.P.Juggernaut <= 0) return new[] { s };
        var j = s.P.Juggernaut;
        if (log != null) if (s.Log) s.EndOfTurnDamage.Add(("Juggernaut", j));
        return HitRandom(s, _ => j);
    }

    private static IEnumerable<SimState> Evoke(SimState s, SimOrb orb)
    {
        switch (orb.Kind)
        {
            case OrbKind.Lightning: return HitRandom(s, _ => orb.Evoke);
            case OrbKind.Frost: return GainBlock(s, orb.Evoke, null);
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

    private static int HpLoss(SimState s, int amount, ref int remnantLeft)
    {
        if (amount <= 0) return 0;
        amount = Math.Max(0, amount - s.P.TungstenRod);
        amount = Math.Min(amount, remnantLeft);
        if (amount > 0 && s.P.Buffer > 0)
        {
            s.P.Buffer--;
            return 0; // a hit Buffer stops doesn't use Beating Remnant's budget (it counts UnblockedDamage)
        }
        if (remnantLeft != int.MaxValue) remnantLeft -= amount;
        return amount;
    }

    private static void LandBlockable(SimState s, string name, int amount)
    {
        if (amount <= 0) return;
        s.BlockDemand += amount;
        if (s.Log) s.Blockable.Add((name, amount));
        var blocked = Math.Min(s.P.Block, amount);
        s.P.Block -= blocked;
        s.DamageThrough += amount - blocked;
        var left = s.P.RemnantLeftPlayerTurn;
        s.Taken += HpLoss(s, amount - blocked, ref left);
        s.P.RemnantLeftPlayerTurn = left;
    }

    private static void LandUnblockable(SimState s, string name, int amount)
    {
        if (amount <= 0) return;
        if (s.Log) s.Unblockable.Add((name, amount));
        var left = s.P.RemnantLeftPlayerTurn;
        s.Taken += HpLoss(s, amount, ref left);
        s.P.RemnantLeftPlayerTurn = left;
    }

    // ------------------------------------------------------------------ card play

    public static void ApplyPlayTo(SimState s, SimPlay play, out List<SimState> branches, out bool truncated)
    {
        var b = new BranchSet(s.Clone());
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
                            if (last) s.P.Orbs.RemoveAt(0);
                            return Evoke(s, orb);
                        });
                    }
                    break;
                case OrbOp.Kind.EvokeAllTwice:
                    b.Each(s => EvokeAllTwice(s));
                    break;
                case OrbOp.Kind.Channel:
                    b.Each(s =>
                    {
                        IEnumerable<SimState> r = new[] { s };
                        if (s.P.OrbCapacity > 0 && s.P.Orbs.Count >= s.P.OrbCapacity)
                        {
                            var front = s.P.Orbs[0];
                            s.P.Orbs.RemoveAt(0);
                            r = Evoke(s, front);
                        }
                        return r.Select(x =>
                        {
                            if (x.P.OrbCapacity > 0) x.P.Orbs.Add(op.Orb!.Clone());
                            return x;
                        }).ToList();
                    });
                    break;
            }
        }

        // Its own block: each gain sets off Juggernaut.
        for (var i = 0; i < p.BlockGains; i++) b.Each(s => GainBlock(s, p.BlockAmount, null));

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
            s.P.HandCount = Math.Max(0, s.P.HandCount + p.HandDelta);
            s.P.Fading = Math.Max(0, s.P.Fading + p.FadingDelta);
            if (p.IsAttack) s.P.AttacksThisTurn++;
            AllyDeaths(s); // deaths from the card's hits

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

    private static IEnumerable<SimState> EvokeAllTwice(SimState s)
    {
        IEnumerable<SimState> states = new[] { s };
        var count = s.P.Orbs.Count;
        for (var i = 0; i < count; i++)
        {
            states = states.SelectMany<SimState, SimState>(x =>
            {
                if (x.P.Orbs.Count == 0) return new[] { x };
                var orb = x.P.Orbs[0];
                return Evoke(x, orb).SelectMany(y =>
                {
                    y.P.Orbs.RemoveAt(0);
                    return Evoke(y, orb);
                }).ToList();
            }).ToList();
        }
        return states;
    }

    // ------------------------------------------------------------------ end of turn

    private static void AllEnemies(SimState s, string name, int amount)
    {
        if (amount <= 0) return;
        if (s.Log) s.EndOfTurnDamage.Add((name + " (all)", amount));
        HitAll(s, _ => amount);
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

        // 2. Orb passives, front first.
        var maxOrbs = b.States.Max(s => s.P.Orbs.Count);
        for (var i = 0; i < maxOrbs; i++)
        {
            var index = i;
            int Triggers(SimState s) => index < s.P.Orbs.Count ? s.P.Orbs[index].Triggers + (index == 0 ? s.P.FrontOrbBonusTriggers : 0) : 0;
            var maxTriggers = b.States.Max(Triggers);
            for (var t = 0; t < maxTriggers; t++)
            {
                var trigger = t;
                b.Each(s =>
                {
                    if (index >= s.P.Orbs.Count) return new[] { s };
                    var orb = s.P.Orbs[index];
                    if (trigger >= Triggers(s)) return new[] { s };
                    switch (orb.Kind)
                    {
                        case OrbKind.Lightning:
                            if (s.Log) s.EndOfTurnDamage.Add(("Lightning", orb.Passive));
                            return HitRandom(s, _ => orb.Passive);
                        case OrbKind.Frost:
                            return GainBlock(s, orb.Passive, "Frost");
                        case OrbKind.Dark:
                            orb.Evoke += orb.Passive;
                            return new[] { s };
                        case OrbKind.Glass:
                            // GlassOrb.Passive: if PassiveVal (base + Focus) > 0, the base drops by 1
                            // (not below 0) and every enemy takes the value from before the drop.
                            if (orb.Passive > 0)
                            {
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
                return r.SelectMany(x => x.P.ForgottenSoulDamage > 0 ? HitRandom(x, _ => x.P.ForgottenSoulDamage) : new[] { x }).ToList();
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

        // 5. AfterSideTurnEnd: Consuming Shadow evokes (last orb first), then Parrying Shield.
        var maxShadow = b.States.Max(s => s.P.ConsumingShadow);
        for (var i = 0; i < maxShadow; i++)
        {
            var n = i;
            b.Each(s =>
            {
                if (n >= s.P.ConsumingShadow || s.P.Orbs.Count == 0) return new[] { s };
                var orb = s.P.Orbs[^1];
                s.P.Orbs.RemoveAt(s.P.Orbs.Count - 1);
                return orb.Kind == OrbKind.Frost ? GainBlock(s, orb.Evoke, "Consuming Shadow (Frost)") : Evoke(s, orb);
            });
        }
        b.Each(s => s.P.ParryingShieldDamage > 0 && s.P.Block >= s.P.ParryingShieldBlock ? HitRandom(s, _ => s.P.ParryingShieldDamage) : new[] { s });
        // AfterSideTurnEndLate: Disintegration.
        b.Do(s =>
        {
            foreach (var (name, amount) in s.P.DebuffLateBlockable) LandBlockable(s, name, amount);
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

            foreach (var perHit in e.Hits)
            {
                if (dead)
                {
                    s.CancelledHits++;
                    continue;
                }
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
            }
            e.Attacked = hitsLanded > 0;
            if (landed > 0) if (s.Log) s.Blockable.Add((hitsLanded > 1 ? $"{e.Name} ({hitsLanded} hits)" : e.Name, landed));
        }
    }
}
