namespace SpireQoL.Sim;

/// <summary>
/// Plain combat state for the outcome simulator: no game types, so the simulator can be tested on
/// its own (tests/SpireQoL.Sim.Tests). <see cref="SimAdapter"/> builds it from the live game.
/// Amounts are already through the game's modifiers where the game would apply them (damage per
/// enemy is precomputed per target; block amounts are what you'd gain).
/// </summary>
public enum OrbKind { Lightning, Frost, Dark, Glass, Plasma, Unknown }

public sealed class SimOrb
{
    public OrbKind Kind;
    public int Passive;
    public int Evoke;
    /// <summary>Passive triggers at end of turn (Hook.ModifyOrbPassiveTriggerCount).</summary>
    public int Triggers = 1;

    /// <summary>
    /// Glass only: the stored base value (GlassOrb._passiveVal, starts at 4) and what Focus adds to it.
    /// Passive = max(0, base + bonus); each passive firing lowers the BASE by 1 (not below 0), so a fully
    /// decayed Glass orb still deals your Focus. Evoke = 2 x passive.
    /// </summary>
    public int GlassBase;
    public int GlassBonus;

    public SimOrb Clone() => (SimOrb)MemberwiseClone();
}

public sealed class SimEnemy
{
    public int Id;
    public string Name = "";
    public int Hp;
    public int Block;
    /// <summary>Can be targeted and hit (alive and hittable when the simulation starts).</summary>
    public bool Hittable = true;
    /// <summary>Hardened Shell: HP it can still lose this turn, and per turn (int.MaxValue = none).</summary>
    public int ShellLeft = int.MaxValue;
    public int ShellPerTurn = int.MaxValue;
    /// <summary>Damage of its poison tick at the start of its turn.</summary>
    public int Poison;
    public bool Barricade;
    public bool Stunned;
    /// <summary>Its attack this turn: damage of each hit on you (the intent's numbers).</summary>
    public List<int> Hits = new();
    public bool Imbalanced;
    /// <summary>Most damage one instance can deal it: Hard to Kill's cap, 1 while Intangible (int.MaxValue = none).</summary>
    public int HitCap = int.MaxValue;

    // ---- per-branch results
    /// <summary>Damage instances that got through its block during your turn (incl. end of turn).</summary>
    public int UnblockedInstances;
    /// <summary>Its first hit on you was fully blocked (Imbalanced).</summary>
    public bool FirstHitFullyBlocked;
    /// <summary>Dead before it could act (your turn, your end of turn, or its poison tick).</summary>
    public bool DiedBeforeActing;
    public bool Attacked;

    public bool Alive => Hp > 0;

    /// <summary>Copies the numbers; the attack list never changes during a run, so it's shared.</summary>
    public SimEnemy Clone() => (SimEnemy)MemberwiseClone();
}

public sealed class SimPlayer
{
    public int Block;

    // Powers that react or act at end of turn.
    public int Juggernaut;
    public int Plating;
    public int FeelNoPain;
    public int Hailstorm;
    public int HailstormFrostNeeded = 1;
    public int TheBombDamage; // fires this turn end (last tick), else 0
    public int ConsumingShadow;
    public int Thorns;
    public int FlameBarrier;
    public bool Reflect;

    // Relics.
    public int OrichalcumBlock;
    /// <summary>Set when end of turn starts: you had no block then (Orichalcum's condition).</summary>
    public bool OrichalcumArmed;
    public int CloakClaspPerCard;
    public int RippleBasinBlock;
    public int ScreamingFlagonDamage;
    public int StoneCalendarDamage; // its turn, else 0
    public int CharonsAshesDamage;
    public int ForgottenSoulDamage;
    public int ParryingShieldBlock;
    public int ParryingShieldDamage;

    // Hand and turn counters.
    public int HandCount;
    /// <summary>Ethereal cards in hand that exhaust at end of turn.</summary>
    public int Fading;
    public int AttacksThisTurn;
    /// <summary>End-of-turn damage from cards in hand (Burn, Decay...), blockable, in hand order.</summary>
    public List<(string Name, int Amount)> HandBlockable = new();
    /// <summary>End-of-turn HP loss from cards in hand (Bad Luck...).</summary>
    public List<(string Name, int Amount)> HandUnblockable = new();
    public int RegretCards;
    public List<(string Name, int Amount)> DebuffBlockable = new();
    /// <summary>Debuffs that land after Parrying Shield (AfterSideTurnEndLate: Disintegration).</summary>
    public List<(string Name, int Amount)> DebuffLateBlockable = new();
    public List<(string Name, int Amount)> DebuffUnblockable = new();

