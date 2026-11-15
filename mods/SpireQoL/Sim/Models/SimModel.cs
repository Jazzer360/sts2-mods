namespace SpireQoL.Sim;

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
    /// <summary>Its cost when played (X costs: the energy spent), and its energy value (CardPlay.Resources.EnergyValue).</summary>
    public int Energy { get; init; }
    public bool CostsX { get; init; }
    /// <summary>Which card in hand it is (SimHandCard.Id), 0 = none.</summary>
    public int HandId { get; init; }

    /// <summary>Its dynamic values a model reads (Heavy Shot's AimMultiplier...), by the game's var name.</summary>
    public IReadOnlyDictionary<string, decimal> Vars { get; init; } = new Dictionary<string, decimal>();

    public bool Is(string tag) => Tags.Contains(tag);
}

/// <summary>One card play as the hooks see it (the game's CardPlay): the card, its target, and which play of how many.</summary>
public sealed record SimCardPlay(SimCard Card, int Owner, int Target, int PlayIndex, int PlayCount, bool AutoPlay);

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

    protected static IEnumerable<SimState> One(SimState s) => new[] { s };

    // ---- pipelines (pure: read the state, change nothing)

    public virtual decimal ModifyDamageAdditive(SimState s, int owner, DamageCtx d) => 0m;
    public virtual decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d) => 1m;
    public virtual decimal ModifyDamageMultiplicativeLate(SimState s, int owner, DamageCtx d) => 1m;
    public virtual decimal ModifyDamageCap(SimState s, int owner, DamageCtx d) => decimal.MaxValue;
    public virtual decimal ModifyBlockAdditive(SimState s, int owner, BlockCtx b) => 0m;
    public virtual decimal ModifyBlockMultiplicative(SimState s, int owner, BlockCtx b) => 1m;
    /// <summary>Hook.ModifyPowerAmountGiven: a power's amount as <paramref name="applier"/> gives it to <paramref name="target"/> (Snecko Skull).</summary>
    public virtual int ModifyPowerAmountGiven(SimState s, int owner, string power, int amount, int? applier, int target) => amount;

    // ---- hooks (may branch)

    public virtual IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play) => One(s);
    public virtual IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) => One(s);
    public virtual IEnumerable<SimState> BeforeDamageReceived(SimState s, int owner, DamageCtx d, int amount) => One(s);
    public virtual IEnumerable<SimState> AfterDamageGiven(SimState s, int owner, DamageEvent e) => One(s);
    public virtual IEnumerable<SimState> AfterDamageReceived(SimState s, int owner, DamageEvent e) => One(s);
    public virtual IEnumerable<SimState> BeforeAttack(SimState s, int owner, AttackInfo attack) => One(s);
    public virtual IEnumerable<SimState> AfterAttack(SimState s, int owner, AttackInfo attack) => One(s);
    public virtual IEnumerable<SimState> AfterBlockGained(SimState s, int owner, int gainer, int amount, SimCard? card) => One(s);
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
