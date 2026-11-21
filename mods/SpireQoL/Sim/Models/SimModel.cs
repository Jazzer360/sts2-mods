using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>
/// A model's tab: a trigger (no number), or one with the amount it gives (block, damage, summon, the cards it draws, the energy it
/// gives: decision #132).
/// </summary>
public enum TabKind { Trigger, Block, Damage, Summon, Draw, Energy }

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
    public static bool IsPoweredAttack(this SimProps simProps) => simProps.HasFlag(SimProps.Move) && !simProps.HasFlag(SimProps.Unpowered);
    /// <summary>ValuePropExtensions.IsPoweredCardOrMonsterMoveBlock (the same test, for block).</summary>
    public static bool IsPoweredBlock(this SimProps simProps) => simProps.HasFlag(SimProps.Move) && !simProps.HasFlag(SimProps.Unpowered);
    public static bool IsCardOrMonsterMove(this SimProps simProps) => simProps.HasFlag(SimProps.Move);
}

public enum SimCardType { None, Attack, Skill, Power, Status, Curse }

/// <summary>
/// A card's tags (the game's CardTag), as models read them. The adapter maps them by name: the game's enum differs between its
/// branches (main has no Wah, Cho, Ki or Shoot), and a game enum compiles into the mod as a number.
/// </summary>
public enum SimCardTag { Strike, Defend, Minion, OstyAttack, Shiv, Wah, Cho, Ki, Shoot }

/// <summary>The affliction on a card (the game's AfflictionModel classes, by name; Other for one this list doesn't know yet).</summary>
public enum SimAffliction { None, Bound, Entangled, Galvanized, Hexed, Ringing, Smog, Tainted, Other }

/// <summary>
/// The card a play, hit or block comes from, as models need it: its class name, type, tags, affliction, and a few flags. Immutable;
/// shared by every branch.
/// </summary>
public sealed record SimCard(string Name, SimCardType Type)
{
    public HashSet<SimCardTag> Tags { get; init; } = new();
    public SimAffliction Affliction { get; init; }
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

    public bool Is(SimCardTag tag) => Tags.Contains(tag);
}

/// <summary>How the game shows a model acted (SimModel.Seen).</summary>
public enum ActedSeen { Flash, StatusCleared, NoEnergyLeft, CounterPassedLimit }

/// <summary>A tab judged on how the turn ends (SimModel.OutcomeTab): crossed or plain, and whether one play's outcome sets it off.</summary>
public sealed record OutcomeRule(bool Harmful, Func<SimOutcome, bool> SetOff);

/// <summary>One card play as the hooks see it (the game's CardPlay): the card, its target, and which play of how many.</summary>
public sealed record SimCardPlay(SimCard Card, int Owner, [property: JsonConverter(typeof(CreatureIdJson))] int Target, int PlayIndex, int PlayCount, bool AutoPlay)
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

/// <summary>What a creature id names (Who.KindOf); None: no creature (Who.None).</summary>
public enum CreatureKind { You, Teammate, YourOsty, Enemy, None }

/// <summary>
/// Creature ids: one number for any creature, the only one the simulator uses (hooks need an owner, a target, a dealer). Enemies
/// are their SimEnemy.Id (0 and up), you are -1, teammate k (SimState.Teammates[k]) is -2 - k, your Osty as a dealer -100.
/// Look a player up by id with SimState.Player.
/// </summary>
public static class Who
{
    public const int You = -1;
    /// <summary>
    /// No one: a play aimed at no creature (a card with no target, or an enemy-aimed one with no enemy to aim at). A card is never
    /// aimed at you, and this is no creature's id, so "aimed at no one" can't be read as you.
    /// </summary>
    public const int None = int.MinValue;
    /// <summary>Your Osty, as a dealer (his own powers aren't simulated: his damage is captured).</summary>
    public const int YourOsty = -100;
    public static int Teammate(int teammateIndex) => -2 - teammateIndex;
    public static CreatureKind KindOf(int id) => id >= 0 ? CreatureKind.Enemy : id == You ? CreatureKind.You : id == YourOsty ? CreatureKind.YourOsty
        : id == None ? CreatureKind.None : CreatureKind.Teammate;
    public static bool IsEnemy(int id) => id >= 0;
    public static bool IsPlayer(int id) => KindOf(id) is CreatureKind.You or CreatureKind.Teammate;
    /// <summary>The teammate index of a player id (SimState.Teammates[k]); -1 for anything else.</summary>
    public static int TeammateIndex(int id) => KindOf(id) == CreatureKind.Teammate ? -2 - id : -1;