    // Orbs, front first.
    public List<SimOrb> Orbs = new();
    public int OrbCapacity;
    /// <summary>Extra passive triggers for whichever orb is in front when it fires (Gold-Plated Cables).</summary>
    public int FrontOrbBonusTriggers;

    // What happens to damage reaching your HP.
    public int TungstenRod;
    /// <summary>Beating Remnant: HP you can still lose until the start of your next turn (it resets only then).</summary>
    public int RemnantLeftPlayerTurn = int.MaxValue;
    public int RemnantCap = int.MaxValue;
    public int Buffer;
    public int OstyHp;

    public SimPlayer Clone()
    {
        // Orbs change (evokes, Glass and Dark passives); the hand and debuff lists don't, so they're shared.
        var c = (SimPlayer)MemberwiseClone();
        c.Orbs = new List<SimOrb>(Orbs.Count);
        foreach (var o in Orbs) c.Orbs.Add(o.Clone());
        return c;
    }
}

/// <summary>One branch of the simulation: the state plus what happened so far.</summary>
public sealed class SimState
{
    public SimPlayer P = new();
    public List<SimEnemy> Enemies = new();

    /// <summary>Keep the display logs (Incoming Damage's own run). Off for card simulations: faster.</summary>
    public bool Log;

    /// <summary>Random hits land on nobody: the safe fallback when there are too many branches.</summary>
    public bool NoRandomHits;

    // ---- results (per branch)
    public int Taken;
    public int OstyTaken;
    public int DamageThrough;
    public int SkippedPoisoned;
    public int SkippedEndOfTurn;
    public int CancelledHits;
    /// <summary>Block you have when the enemy turn starts.</summary>
    public int BlockAtEnemyTurn;
    public List<(string Name, int Amount)> EndOfTurnBlock = new();
    public List<(string Name, int Amount)> Blockable = new();
    public List<(string Name, int Amount)> Unblockable = new();
    public List<(string Name, int Amount)> EndOfTurnDamage = new();

    /// <summary>Running "blockable damage landed so far" for Imbalanced's full-block threshold.</summary>
    public int BlockDemand;
    public Dictionary<int, int> FullBlockNeeded = new();

    public SimEnemy? Enemy(int id) => Enemies.FirstOrDefault(e => e.Id == id);

    public IEnumerable<SimEnemy> Targetable => Enemies.Where(e => e.Hittable && e.Alive);

    public SimState Clone()
    {
        var c = (SimState)MemberwiseClone();
        c.P = P.Clone();
        c.Enemies = new List<SimEnemy>(Enemies.Count);
        foreach (var e in Enemies) c.Enemies.Add(e.Clone());
        if (Log)
        {
            c.EndOfTurnBlock = new(EndOfTurnBlock);
            c.Blockable = new(Blockable);
            c.Unblockable = new(Unblockable);
            c.EndOfTurnDamage = new(EndOfTurnDamage);
        }
        return c;
    }

    /// <summary>
    /// Identifies branches that can't diverge any more (same numbers everywhere that matters), so
    /// they're merged after each random event. Results that only differ in their logs merge too;
    /// the first one's logs are kept.
    /// </summary>
    public long Key()
    {
        // FNV-1a over every number that can differ between branches of one run (64-bit: collisions
        // among a few hundred branches are vanishingly unlikely).
        var h = unchecked((long)14695981039346656037UL);
        void Add(long v)
        {
            unchecked
            {
                h ^= v;
                h *= 1099511628211L;
            }
        }
        Add(P.Block); Add(Taken); Add(OstyTaken); Add(P.Buffer); Add(P.OstyHp); Add(P.RemnantLeftPlayerTurn); Add(P.OrichalcumArmed ? 1 : 0);
        Add(BlockDemand); Add(SkippedPoisoned); Add(SkippedEndOfTurn); Add(CancelledHits);
        foreach (var e in Enemies)
        {
            Add(e.Id); Add(e.Hp); Add(e.Block); Add(e.ShellLeft); Add(e.UnblockedInstances); Add(e.FirstHitFullyBlocked ? 1 : 0); Add(e.Poison); Add(e.DiedBeforeActing ? 1 : 0);
        }
        Add(P.Orbs.Count);
        foreach (var o in P.Orbs)
        {
            Add((int)o.Kind); Add(o.Passive); Add(o.Evoke);
        }
        return h;
    }
}
