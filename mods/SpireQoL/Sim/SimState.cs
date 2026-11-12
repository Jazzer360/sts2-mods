using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// <summary>
    /// Each hit's amount on teammate k (their own Vulnerable, Intangible, Tank...: the game's damage math per
    /// target); missing: the same as yours.
    /// </summary>
    public List<List<int>> TeammateHits = new();
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

    /// <summary>Poison stacks and how many times they tick (Accelerant): Poison above is their damage.</summary>
    public int PoisonStacks;
    public int PoisonTriggers = 1;

    // Reactions to your card plays (block it gains during your turn; decompile v0.111.0).
    /// <summary>Curl Up: block it gains after a card whose attack damaged it (once; the power goes).</summary>
    public int CurlUp;
    /// <summary>Sneaky: block it gains after each Attack you play.</summary>
    public int Sneaky;
    /// <summary>Skittish: block it gains after a card's attack gets damage through its block (once per turn; 0 once used).</summary>
    public int Skittish;
    // During one play: it was hit by the card's attack / damage got through.
    public bool HitByCard;
    public bool ThroughByCard;
    /// <summary>It gained block from Curl Up, Sneaky or Skittish because of the play.</summary>
    public bool ReactedWithBlock;

    // Being hit (decompile v0.111.0):
    /// <summary>
    /// Thorns: damage to you (blockable) before each hit of your card's attack on it
    /// (ThornsPower.BeforeDamageReceived on powered attacks), after the game's damage math (0 = none).
    /// </summary>
    public int Thorns;
    /// <summary>Burrowed: keeps its block between turns; breaking that block stuns it (AfterBlockBroken), and the power and its block go.</summary>
    public bool Burrowed;
    /// <summary>Flutter: powered-attack hits with damage through its block left before it's stunned (0 = none).</summary>
    public int Flutter;
    /// <summary>Plow / Shriek: stunned when damage gets through and leaves it at or below this HP (0 = none).</summary>
    public int StunAtHp;
    /// <summary>The Gambit: dies when a powered attack gets damage through.</summary>
    public bool Gambit;
    public bool GambitTriggered;
    /// <summary>Slippery: hits that lose it at most 1 HP each, left (0 = none).</summary>
    public int Slippery;
    public bool SlipperyTriggered;
    /// <summary>Alive when the simulation starts (deaths after that set off Crab Rage and Ravenous).</summary>
    public bool StartedAlive = true;
    /// <summary>Ravenous: an ally dying stuns it (RavenousPower.AfterDeath).</summary>
    public bool Ravenous;
    /// <summary>Crab Rage: its hits once an ally has died (its Strength; null = no Crab Rage).</summary>
    public List<int>? HitsAfterAllyDies;
    /// <summary>Crab Rage: block it gains when an ally dies.</summary>
    public int CrabRageBlock;

    /// <summary>Its block was broken (taken from above 0 to 0 by damage) during your turn.</summary>
    public bool BlockBroken;
    /// <summary>Stunned by something during your turn (not at the start).</summary>
    public bool StunnedThisTurn;

    /// <summary>
    /// Adds poison stacks and raises the tick damage by what they add: each tick deals the stacks left,
    /// for min(stacks, triggers) ticks (PoisonPower.CalculateTotalDamageNextTurn, without damage modifiers).
    /// </summary>
    public void AddPoison(int stacks)
    {
        if (stacks <= 0) return;
        int Tick(int a)
        {
            var sum = 0;
            for (var i = 0; i < Math.Min(a, Math.Max(1, PoisonTriggers)); i++) sum += a - i;
            return sum;
        }
        Poison += Tick(PoisonStacks + stacks) - Tick(PoisonStacks);
        PoisonStacks += stacks;
    }

    /// <summary>Its HP when your end of turn is over (before its turn), for the outcome audit.</summary>
    public int HpAtTurnEnd;
    public bool Attacked;

    [JsonIgnore]
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
    /// <summary>
    /// A character with no base orb slots: the first channel with no capacity adds a slot
    /// (OrbCmd.Channel → AddSlots 1). A Defect whose slots are all gone channels nothing.
    /// </summary>
    public bool SlotOnFirstChannel;
    /// <summary>Thunder: damage (unpowered) to each Lightning evoke's target that's still alive (ThunderPower.AfterOrbEvoked; 0 = none).</summary>
    public int Thunder;
    /// <summary>Extra passive triggers for whichever orb is in front when it fires (Gold-Plated Cables).</summary>
    public int FrontOrbBonusTriggers;

    // What happens to damage reaching your HP.
    public int TungstenRod;

    /// <summary>Block gains so far (each sets off Juggernaut): counted for the Juggernaut tab.</summary>
    public int BlockGains;

    /// <summary>Envenom: poison on each enemy your attack gets damage through to (per hit).</summary>
    public int Envenom;
    /// <summary>Snecko Skull: extra poison on every application of yours.</summary>
    public int PoisonBonus;
    /// <summary>The Boot: your attack hits that lose an enemy any HP lose it at least this much (0 = no relic).</summary>
    public int BootMinimum;
    /// <summary>Your Focus now (orb values already include it), for Focus changes during a play.</summary>
    public int Focus;
    /// <summary>Beating Remnant: HP you can still lose until the start of your next turn (it resets only then).</summary>
    public int RemnantLeftPlayerTurn = int.MaxValue;
    public int RemnantCap = int.MaxValue;
    public int Buffer;
    public int OstyHp;

    // Teammates only (yours are on SimState: Taken, OstyTaken, DamageThrough).
    /// <summary>A teammate's HP (hits on a teammate at 0 stop).</summary>
    public int Hp = int.MaxValue;
    /// <summary>A teammate's HP lost, through their block, and lost by their Osty this turn.</summary>
    public int Taken, Through, OstyTaken;
    /// <summary>A teammate's damage landing on them (before block) and block they gained: their Incoming Damage.</summary>
    public int Incoming, BlockGained;
    /// <summary>
    /// Beacon of Hope: half of each block this player gains on the players' side goes to every other
    /// living player (BeaconOfHopePower.AfterBlockGained). BeaconGiving: held while it gives (the game's
    /// guard against two Beacons trading block forever).
    /// </summary>
    public bool BeaconOfHope, BeaconGiving;

    // Branches share their orbs until one changes them (OwnOrbs): copying them for every candidate
    // branch, most of which merge away as duplicates, was most of a branch-heavy simulation's cost.
    private bool _orbsShared;

    /// <summary>The orbs, to change (evoke, channel, Glass and Dark passives, Focus): copied for this branch first if shared.</summary>
    public List<SimOrb> OwnOrbs()
    {
        if (_orbsShared)
        {
            var own = new List<SimOrb>(Orbs.Count);
            foreach (var o in Orbs) own.Add(o.Clone());
            Orbs = own;
            _orbsShared = false;
        }
        return Orbs;
    }

    public SimPlayer Clone()
    {
        // The hand and debuff lists never change, so they're shared; the orbs until one branch changes them.
        var c = (SimPlayer)MemberwiseClone();
        // Both now point at one list: whichever changes it first copies it (an empty one too: a channel adds to it).
        _orbsShared = true;
        c._orbsShared = true;
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

    /// <summary>
    /// Branches ran out: random hits and poison land on the first possible target in place, without
    /// branching (this run only keeps "can happen" results; damage to you comes from the safe run).
    /// </summary>
    public bool FirstTargetOnly;

    /// <summary>A step ran out of branches inside itself (SimEngine.Then): the run is out of branches.</summary>
    internal bool OutOfBranches;

    // ---- results (per branch)
    public int Taken;
    public int OstyTaken;
    public int DamageThrough;
    public int SkippedPoisoned;
    public int SkippedEndOfTurn;
    public int CancelledHits;
    /// <summary>Block you have when the enemy turn starts.</summary>
    public int BlockAtEnemyTurn;
    /// <summary>Your Parrying Shield fired at the end of your turn (your block at least its threshold).</summary>
    public bool ParryingShieldFired;
    public List<(string Name, int Amount)> EndOfTurnBlock = new();
    public List<(string Name, int Amount)> Blockable = new();
    public List<(string Name, int Amount)> Unblockable = new();
    public List<(string Name, int Amount)> EndOfTurnDamage = new();

    /// <summary>Running "blockable damage landed so far" for Imbalanced's full-block threshold.</summary>
    public int BlockDemand;
    public Dictionary<int, int> FullBlockNeeded = new();

    public SimEnemy? Enemy(int id) => Enemies.FirstOrDefault(e => e.Id == id);

    [JsonIgnore]
    public IEnumerable<SimEnemy> Targetable => Enemies.Where(e => e.Hittable && e.Alive);

    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true, WriteIndented = false };

    /// <summary>
    /// The state as JSON, for reports: a report's start state can be dropped into
    /// tests/SpireQoL.Sim.Tests/Cases to replay it offline (see docs/DEVELOPMENT.md).
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static SimState FromJson(string json) => JsonSerializer.Deserialize<SimState>(json, JsonOptions)!;

    /// <summary>
    /// Fingerprint of everything in the state (every field, via its JSON), for caching simulations
    /// between frames. Unlike Key (branch merging), it changes when anything the simulation reads
    /// changes: intents, powers, relic values, hand cards.
    /// </summary>
    public long FullKey()
    {
        var json = ToJson();
        var h = unchecked((long)14695981039346656037UL);
        foreach (var ch in json)
        {
            unchecked
            {
                h ^= ch;
                h *= 1099511628211L;
            }
        }
        return h;
    }

    /// <summary>
    /// This branch's share of the rolls (1 at the start): each random target is equally likely, so a
    /// split divides it evenly, and identical branches merged together add theirs. For chances.
    /// </summary>
    public double Weight = 1;

    /// <summary>
    /// Other players in multiplayer, from what's on screen (orbs, powers, relics): only their effects on
    /// enemies at end of turn count; their block protects them, and their hand isn't known.
    /// </summary>
    public List<SimPlayer> Teammates = new();

    /// <summary>How many teammates come before you in player order (their orbs fire before yours).</summary>
    public int TeammatesBeforeMe;

    // Branches share their teammates until one changes them (Glass and Dark passives, Consuming
    // Shadow): copying them on every branch made multiplayer fights slow.
    private bool _teammatesShared;

    /// <summary>
    /// After a lead-in (plays still resolving or queued, decision #27): what this branch looked like when the
    /// card itself starts, so its own kills, block breaks and block gains are measured from there.
    /// </summary>
    internal PlayBaseline? PlayBase;

    /// <summary>The run's work counter (SimEngine.Run sets it; every copy of the state shares it).</summary>
    internal SimWork? Work;

    internal sealed record PlayBaseline(HashSet<int> Alive, HashSet<int> Broken, int BlockGains);

    /// <summary>Teammate k, to change: copied for this branch first if it's shared.</summary>
    public SimPlayer MutableTeammate(int k)
    {
        if (_teammatesShared)
        {
            Teammates = Teammates.Select(t => t.Clone()).ToList();
            _teammatesShared = false;
        }
        return Teammates[k];
    }

    /// <summary>
    /// A copy without the cards in teammates' hands (decision #25: what's shown uses only what you can see).
    /// Their hand size stays (it's on screen: Cloak Clasp, Screaming Flagon, Regret's amount); the cards
    /// that act from their hand at end of turn (Burn, Decay, Regret) are left out.
    /// </summary>
    public SimState WithoutTeammateHands()
    {
        var shown = Clone();
        for (var k = 0; k < shown.Teammates.Count; k++)
        {
            var t = shown.MutableTeammate(k);
            t.HandBlockable = new();
            t.HandUnblockable = new();
            t.RegretCards = 0;
        }
        return shown;
    }

    public SimState Clone()
    {
        if (Work != null) Work.Copies++;
        var c = (SimState)MemberwiseClone();
        c.P = P.Clone();
        _teammatesShared = true;
        c._teammatesShared = true;
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
        Add(P.Block); Add(Taken); Add(OstyTaken); Add(P.Buffer); Add(P.OstyHp); Add(P.RemnantLeftPlayerTurn); Add(P.OrichalcumArmed ? 1 : 0); Add(ParryingShieldFired ? 1 : 0);
        Add(BlockDemand); Add(SkippedPoisoned); Add(SkippedEndOfTurn); Add(CancelledHits);
        foreach (var e in Enemies)
        {
            Add(e.Id); Add(e.Hp); Add(e.Block); Add(e.ShellLeft); Add(e.UnblockedInstances); Add(e.FirstHitFullyBlocked ? 1 : 0); Add(e.Poison); Add(e.DiedBeforeActing ? 1 : 0);
            Add(e.PoisonStacks); Add(e.CurlUp); Add(e.Skittish); Add(e.HitByCard ? 1 : 0); Add(e.ThroughByCard ? 1 : 0);
            Add(e.Stunned ? 1 : 0); Add(e.Burrowed ? 1 : 0); Add(e.Flutter); Add(e.StunAtHp); Add(e.Gambit ? 1 : 0); Add(e.Slippery);
            Add(e.ReactedWithBlock ? 1 : 0); Add(e.BlockBroken ? 1 : 0);
            Add(e.HitsAfterAllyDies == null ? 0 : 1); Add(e.Hits.Count); foreach (var hit in e.Hits) Add(hit); Add(e.GambitTriggered ? 1 : 0); Add(e.SlipperyTriggered ? 1 : 0);
        }
        Add(P.Orbs.Count);
        foreach (var o in P.Orbs)
        {
            Add((int)o.Kind); Add(o.Passive); Add(o.Evoke);
        }
        return h;
    }
}

/// <summary>
/// What one simulation costs, the same on every machine (the performance tests limit these, not time).
/// One run is one thread: plain fields, never shared between runs.
/// </summary>
internal sealed class SimWork
{
    /// <summary>Steps applied to the branch set (each hit, orb, block gain, the enemy turn).</summary>
    public int Steps;
    /// <summary>Branches stepped, summed over steps.</summary>
    public int Stepped;
    /// <summary>State copies (most of the cost: every random outcome is a copy).</summary>
    public int Copies;
    /// <summary>The most copies one step made.</summary>
    public int MaxStepCopies;
    private int _stepStart;

    /// <summary>A new step starts (BranchSet).</summary>
    public void StartStep()
    {
        EndStep();
        _stepStart = Copies;
    }

    public void EndStep() => MaxStepCopies = Math.Max(MaxStepCopies, Copies - _stepStart);

    /// <summary>This step has made SimEngine.StepCopyBudget copies: random choices stop branching.</summary>
    public bool OverBudget => Copies - _stepStart >= SimEngine.StepCopyBudget;
}