    /// <summary>
    /// Every player's id in player order, with you at your place among your teammates (the order the game runs players in: hook
    /// listeners, turn ends, hits on every player). Made once per team shape, so reading it allocates nothing.
    /// </summary>
    public static int[] InPlayerOrder(int teammates, int teammatesBeforeYou)
    {
        if (teammates >= Orders.Length) return Order(teammates, teammatesBeforeYou);
        var before = Math.Clamp(teammatesBeforeYou, 0, teammates);
        return Orders[teammates][before];
    }

    private static readonly int[][][] Orders = Enumerable.Range(0, 4)
        .Select(teammates => Enumerable.Range(0, teammates + 1).Select(before => Order(teammates, before)).ToArray()).ToArray();

    private static int[] Order(int teammates, int before)
    {
        before = Math.Clamp(before, 0, teammates);
        var order = new int[teammates + 1];
        for (var teammateIndex = 0; teammateIndex < before; teammateIndex++) order[teammateIndex] = Teammate(teammateIndex);
        order[before] = You;
        for (var teammateIndex = before; teammateIndex < teammates; teammateIndex++) order[teammateIndex + 1] = Teammate(teammateIndex);
        return order;
    }
}

/// <summary>
/// A relic, power or enemy effect the simulator models (docs/SIMULATOR.md): the game hooks it overrides, written from its
/// decompiled body. Models hold no state of their own: their numbers live in the state (the creature's PowerSet, a player's
/// relic counters), so a branch copies only numbers. A hook returns the branches after it (one, unless it's random) and
/// never changes anything but the state it's given (or its copies).
/// </summary>
public abstract class SimModel
{
    /// <summary>
    /// The game's class name (StrengthPower, PenNib...): a model is named after the class it models, so this is its own class name
    /// unless it says otherwise (the one entry two models share, or a name the game doesn't have).
    /// </summary>
    public virtual string Name => _name;

    private readonly string _name;

    /// <summary>Its own entry on a creature, when it's a power (its PowerSet entry: amount and data).</summary>
    protected PowerId ThisPower => new(Name);

    /// <summary>Its own entry on a player, when it's a relic (its RelicSet entry: counter and flag).</summary>
    protected RelicId ThisRelic => new(Name);

    /// <summary>It flashes when it changes a damage amount (the game's AfterModifyingDamageAmount: Slow, Hard to Kill...).</summary>
    public virtual bool FlashesOnDamageModified => false;

    /// <summary>
    /// Its tab on a card whose play makes it fire (from the play log; the amount from what it logged): a trigger, or the block,
    /// damage or summon it adds. Null: no tab from the log (its tab, if any, comes from the outcome comparisons).
    /// </summary>
    public virtual TabKind? Tab => null;
    /// <summary>
    /// A tab judged on how the turn ends instead of on what fires during the play: given the turn ended now (<paramref name="now"/>,
    /// from <paramref name="start"/>), the rule one play's outcome must pass for the tab to show, or null when there's none for this
    /// source here (it happens anyway, or never can). The tab shows when some aim passes it, with the aim marker when not every aim
    /// does; a bonus needs every branch of an aim (InEvery), a warning one branch (InSome). Rules shared by several sources are in
    /// Models/OutcomeModels.cs (OutcomeRules).
    /// </summary>
    /// <param name="owner">Whose source it is: an enemy's id for its powers, a player's for theirs and their relics.</param>
    public virtual OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => null;

