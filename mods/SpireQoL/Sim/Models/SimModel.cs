namespace SpireQoL.Sim;

/// <summary>What a model's tab shows: a plain trigger, or the block, damage or summon it adds (docs/FEATURES.md "On-card tabs").</summary>
public enum TabKind { Trigger, Block, Damage, Summon }

/// <summary>
/// A card event the hooks see (AfterCardDrawn, AfterCardExhausted, AfterCardDiscarded, AfterCardGeneratedForCombat, AfterStarsGained,
/// AfterShuffle), one per card (or star).
/// </summary>
public enum CardEvent { Drawn, Exhausted, Discarded, Generated, GeneratedStatus, GeneratedSadness, StarsGained, StarsSpent, Shuffled, EnergySpent, EnergyGained, ArrowDislodged }

/// <summary>The game's ValueProp flags (MegaCrit.Sts2.Core.ValueProps), same values.</summary>
[Flags]
public enum SimProps
{
    None = 0,
    Unblockable = 2,
    Unpowered = 4,
    Move = 8,
    SkipHurtAnim = 0x10,
    FromArrow = 0x20,
}

public static class SimPropsExtensions
{
    /// <summary>ValuePropExtensions.IsPoweredAttack: a Move that isn't Unpowered.</summary>
    public static bool IsPoweredAttack(this SimProps p) => p.HasFlag(SimProps.Move) && !p.HasFlag(SimProps.Unpowered);
    /// <summary>ValuePropExtensions.IsPoweredCardOrMonsterMoveBlock (the same test, for block).</summary>
    public static bool IsPoweredBlock(this SimProps p) => p.HasFlag(SimProps.Move) && !p.HasFlag(SimProps.Unpowered);
    public static bool IsCardOrMonsterMove(this SimProps p) => p.HasFlag(SimProps.Move);
}

public enum SimCardType { None, Attack, Skill, Power, Status, Curse }

/// <summary>
/// The card a play, hit or block comes from, as models need it: its class name, type, tags (Strike, Defend, Shiv, Minion,
/// Shoot, Wah, Cho, Ki...), and a few flags. Immutable; shared by every branch.
/// </summary>
public sealed record SimCard(string Name, SimCardType Type)
{
    public HashSet<string> Tags { get; init; } = new();
    public bool Upgraded { get; init; }
    public bool Enchanted { get; init; }
    public bool Ethereal { get; init; }
    /// <summary>It gains block (CardModel.GainsBlock, or its enchantment's): Stretch binds to such a play.</summary>
    public bool GainsBlock { get; init; }
    /// <summary>Its cost when played (X costs: the energy spent), and its energy value (CardPlay.Resources.EnergyValue).</summary>
    public int Energy { get; init; }
    public bool CostsX { get; init; }
    /// <summary>Stars it spends when played (CardPlay.Resources.StarsSpent).</summary>
    public int Stars { get; init; }
    /// <summary>Its star cost is X (Chemical X boosts it too).</summary>
    public bool StarCostX { get; init; }
    /// <summary>It exhausts itself once played (the Exhaust keyword, added ones included).</summary>
    public bool ExhaustsSelf { get; init; }
    /// <summary>A copy made by a replay (CardModel.IsDupe): Feral doesn't return it.</summary>
    public bool Dupe { get; init; }
    /// <summary>It can still be upgraded (Razor Tooth).</summary>
    public bool Upgradable { get; init; }
    /// <summary>Another card in your hand still costs energy or stars (Mummified Hand has something to make free).</summary>
    public bool OthersInHandCost { get; init; }
    /// <summary>Which card in hand it is (SimHandCard.Id), 0 = none.</summary>
    public int HandId { get; init; }

    /// <summary>Its dynamic values a model reads (Heavy Shot's AimMultiplier...), by the game's var name.</summary>
    public IReadOnlyDictionary<string, decimal> Vars { get; init; } = new Dictionary<string, decimal>();

    public bool Is(string tag) => Tags.Contains(tag);
}

/// <summary>One card play as the hooks see it (the game's CardPlay): the card, its target, and which play of how many.</summary>
/// <summary>How the game shows a model acted (SimModel.Seen).</summary>
public enum ActedSeen { Flash, StatusCleared, NoEnergyLeft, CounterPassedLimit }

public sealed record SimCardPlay(SimCard Card, int Owner, int Target, int PlayIndex, int PlayCount, bool AutoPlay)
{
    /// <summary>
    /// Epic Retrieval (AfterArrowDislodged, source this card): energy for each arrow of its player's knocked loose during the play
    /// (decision #117).
    /// </summary>
    public int ArrowEnergy { get; init; }
}

/// <summary>A damage calculation's inputs (Hook.ModifyDamage): who deals it to whom, its props, and the card it's from.</summary>
public sealed record DamageCtx(int? Dealer, int Target, SimProps Props, SimCard? Card, SimCardPlay? Play);

/// <summary>A block calculation's inputs (Hook.ModifyBlock): who gains it, its props, and the card it's from.</summary>
public sealed record BlockCtx(int Target, SimProps Props, SimCard? Card, SimCardPlay? Play);

/// <summary>An attack (AttackCommand) for BeforeAttack and AfterAttack: its id in this simulation, who makes it, its props and card.</summary>
public sealed record AttackInfo(int Id, int Dealer, SimProps Props, SimCard? Card);

/// <summary>A damage event (DamageResult) for the hooks around it.</summary>
public sealed record DamageEvent(int? Dealer, int Target, SimProps Props, SimCard? Card, int Total, int Blocked, int Unblocked, bool Killed);

/// <summary>
/// Creature ids (one number for any creature, as hooks need an owner and a target): enemies are their SimEnemy.Id (0 and up),
/// you are -1, teammate k is -2 - k (SimAdapter.AllyKey).
/// </summary>
public static class Who
{
    public const int You = -1;
    public static int Teammate(int k) => -2 - k;
    public static bool IsEnemy(int id) => id >= 0;
    public static bool IsPlayer(int id) => id < 0 && id > YourOsty;
    /// <summary>Your Osty, as a dealer (his own powers aren't simulated: his damage is captured).</summary>
    public const int YourOsty = -100;
    /// <summary>The teammate index of a player id (-1 for you).</summary>
    public static int TeammateIndex(int id) => id <= -2 ? -2 - id : -1;
}

/// <summary>
/// A relic, power or enemy effect the simulator models (docs/SIMULATOR_V2.md): the game hooks it overrides, written from its
/// decompiled body. Models hold no state of their own: their numbers live in the state (the creature's PowerSet, a player's
/// relic counters), so a branch copies only numbers. A hook returns the branches after it (one, unless it's random) and
/// never changes anything but the state it's given (or its copies).
/// </summary>
public abstract class SimModel
{
    /// <summary>The game's class name (StrengthPower, PenNib...).</summary>
    public abstract string Name { get; }

    /// <summary>It flashes when it changes a damage amount (the game's AfterModifyingDamageAmount: Slow, Hard to Kill...).</summary>
    public virtual bool FlashesOnDamageModified => false;

    /// <summary>
    /// Its tab on a card whose play makes it fire (from the play log; the amount from what it logged): a trigger, or the block,
    /// damage or summon it adds. Null: no tab from the log (its tab, if any, comes from the outcome comparisons).
    /// </summary>
    public virtual TabKind? Tab => null;
    /// <summary>Its tab is crossed (a warning: it helps the enemy, hurts you, or gives up a bonus), shown when it fires in some branch.</summary>
    public virtual bool Harmful => false;
    /// <summary>
    /// How the game shows that it acted, for the play check (decision #117): a flash (the default), or, for a relic whose crossed tab
    /// means the play gives up its bonus, the relic going dark (its Active status cleared: Art of War, Ripple Basin, Pael's Eye) or
    /// no energy left after the play (Pael's Tears, which judges the energy as your turn ends), or its counter going past its limit
    /// (Pocketwatch: <see cref="LimitVar"/>).
    /// </summary>
    public virtual ActedSeen Seen => ActedSeen.Flash;
    /// <summary>For <see cref="ActedSeen.CounterPassedLimit"/>: the relic's value (DynamicVars) its counter must stay within.</summary>
    public virtual string? LimitVar => null;

    protected static IEnumerable<SimState> One(SimState s) => new[] { s };

    private readonly HashSet<string> _overrides;