    /// <summary>It has a tab judged on how the turn ends (<see cref="OutcomeTab"/>).</summary>
    public bool JudgedOnOutcome => Overrides(nameof(OutcomeTab));

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

    protected static IEnumerable<SimState> One(SimState state) => new[] { state };

    private readonly HashSet<string> _overrides;

    protected SimModel()
    {
        _name = GetType().Name;
        // The hooks this model overrides (here or in a base class of its own): the engine calls only those.
        _overrides = GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(method => method.IsVirtual && method.DeclaringType != typeof(SimModel) && method.GetBaseDefinition().DeclaringType == typeof(SimModel))
            .Select(method => method.Name).ToHashSet();
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

    public virtual decimal ModifyDamageAdditive(SimState state, int owner, DamageCtx damage) => 0m;
    public virtual decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) => 1m;
    public virtual decimal ModifyDamageMultiplicativeLate(SimState state, int owner, DamageCtx damage) => 1m;
    public virtual decimal ModifyDamageCap(SimState state, int owner, DamageCtx damage) => decimal.MaxValue;
    public virtual decimal ModifyBlockAdditive(SimState state, int owner, BlockCtx block) => 0m;
    public virtual decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block) => 1m;
    /// <summary>Hook.ModifyPowerAmountGiven: a power's amount as <paramref name="applier"/> gives it to <paramref name="target"/> (Snecko Skull).</summary>
    public virtual int ModifyPowerAmountGiven(SimState state, int owner, PowerId power, int amount, int? applier, int target, bool debuff) => amount;

    // ---- hooks (may branch)

    public virtual IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) => One(state);
    public virtual IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) => One(state);
    /// <summary>
    /// Not a game hook: once a play has fully resolved (every AfterCardPlayed, whatever their order, and what follows the play), for a
    /// tab judged on what the play leaves rather than on a moment inside it (Pael's Tears: the energy left). <paramref name="player"/>:
    /// who played it.
    /// </summary>
    public virtual IEnumerable<SimState> AfterPlayResolved(SimState state, int owner, int player) => One(state);
    // ---- damage-time hooks: deterministic, run inside the damage itself (SimEngine.Damage), so every source of damage
    // (hits, procs, orbs, end of turn) gets them

    public virtual void BeforeDamageReceived(SimState state, int owner, DamageCtx damage, int amount) { }
    public virtual void AfterDamageGiven(SimState state, int owner, DamageEvent hit) { }
    public virtual void AfterDamageReceived(SimState state, int owner, DamageEvent hit) { }
    /// <summary>A creature's block went from above 0 to 0 (AfterBlockBroken: Burrowed).</summary>
    /// <param name="breaker">Who dealt the damage that broke it (null: no one).</param>
    public virtual void AfterBlockBroken(SimState state, int owner, int target, int? breaker) { }
    /// <summary>The HP-loss pipeline (Hook.ModifyHpLost*): BeforeOsty, BeforeOstyLate, AfterOsty, AfterOstyLate.</summary>
    public virtual decimal ModifyHpLostBeforeOsty(SimState state, int owner, DamageCtx damage, decimal amount) => amount;
    public virtual decimal ModifyHpLostBeforeOstyLate(SimState state, int owner, DamageCtx damage, decimal amount) => amount;
    public virtual decimal ModifyHpLostAfterOsty(SimState state, int owner, DamageCtx damage, decimal amount) => amount;
    public virtual decimal ModifyHpLostAfterOstyLate(SimState state, int owner, DamageCtx damage, decimal amount) => amount;
    /// <summary>It changed an HP loss (the game's AfterModifyingHpLost*): most flash.</summary>
    public virtual void AfterModifyingHpLost(SimState state, int owner) => state.LogFired(Name, amount: 0, owner);
    public virtual IEnumerable<SimState> BeforeAttack(SimState state, int owner, AttackInfo attack) => One(state);
    public virtual IEnumerable<SimState> AfterAttack(SimState state, int owner, AttackInfo attack) => One(state);
    public virtual IEnumerable<SimState> AfterBlockGained(SimState state, int owner, int gainer, int amount, SimCard? card) => One(state);
    /// <summary>A card event (one per card or star): draws, exhausts, discards, created cards, stars, shuffles. Player: who it happened to.</summary>
    public virtual IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) => One(state);
    // ---- end of your side's turn (Hook.BeforeSideTurnEnd: every listener's VeryEarly, then Early, then BeforeSideTurnEnd;
    // Hook.AfterSideTurnEnd: AfterSideTurnEnd, then Late). Only for players' models whose owner takes part (SimEngine.TakesPart).

    public virtual IEnumerable<SimState> BeforeSideTurnEndVeryEarly(SimState state, int owner) => One(state);
    public virtual IEnumerable<SimState> BeforeSideTurnEndEarly(SimState state, int owner) => One(state);
    public virtual IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner) => One(state);
    public virtual IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner) => One(state);
    public virtual IEnumerable<SimState> AfterSideTurnEndLate(SimState state, int owner) => One(state);
    /// <summary>A player's Osty lost HP (AfterCurrentHpChanged with a loss on Osty): Necro Mastery. Runs inside the damage.</summary>
    public virtual void AfterOstyHpLost(SimState state, int owner, int player, int lost) { }
    /// <summary>A player tried a shot (AfterArrowShot: with or without an arrow): Covering Fire.</summary>
    public virtual IEnumerable<SimState> AfterArrowShot(SimState state, int owner, int shooter) => One(state);
    /// <summary>A shooter's arrow impaled an enemy (AfterArrowImpaled): Explosive Arrows.</summary>
    public virtual IEnumerable<SimState> AfterArrowImpaled(SimState state, int owner, int target, int shooter) => One(state);
    /// <summary>An orb was channeled for a player (AfterOrbChanneled): Metronome.</summary>
    public virtual IEnumerable<SimState> AfterOrbChanneled(SimState state, int owner, int player) => One(state);
    /// <summary>A power landed on a creature (after Artifact): Sleight of Flesh, Vicious, Shroud.</summary>
    public virtual IEnumerable<SimState> AfterPowerApplied(SimState state, int owner, PowerId power, int amount, int applier, int target, bool debuff) => One(state);
    /// <summary>A creature died (AfterDeath; once per death): Crab Rage, Ravenous.</summary>
    public virtual void AfterDeath(SimState state, int owner, int dead) { }
    /// <summary>After this power was applied to its owner (the game's AfterApplied): Covered sets up Intercept on its applier.</summary>
    public virtual void AfterApplied(SimState state, int owner, int applier, int amount) { }
    /// <summary>After a block amount it changed was gained (the game's AfterModifyingBlockAmount): Pael's Legion, Vambrace, Stretch.</summary>
    public virtual void AfterModifyingBlockAmount(SimState state, int owner, int amount, SimCardPlay? play) { }
    /// <summary>After a damage amount it changed (the game's AfterModifyingDamageAmount): most only flash (FlashesOnDamageModified).</summary>
    public virtual void AfterModifyingDamageAmount(SimState state, int owner, SimCard? card) { if (FlashesOnDamageModified) state.LogFired(Name, amount: 0, owner); }
}

/// <summary>The models the simulator knows, by the game's class name. A name not here isn't modelled.</summary>
public static class SimModels
{
    private static readonly Dictionary<string, SimModel> ByName = new();

    static SimModels()
    {
        foreach (var type in typeof(SimModel).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(SimModel).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null) continue;
            var model = (SimModel)Activator.CreateInstance(type)!;
            ByName[model.Name] = model;
        }
    }

    public static SimModel? Get(string name) => ByName.GetValueOrDefault(name);

    public static bool Has(string name) => ByName.ContainsKey(name);

    public static IEnumerable<string> Names => ByName.Keys;
}