    protected SimModel()
    {
        // The hooks this model overrides (here or in a base class of its own): the engine calls only those.
        _overrides = GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.IsVirtual && m.DeclaringType != typeof(SimModel) && m.GetBaseDefinition().DeclaringType == typeof(SimModel))
            .Select(m => m.Name).ToHashSet();
        ListensToDamage = DamageHooks.Any(_overrides.Contains);
    }

    private static readonly string[] DamageHooks =
    {
        nameof(ModifyDamageCap), nameof(BeforeDamageReceived), nameof(AfterBlockBroken), nameof(ModifyHpLostBeforeOsty),
        nameof(ModifyHpLostBeforeOstyLate), nameof(ModifyHpLostAfterOsty), nameof(ModifyHpLostAfterOstyLate),
        nameof(AfterModifyingHpLost), nameof(AfterDamageGiven), nameof(AfterDamageReceived),
    };

    /// <summary>It overrides this hook (by name): the engine skips models that don't listen to a hook.</summary>
    public bool Overrides(string hook) => _overrides.Contains(hook);

    /// <summary>It acts inside damage (SimEngine.Damage): without such a model, damage takes the plain path.</summary>
    public bool ListensToDamage { get; }

    // ---- pipelines (pure: read the state, change nothing)

    public virtual decimal ModifyDamageAdditive(SimState s, int owner, DamageCtx d) => 0m;
    public virtual decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d) => 1m;
    public virtual decimal ModifyDamageMultiplicativeLate(SimState s, int owner, DamageCtx d) => 1m;
    public virtual decimal ModifyDamageCap(SimState s, int owner, DamageCtx d) => decimal.MaxValue;
    public virtual decimal ModifyBlockAdditive(SimState s, int owner, BlockCtx b) => 0m;
    public virtual decimal ModifyBlockMultiplicative(SimState s, int owner, BlockCtx b) => 1m;
    /// <summary>Hook.ModifyPowerAmountGiven: a power's amount as <paramref name="applier"/> gives it to <paramref name="target"/> (Snecko Skull).</summary>
    public virtual int ModifyPowerAmountGiven(SimState s, int owner, string power, int amount, int? applier, int target, bool debuff) => amount;

    // ---- hooks (may branch)

    public virtual IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play) => One(s);
    public virtual IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) => One(s);
    /// <summary>
    /// Not a game hook: once a play has fully resolved (every AfterCardPlayed, whatever their order, and what follows the play), for a
    /// tab judged on what the play leaves rather than on a moment inside it (Pael's Tears: the energy left). <paramref name="player"/>:
    /// who played it.
    /// </summary>
    public virtual IEnumerable<SimState> AfterPlayResolved(SimState s, int owner, int player) => One(s);
    // ---- damage-time hooks: deterministic, run inside the damage itself (SimEngine.Damage), so every source of damage
    // (hits, procs, orbs, end of turn) gets them

    public virtual void BeforeDamageReceived(SimState s, int owner, DamageCtx d, int amount) { }
    public virtual void AfterDamageGiven(SimState s, int owner, DamageEvent e) { }
    public virtual void AfterDamageReceived(SimState s, int owner, DamageEvent e) { }
    /// <summary>A creature's block went from above 0 to 0 (AfterBlockBroken: Burrowed).</summary>
    public virtual void AfterBlockBroken(SimState s, int owner, int target) { }
    /// <summary>The HP-loss pipeline (Hook.ModifyHpLost*): BeforeOsty, BeforeOstyLate, AfterOsty, AfterOstyLate.</summary>
    public virtual decimal ModifyHpLostBeforeOsty(SimState s, int owner, DamageCtx d, decimal amount) => amount;
    public virtual decimal ModifyHpLostBeforeOstyLate(SimState s, int owner, DamageCtx d, decimal amount) => amount;
    public virtual decimal ModifyHpLostAfterOsty(SimState s, int owner, DamageCtx d, decimal amount) => amount;
    public virtual decimal ModifyHpLostAfterOstyLate(SimState s, int owner, DamageCtx d, decimal amount) => amount;
    /// <summary>It changed an HP loss (the game's AfterModifyingHpLost*): most flash.</summary>
    public virtual void AfterModifyingHpLost(SimState s, int owner) => s.LogFired(Name, 0, owner);
    public virtual IEnumerable<SimState> BeforeAttack(SimState s, int owner, AttackInfo attack) => One(s);
    public virtual IEnumerable<SimState> AfterAttack(SimState s, int owner, AttackInfo attack) => One(s);
    public virtual IEnumerable<SimState> AfterBlockGained(SimState s, int owner, int gainer, int amount, SimCard? card) => One(s);
    /// <summary>After a block amount it changed was gained (the game's AfterModifyingBlockAmount): Pael's Legion, Vambrace, Stretch.</summary>
    /// <summary>A card event (one per card or star): draws, exhausts, discards, created cards, stars, shuffles. Player: who it happened to.</summary>
    public virtual IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) => One(s);
    // ---- end of your side's turn (Hook.BeforeSideTurnEnd: every listener's VeryEarly, then Early, then BeforeSideTurnEnd;
    // Hook.AfterSideTurnEnd: AfterSideTurnEnd, then Late). Only for players' models whose owner takes part (SimEngine.TakesPart).

    public virtual IEnumerable<SimState> BeforeSideTurnEndVeryEarly(SimState s, int owner) => One(s);
    public virtual IEnumerable<SimState> BeforeSideTurnEndEarly(SimState s, int owner) => One(s);
    public virtual IEnumerable<SimState> BeforeSideTurnEnd(SimState s, int owner) => One(s);
    public virtual IEnumerable<SimState> AfterSideTurnEnd(SimState s, int owner) => One(s);
    public virtual IEnumerable<SimState> AfterSideTurnEndLate(SimState s, int owner) => One(s);
    /// <summary>A player's Osty lost HP (AfterCurrentHpChanged with a loss on Osty): Necro Mastery. Runs inside the damage.</summary>
    public virtual void AfterOstyHpLost(SimState s, int owner, int player, int lost) { }
    /// <summary>A player tried a shot (AfterArrowShot: with or without an arrow): Covering Fire.</summary>
    public virtual IEnumerable<SimState> AfterArrowShot(SimState s, int owner, int shooter) => One(s);
    /// <summary>A shooter's arrow impaled an enemy (AfterArrowImpaled): Explosive Arrows.</summary>
    public virtual IEnumerable<SimState> AfterArrowImpaled(SimState s, int owner, int target, int shooter) => One(s);
    /// <summary>An orb was channeled for a player (AfterOrbChanneled): Metronome.</summary>
    public virtual IEnumerable<SimState> AfterOrbChanneled(SimState s, int owner, int player) => One(s);
    /// <summary>A power landed on a creature (after Artifact): Sleight of Flesh, Vicious, Shroud.</summary>
    public virtual IEnumerable<SimState> AfterPowerApplied(SimState s, int owner, string power, int amount, int applier, int target, bool debuff) => One(s);
    /// <summary>A creature died (AfterDeath; once per death): Crab Rage, Ravenous.</summary>
    public virtual void AfterDeath(SimState s, int owner, int dead) { }
    /// <summary>After this power was applied to its owner (the game's AfterApplied): Covered sets up Intercept on its applier.</summary>
    public virtual void AfterApplied(SimState s, int owner, int applier, int amount) { }
    public virtual void AfterModifyingBlockAmount(SimState s, int owner, int amount, SimCardPlay? play) { }
    /// <summary>After a damage amount it changed (the game's AfterModifyingDamageAmount): most only flash (FlashesOnDamageModified).</summary>
    public virtual void AfterModifyingDamageAmount(SimState s, int owner, SimCard? card) { if (FlashesOnDamageModified) s.LogFired(Name, 0, owner); }
}

/// <summary>The models the simulator knows, by the game's class name. A name not here isn't modelled.</summary>
public static class SimModels
{
    private static readonly Dictionary<string, SimModel> ByName = new();

    static SimModels()
    {
        foreach (var t in typeof(SimModel).Assembly.GetTypes())
        {
            if (t.IsAbstract || !typeof(SimModel).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
            var m = (SimModel)Activator.CreateInstance(t)!;
            ByName[m.Name] = m;
        }
    }

    public static SimModel? Get(string name) => ByName.GetValueOrDefault(name);

    public static bool Has(string name) => ByName.ContainsKey(name);

    public static IEnumerable<string> Names => ByName.Keys;
}
