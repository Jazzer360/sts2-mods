using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>
/// A model's tab: a trigger (no number), or one with the amount it gives (block, damage, summon, the cards it draws, the energy it
/// gives: decision #132), or the cards it puts into your piles (CardsMade: Personal Hive's Dazed, decision #161).
/// </summary>
public enum TabKind { Trigger, Block, Damage, Summon, Draw, Energy, CardsMade }

/// <summary>
/// A card event the hooks see (AfterCardDrawn, AfterCardExhausted, AfterCardDiscarded, AfterCardGeneratedForCombat, AfterStarsGained,
/// AfterShuffle), one per card (or star). A card made is Generated whatever it is: its class and type are the event's card
/// (SimState.EventCard), which a hook tests as the game's code does (`card is Sadness`, `card.Type == CardType.Status`).
/// Each keeps its number: a play's script and a report's start state carry it (4 and 5 were a made Status and a made Sadness).
/// </summary>
public enum CardEvent { Drawn = 0, Exhausted = 1, Discarded = 2, Generated = 3, StarsGained = 6, StarsSpent = 7, Shuffled = 8, EnergySpent = 9, EnergyGained = 10, ArrowDislodged = 11 }

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
    /// <summary>It gains block (CardModel.GainsBlock): Stretch binds to such a play (or one whose enchantment does: <see cref="EnchantmentGainsBlock"/>).</summary>
    public bool GainsBlock { get; init; }
    /// <summary>Its enchantment gains block on play (EnchantmentModel.GainsBlock, beta: Adroit's powered block).</summary>
    public bool EnchantmentGainsBlock { get; init; }
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
    /// <summary>Whom it's aimed at (its TargetType: one enemy, every enemy, a random one; None for any other): Tag Team's test.</summary>
    public SimPlay.Target Targets { get; init; }
    /// <summary>The energy its own exhaust gives its owner (its AfterCardExhausted: Drum of Battle's), when a model sends it to the exhaust pile.</summary>
    public int ExhaustEnergy { get; init; }
    /// <summary>It can still be upgraded (Razor Tooth).</summary>
    public bool Upgradable { get; init; }
    /// <summary>Another card in your hand still costs energy or stars (Mummified Hand has something to make free).</summary>
    public bool OthersInHandCost { get; init; }
    /// <summary>Which card in hand it is (SimHandCard.Id), 0 = none.</summary>
    public int HandId { get; init; }
    /// <summary>
    /// Its class is a roll's (a card made at random of a type: Calamity's CardFactory.GetForCombat): only its type is known, so a test of its class
    /// makes a simulation unknown.
    /// </summary>
    public bool RandomClass { get; init; }

    /// <summary>Its dynamic values a model reads (Heavy Shot's AimMultiplier...), by the game's var name.</summary>
    public IReadOnlyDictionary<string, decimal> Vars { get; init; } = new Dictionary<string, decimal>();

    /// <summary>
    /// Its enchantment, as the engine runs it from its class's code after each play's own effects (CardModel.OnPlayWrapper: the card's OnPlay,
    /// then its enchantment's): null for none.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SimEnchantment? Enchantment { get; init; }

    public bool Is(SimCardTag tag) => Tags.Contains(tag);
}

/// <summary>
/// A card's enchantment (decision #217): its class, its amount (EnchantmentModel.Amount) and its own state its code reads and stores (its Status:
/// Swift and Sown turn themselves off after their card's first play), as the game has them when the card is played. The engine runs its read
/// programs (ModelKind.Enchantment) on its card's plays; its state through the play is kept in its owner's fields, under its card.
/// </summary>
public sealed record SimEnchantment(string Name, int Amount)
{
    /// <summary>Its own state as the game has it (its fields and properties its code reads: "Status" 0 Normal, 1 Disabled).</summary>
    public NamedCounts Fields { get; init; } = NamedCounts.Empty;

    /// <summary>
    /// The captured terms of a block its card gains (the game's own math for the sources the simulator doesn't model, and the enchantment's own:
    /// SimReaders.Block), for a block its code gains (Adroit's). Null: none captured.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CapturedTerms? BlockTerms { get; init; }
}

/// <summary>How the game shows a model acted (SimModel.Seen).</summary>
public enum ActedSeen { Flash, StatusCleared, NoEnergyLeft, CounterPassedLimit }

/// <summary>A tab judged on how the turn ends (SimModel.OutcomeTab): crossed or plain, and whether one play's outcome sets it off.</summary>
/// <param name="InBranch">
/// Whether one branch sets it off, for a warning's chance (decision #159: the share of the outcomes that do); null when the rule
/// can't be told branch by branch (no chance shown then).
/// </param>
/// <param name="InSome">
/// For a bonus (set off in every outcome: SetOff), whether some outcome of one play sets it off, so the checks accept its flash there
/// (Gremlin Horn on a kill that only some rolls make); null: SetOff alone.
/// </param>
/// <param name="InPlay">
/// The play itself sets it off, so it flashes while the play resolves and the play check holds it to that (a kill's Gremlin Horn).
/// False for an effect the play only makes certain for later: the end of your turn (Parrying Shield) or the enemy turn (Imbalanced,
/// stunned when its hit is fully blocked), which the play check can't see; the turn's own check holds those.
/// </param>
/// <param name="Change">
/// What one branch does to a bonus against ending the turn now (a payout decided where it's paid: decision #239), as the direction its source's
/// settings row can show (TabShows: sets it up, gives it up, makes it smaller); null for a rule with one direction only. With it, the tab is
/// judged on the branches whose change the row shows (CardTabs.FromRule): <see cref="Harmful"/> says which side the rule is on.
/// </param>
public sealed record OutcomeRule(bool Harmful, Func<SimOutcome, bool> SetOff, Func<SimState, bool>? InBranch = null, Func<SimOutcome, bool>? InSome = null, bool InPlay = true,
    Func<SimState, TabShows>? Change = null);

/// <summary>One card play as the hooks see it (the game's CardPlay): the card, its target, and which play of how many.</summary>
public sealed record SimCardPlay(SimCard Card, int Owner, [property: JsonConverter(typeof(CreatureIdJson))] int Target, int PlayIndex, int PlayCount, bool AutoPlay)
{
    /// <summary>
    /// Epic Retrieval (AfterArrowDislodged, source this card): energy for each arrow of its player's knocked loose during the play
    /// (decision #117).
    /// </summary>
    public int ArrowEnergy { get; init; }

    /// <summary>What this play is the first of this turn among its player's plays, set as it starts (SimKeys.FirstThisTurn's bits).</summary>
    public int FirstThisTurn { get; init; }

    /// <summary>
    /// Which card play of the simulation it is (the game's CardPlay: every play of a replay shares it, a card played inside it has its own), set as
    /// its first play starts: what a model binds to the card played (its own data keyed by cardPlay.Card) is this card play's (decision #267).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Serial { get; init; }
}

/// <summary>A damage calculation's inputs (Hook.ModifyDamage): who deals it to whom, its props, and the card it's from.</summary>
public sealed record DamageCtx(int? Dealer, int Target, SimProps Props, SimCard? Card, SimCardPlay? Play);

/// <summary>A block calculation's inputs (Hook.ModifyBlock): who gains it, its props, and the card it's from.</summary>
public sealed record BlockCtx(int Target, SimProps Props, SimCard? Card, SimCardPlay? Play);

/// <summary>An attack (AttackCommand) for BeforeAttack and AfterAttack: its id in this simulation, who makes it, its props and card.</summary>
public sealed record AttackInfo(int Id, int Dealer, SimProps Props, SimCard? Card);

/// <summary>
/// A damage event (DamageResult) for the hooks around it. <paramref name="FullyBlocked"/>: DamageResult.WasFullyBlocked, as
/// CreatureCmd.GetDamageResults works it out: a damage that isn't Unblockable, on a target with block before it or whose block took some, and no
/// HP lost after the HP-loss pipeline (its receiver's: Osty's when he takes it, so a hit Osty takes isn't); a pet's own result (a redirect's) never is.
/// </summary>
public sealed record DamageEvent(int? Dealer, int Target, SimProps Props, SimCard? Card, int Total, int Blocked, int Unblocked, bool Killed, bool FullyBlocked = false);

/// <summary>What a creature id names (Who.KindOf); None: no creature (Who.None). Pet: a pet the simulator keeps other than your Osty (Who.PetOf).</summary>
public enum CreatureKind { You, Teammate, YourOsty, Enemy, None, Pet }

/// <summary>The pets the simulator keeps for each player (PlayerCmd.AddPet's creatures): Osty (OstyCmd.Summon) and Wuppy (the Wuppy relic's).</summary>
public enum PetKind { Osty, Wuppy }

/// <summary>
/// Creature ids: one number for any creature, the only one the simulator uses (hooks need an owner, a target, a dealer). Enemies
/// are their SimEnemy.Id (0 and up), you are -1, teammate k (SimState.Teammates[k]) is -2 - k, a player's pets each their own (PetOf):
/// your Osty -100, your Wuppy -200, teammate k's -101 - k and -201 - k. Look a player up by id with SimState.Player.
/// </summary>
public static class Who
{
    public const int You = -1;
    /// <summary>
    /// No one: a play aimed at no creature (a card with no target, or an enemy-aimed one with no enemy to aim at). A card is never
    /// aimed at you, and this is no creature's id, so "aimed at no one" can't be read as you.
    /// </summary>
    public const int None = int.MinValue;
    /// <summary>Your Osty: his powers are his own (SimPlayer.OstyPowers), listening at his place among the creatures.</summary>
    public const int YourOsty = -100;
    /// <summary>The first id of a player's pets of a kind (you: PetBase + kind's offset; teammate k: one less for each).</summary>
    private const int OstyBase = -100, WuppyBase = -200, PetsPerKind = 100;
    public static int Teammate(int teammateIndex) => -2 - teammateIndex;
    public static CreatureKind KindOf(int id) => id >= 0 ? CreatureKind.Enemy : id == You ? CreatureKind.You : id == YourOsty ? CreatureKind.YourOsty
        : id == None ? CreatureKind.None : IsPet(id) ? CreatureKind.Pet : CreatureKind.Teammate;
    public static bool IsEnemy(int id) => id >= 0;
    public static bool IsPlayer(int id) => KindOf(id) is CreatureKind.You or CreatureKind.Teammate;

    /// <summary>A player's pet of a kind: its id (your Osty is YourOsty).</summary>
    public static int PetOf(int player, PetKind kind) => (kind == PetKind.Osty ? OstyBase : WuppyBase) - (player == You ? 0 : 1 + TeammateIndex(player));

    /// <summary>A player's Osty's id (PetOf with Osty).</summary>
    public static int OstyOf(int player) => PetOf(player, PetKind.Osty);

    /// <summary>The id names one of the pets the simulator keeps.</summary>
    public static bool IsPet(int id) => id is <= OstyBase and > OstyBase - PetsPerKind or <= WuppyBase and > WuppyBase - PetsPerKind;

    /// <summary>Which kind of pet the id names (an id that names none: Osty, never asked).</summary>
    public static PetKind PetKindOf(int id) => id <= WuppyBase ? PetKind.Wuppy : PetKind.Osty;

    /// <summary>The pet's player (its PetOwner): you or a teammate; Who.None for an id that names no pet.</summary>
    public static int PetOwnerOf(int id)
    {
        if (!IsPet(id)) return None;
        var index = (PetKindOf(id) == PetKind.Wuppy ? WuppyBase : OstyBase) - id;
        return index == 0 ? You : Teammate(index - 1);
    }

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
    /// It upgrades the card being played (CardCmd.Upgrade of cardPlay.Card: Razor Tooth): the adapter reads that card's upgraded copy for a replay's
    /// later plays (SimPlay.Upgraded).
    /// </summary>
    public virtual bool UpgradesCardInPlay => false;

    /// <summary>
    /// A power each application of which is a copy of its own, with its own amount and values (PowerInstanceType.Instanced: each
    /// Monologue or The Bomb played). Its Powers entry holds them merged (amounts added), so its model must say how its copies act:
    /// each on its own counter (the frozen counter models' list), on their values' total (VarTotal), or all the same. Its AfterApplied runs for
    /// every copy. InstancedPowerTests holds this to the game's InstanceType.
    /// </summary>
    public virtual bool Instanced => false;

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
    /// Its tab is crossed for this owner (an enemy's id, a player's): a hand-written model's crossing is its own (<see cref="Harmful"/>); a
    /// read model's is by whose side its effect helps (ReadModel.HarmfulFor).
    /// </summary>
    public virtual bool HarmfulFor(int owner) => Harmful;
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

    /// <summary>
    /// A model of the game class <paramref name="name"/> whose hooks are the ones its own code answers (a model read from the game's code,
    /// ReadModel: the engine calls it only for those, as it calls a hand-written model only for the ones it overrides).
    /// </summary>
    protected SimModel(string name, IEnumerable<string> hooks)
    {
        _name = name;
        _overrides = hooks.ToHashSet();
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

    /// <summary>
    /// It may act in this turn's-end or turn's-start hook as the enemies' side's turn ends or starts (SimEngine.EnemyTurnEnd, EnemyTurnStart), for this
    /// owner. A hook whose code acts only for its owner taking part (participants.Contains(base.Owner)) can't for a creature of the players' side,
    /// which isn't among the enemies: the engine skips a hook no listener may act in there, as it skips one no listener overrides.
    /// </summary>
    public virtual bool MayActOnTheEnemiesSide(string hook, int owner) => Overrides(hook);

    /// <summary>It acts inside damage (SimEngine.Damage): without such a model, damage takes the plain path.</summary>
    public bool ListensToDamage { get; }

    // ---- pipelines (pure: read the state, change nothing)

    public virtual decimal ModifyDamageAdditive(SimState state, int owner, DamageCtx damage) => 0m;
    public virtual decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) => 1m;
    public virtual decimal ModifyDamageMultiplicativeLate(SimState state, int owner, DamageCtx damage) => 1m;
    public virtual decimal ModifyDamageCap(SimState state, int owner, DamageCtx damage) => decimal.MaxValue;
    public virtual decimal ModifyBlockAdditive(SimState state, int owner, BlockCtx block) => 0m;
    public virtual decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block) => 1m;
    /// <summary>
    /// Hook.ModifyPowerAmountGiven's additive pass: what a model adds to a power's amount as <paramref name="applier"/> gives it to <paramref name="target"/>
    /// (Snecko Skull's Poison), each listener in turn on the amount so far. Pure.
    /// </summary>
    public virtual int ModifyPowerAmountGivenAdditive(SimState state, int owner, PowerId power, int amount, int applier, int target, bool debuff) => 0;
    /// <summary>Hook.ModifyPowerAmountGiven's multiplicative pass, after every listener's additive one. Pure.</summary>
    public virtual decimal ModifyPowerAmountGivenMultiplicative(SimState state, int owner, PowerId power, int amount, int applier, int target, bool debuff) => 1m;
    /// <summary>
    /// A hand-written model's answer in the multiplicative pass, as the amount it leaves (Unsettling Lamp's doubling: its own bookkeeping as it
    /// decides).
    /// </summary>
    public virtual int ModifyPowerAmountGiven(SimState state, int owner, PowerId power, int amount, int? applier, int target, bool debuff) => amount;
    /// <summary>Hook.AfterModifyingPowerAmountGiven: told to the models whose term changed a power's amount given (Snecko Skull's flash).</summary>
    public virtual void AfterModifyingPowerAmountGiven(SimState state, int owner, PowerId power) { }

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
    /// <summary>
    /// Hook.ModifyPreBlockDamageTarget: who a damage to <paramref name="damage"/>'s Target hits instead, before its block (Wuppy's Loyalty), as this
    /// listener leaves <paramref name="targetSoFar"/> (Who.None: no one yet, the hook's null). Pure.
    /// </summary>
    public virtual int ModifyPreBlockDamageTarget(SimState state, int owner, DamageCtx damage, int targetSoFar, decimal amount) => targetSoFar;
    /// <summary>
    /// Hook.ModifyUnblockedDamageTarget: who takes what a damage got past its target's block (Die For You), as this listener leaves the target so far
    /// (<paramref name="damage"/>'s Target). Pure.
    /// </summary>
    public virtual int ModifyUnblockedDamageTarget(SimState state, int owner, DamageCtx damage, decimal amount) => damage.Target;
    /// <summary>
    /// PowerModel.ShouldPowerBeRemovedAfterOwnerDeath, the other way round: the power stays on <paramref name="owner"/> when it dies (a pet's Die For You
    /// and Wuppy's Loyalty), so a pet that dies keeps it listening (SimEngine.PetDied). Pure.
    /// </summary>
    public virtual bool StaysOnDeadOwner(SimState state, int owner) => false;
    /// <summary>
    /// Hook.ShouldCreatureBeRemovedFromCombatAfterDeath: whether <paramref name="creature"/>, dying, leaves the fight (false keeps it: a pet's own Die
    /// For You keeps Osty, dead, for a summon to revive). A dead enemy kept so isn't followed (SimEngine.AllyDeaths: unknown). Pure.
    /// </summary>
    public virtual bool ShouldCreatureBeRemovedFromCombatAfterDeath(SimState state, int owner, int creature) => true;
    /// <summary>
    /// Hook.ShouldStopCombatFromEnding: the fight goes on with no primary enemy alive (CombatManager.IsEnding: a death that brings creatures in), as
    /// SimEngine.FightEnding asks it. Pure.
    /// </summary>
    public virtual bool ShouldStopCombatFromEnding(SimState state, int owner) => false;
    /// <summary>
    /// Hook.ShouldClearBlock: whether <paramref name="creature"/>'s block is cleared as its side's turn starts (CombatManager.StartTurn; Barricade,
    /// Burrowed: no), as SimEngine.EnemyTurn asks it of each enemy. Pure.
    /// </summary>
    public virtual bool ShouldClearBlock(SimState state, int owner, int creature) => true;
    /// <summary>Hook.ShouldAllowHitting: whether <paramref name="creature"/> can be hit and receive powers (Creature.IsHittable, CanReceivePowers). Pure.</summary>
    public virtual bool ShouldAllowHitting(SimState state, int owner, int creature) => true;
    /// <summary>The HP-loss pipeline (Hook.ModifyHpLost*): BeforeOsty, BeforeOstyLate, AfterOsty, AfterOstyLate.</summary>
    public virtual decimal ModifyHpLostBeforeOsty(SimState state, int owner, DamageCtx damage, decimal amount) => amount;
    public virtual decimal ModifyHpLostBeforeOstyLate(SimState state, int owner, DamageCtx damage, decimal amount) => amount;
    public virtual decimal ModifyHpLostAfterOsty(SimState state, int owner, DamageCtx damage, decimal amount) => amount;
    public virtual decimal ModifyHpLostAfterOstyLate(SimState state, int owner, DamageCtx damage, decimal amount) => amount;
    /// <summary>It changed an HP loss (the game's AfterModifyingHpLostBeforeOsty, or AfterOsty when <paramref name="afterOsty"/>): most flash.</summary>
    public virtual void AfterModifyingHpLost(SimState state, int owner, bool afterOsty) => state.LogFired(Name, amount: 0, owner);
    public virtual IEnumerable<SimState> BeforeAttack(SimState state, int owner, AttackInfo attack) => One(state);
    public virtual IEnumerable<SimState> AfterAttack(SimState state, int owner, AttackInfo attack) => One(state);
    public virtual IEnumerable<SimState> AfterBlockGained(SimState state, int owner, int gainer, int amount, SimCard? card) => One(state);
    /// <summary>A card event (one per card or star): draws, exhausts, discards, created cards, stars, shuffles. Player: who it happened to.</summary>
    public virtual IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) => One(state);
    // ---- end of a side's turn (Hook.BeforeSideTurnEnd: every listener's VeryEarly, then Early, then BeforeSideTurnEnd;
    // Hook.AfterSideTurnEnd: AfterSideTurnEnd, then Late): the players' as your turn ends, then the enemies' after their turn
    // (SimState.SideEnding). Every listener; the participants are SimState.EndsTheirTurn's.

    public virtual IEnumerable<SimState> BeforeSideTurnEndVeryEarly(SimState state, int owner) => One(state);
    public virtual IEnumerable<SimState> BeforeSideTurnEndEarly(SimState state, int owner) => One(state);
    public virtual IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner) => One(state);
    public virtual IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner) => One(state);
    public virtual IEnumerable<SimState> AfterSideTurnEndLate(SimState state, int owner) => One(state);
    // ---- a side's turn start (Hook.BeforeSideTurnStart, then Hook.AfterSideTurnStart: AfterSideTurnStart, then Late): the enemies', its
    // participants the living enemies, and in a projection the players' next turn, its participants the living players and their pets
    // (CombatManager.StartTurn: CreaturesOnCurrentSide; SimState.SideStarting). Every listener.

    public virtual IEnumerable<SimState> BeforeSideTurnStart(SimState state, int owner) => One(state);
    public virtual IEnumerable<SimState> AfterSideTurnStart(SimState state, int owner) => One(state);
    public virtual IEnumerable<SimState> AfterSideTurnStartLate(SimState state, int owner) => One(state);
    // ---- a player's next turn's start in a projection (CombatManager.SetupPlayerTurn: decision #306): Hook.AfterEnergyReset (then Late),
    // Hook.BeforeHandDraw (then Late), Hook.AfterPlayerTurnStart (Early, its own, Late), each told the player whose turn it sets up. Every listener.

    public virtual IEnumerable<SimState> AfterEnergyReset(SimState state, int owner, int player) => One(state);
    public virtual IEnumerable<SimState> AfterEnergyResetLate(SimState state, int owner, int player) => One(state);
    public virtual IEnumerable<SimState> BeforeHandDraw(SimState state, int owner, int player) => One(state);
    public virtual IEnumerable<SimState> BeforeHandDrawLate(SimState state, int owner, int player) => One(state);
    public virtual IEnumerable<SimState> AfterPlayerTurnStartEarly(SimState state, int owner, int player) => One(state);
    public virtual IEnumerable<SimState> AfterPlayerTurnStart(SimState state, int owner, int player) => One(state);
    public virtual IEnumerable<SimState> AfterPlayerTurnStartLate(SimState state, int owner, int player) => One(state);
    /// <summary>A player's Osty lost HP (AfterCurrentHpChanged with a loss on Osty): Necro Mastery. Runs inside the damage.</summary>
    public virtual void AfterOstyHpLost(SimState state, int owner, int player, int lost) { }
    /// <summary>Hook.ModifySummonAmount: a summon's amount for <paramref name="summoner"/> as this listener leaves it (OstyCmd.Summon). Pure.</summary>
    public virtual decimal ModifySummonAmount(SimState state, int owner, int summoner, decimal amount) => amount;
    /// <summary>Hook.AfterSummon: <paramref name="summoner"/> summoned <paramref name="amount"/> (OstyCmd.Summon's last step).</summary>
    public virtual IEnumerable<SimState> AfterSummon(SimState state, int owner, int summoner, int amount) => One(state);
    /// <summary>Hook.AfterOstyRevived: <paramref name="player"/>'s dead Osty came back with a summon.</summary>
    public virtual IEnumerable<SimState> AfterOstyRevived(SimState state, int owner, int player) => One(state);
    /// <summary>A player tried a shot (AfterArrowShot: with or without an arrow): Covering Fire.</summary>
    public virtual IEnumerable<SimState> AfterArrowShot(SimState state, int owner, int shooter) => One(state);
    /// <summary>A shooter's arrow impaled an enemy (AfterArrowImpaled): Explosive Arrows.</summary>
    public virtual IEnumerable<SimState> AfterArrowImpaled(SimState state, int owner, int target, int shooter) => One(state);
    /// <summary>An orb was channeled for a player (AfterOrbChanneled): Metronome.</summary>
    public virtual IEnumerable<SimState> AfterOrbChanneled(SimState state, int owner, int player) => One(state);
    /// <summary>Hook.ModifyOrbValue: an orb's value as this listener leaves it (Focus, Infused Core). Pure: reads the state, changes nothing.</summary>
    public virtual decimal ModifyOrbValue(SimState state, int owner, OrbCtx orb, decimal value) => value;
    /// <summary>Hook.ModifyOrbPassiveTriggerCount: how many times an orb's passive goes off (Gold-Plated Cables on the front orb). Pure.</summary>
    public virtual int ModifyOrbPassiveTriggerCounts(SimState state, int owner, OrbCtx orb, int count) => count;
    /// <summary>Hook.AfterModifyingOrbPassiveTriggerCount: this listener changed the count (a flash).</summary>
    public virtual void AfterModifyingOrbPassiveTriggerCount(SimState state, int owner, OrbCtx orb) { }
    /// <summary>Hook.AfterOrbEvoked: an orb was evoked, having acted on <see cref="SimState.OrbTargets"/> (Thunder).</summary>
    public virtual IEnumerable<SimState> AfterOrbEvoked(SimState state, int owner, OrbCtx orb) => One(state);
    /// <summary>A power landed on a creature (after Artifact): Sleight of Flesh, Vicious, Shroud.</summary>
    public virtual IEnumerable<SimState> AfterPowerApplied(SimState state, int owner, PowerId power, int amount, int applier, int target, bool debuff) => One(state);
    /// <summary>A creature died (AfterDeath; once per death): Crab Rage, Ravenous.</summary>
    public virtual void AfterDeath(SimState state, int owner, int dead) { }

    /// <summary>A Doom kill killed these creatures (Hook.AfterDiedToDoom, once per kill, with the list it killed): Book Repair Knife.</summary>
    public virtual void AfterDiedToDoom(SimState state, int owner, IReadOnlyList<int> creatures) { }
    /// <summary>
    /// After this power was applied to its owner (the game's AfterApplied): Covered sets up Intercept on its applier. Once, when it
    /// lands on a creature without it; for an instanced power (<see cref="Instanced"/>) on every copy.
    /// </summary>
    public virtual void AfterApplied(SimState state, int owner, int applier, int amount) { }
    /// <summary>
    /// This power came off <paramref name="owner"/> (PowerCmd.Remove: RemoveInternal, then PowerModel.AfterRemoved(oldOwner), told to it alone),
    /// with <paramref name="amount"/> the amount it had (RemoveInternal leaves it). Deterministic, on the state given (SimEngine.RemovePower).
    /// </summary>
    public virtual void AfterRemoved(SimState state, int owner, int amount) { }
    /// <summary>After a block amount it changed was gained (the game's AfterModifyingBlockAmount): Pael's Legion, Vambrace, Stretch.</summary>
    public virtual void AfterModifyingBlockAmount(SimState state, int owner, int amount, SimCardPlay? play) { }
    /// <summary>After a damage amount it changed (the game's AfterModifyingDamageAmount): most only flash (FlashesOnDamageModified).</summary>
    public virtual void AfterModifyingDamageAmount(SimState state, int owner, SimCard? card) { if (FlashesOnDamageModified) state.LogFired(Name, amount: 0, owner); }

    // ---- a card play's own shape, decided once before its plays (CardModel.OnPlayWrapper: Hook.ModifyCardPlayResultLocation, then
    // GeneratePlayCount's Hook.ModifyCardPlayCount), each listener in turn on what the one before it gave

    /// <summary>
    /// Hook.ModifyCardPlayCount: how many times the card play plays (Echo Form). Asked once, before its first play, for a card play whose
    /// count the simulator decides (CardPlayAction.CountByModels); a model that changes it is told after (AfterModifyingCardPlayCount).
    /// </summary>
    public virtual int ModifyCardPlayCount(SimState state, int owner, SimCardPlay play, int count) => count;
    public virtual void AfterModifyingCardPlayCount(SimState state, int owner, SimCardPlay play) { }
    /// <summary>
    /// Hook.ModifyCardPlayResultLocation: where the card goes once its plays are over (Feral: back to your hand). Asked once, before its
    /// first play; a model that changes it is told after (AfterModifyingCardPlayResultLocation), still before the plays.
    /// </summary>
    public virtual SimResultLocation ModifyCardPlayResultLocation(SimState state, int owner, SimCardPlay play, SimResultLocation location) => location;
    public virtual void AfterModifyingCardPlayResultLocation(SimState state, int owner, SimCardPlay play, SimResultLocation location) { }
    /// <summary>The questions its game code asks of the fight's log (decision #191), as the history ledger counts them (held to the code: HistoryLedgerTests).</summary>
    public virtual IReadOnlyList<HistoryQuery> History => Array.Empty<HistoryQuery>();
}

/// <summary>
/// Where a played card goes once its plays are over, as Hook.ModifyCardPlayResultLocation leaves it: its usual pile (Default), the top of
/// its owner's hand (Feral), the top of their draw pile (Nostalgia, Rebound), the exhaust pile (the Exhaust keyword, Corruption's Skills),
/// nowhere (a Power, a copy made by a replay).
/// </summary>
public enum SimResultLocation { Default, HandTop, DrawTop, Exhaust, Nowhere }

/// <summary>
/// A card's own hooks (its class's overrides of the game's hooks), listening from its owner's piles: the game lists every card in
/// every pile after its owner's relics, potions and orbs (CombatState.IterateHookListeners), so a card answers a hook after every
/// power and relic of its owner. One model per class (SimPlayer.Cards), named after the game's card class; each acts only on its own
/// cards' events (the card drawn, the cards in hand), as each copy's hook does in the game.
/// </summary>
public abstract class CardListener : SimModel
{
}

/// <summary>
/// The models the simulator knows, by the game's class name: the hand-written ones (a class of this assembly each) and the ones read from the
/// game's code (ReadModel, decision #195), which <see cref="Read"/> gives once: in the game the adapter reads them from the running game's
/// code on a worker as the mod loads (StartReading), in the simulator's tests they come from the file the catalog tests write, read on the first
/// ask. Made once and never changed after, so every search's thread shares them. A name not here isn't modelled.
/// </summary>
public static class SimModels
{
    /// <summary>
    /// Where the read models come from (set once, before any simulation: the tests' module initializers, read on the first ask; the game
    /// starts them on a worker instead: <see cref="StartReading"/>). Null: none.
    /// </summary>
    public static Func<IEnumerable<ModelSpec>>? Read { get; set; }

    /// <summary>Where every power class's rules come from (PowerRule: decision #234), set with <see cref="Read"/>. Null: none (no class can go below 0, none scales).</summary>
    public static Func<IEnumerable<PowerRule>>? ReadRules { get; set; }

    /// <summary>Where the turn's-end listeners the simulator doesn't follow come from (TurnEndGap: decision #303), set with <see cref="Read"/>. Null: none.</summary>
    public static Func<IEnumerable<TurnEndGap>>? ReadTurnEndGaps { get; set; }

    /// <summary>Where the listeners the players' next turn's start doesn't follow come from (TurnStartGap: decision #306), set with <see cref="Read"/>. Null: none.</summary>
    public static Func<IEnumerable<TurnStartGap>>? ReadTurnStartGaps { get; set; }

    /// <summary>The read started on a worker (StartReading), or null: the models are read on the first ask, on its thread.</summary>
    private static volatile Task<LoadedModels>? _reading;

    /// <summary>
    /// Starts reading the models on a worker thread (the game, at the mod's load: reading every power's and relic's code takes a few tenths of
    /// a second, which the game's thread shouldn't spend). An ask before it's done waits for it (a search on a worker); the frame doesn't ask
    /// until it is (<see cref="Loaded"/>, SimWait.Models: a prediction is pending until then, never made without the models).
    /// </summary>
    /// <returns>The read, so the caller can say when it's done (the dev log's load times).</returns>
    public static Task StartReading(Func<IEnumerable<ModelSpec>> read, Func<IEnumerable<PowerRule>> readRules, Func<IEnumerable<TurnEndGap>>? readTurnEndGaps = null,
        Func<IEnumerable<TurnStartGap>>? readTurnStartGaps = null)
    {
        Read = read;
        ReadRules = readRules;
        ReadTurnEndGaps = readTurnEndGaps;
        ReadTurnStartGaps = readTurnStartGaps;
        var reading = Task.Run(Load);
        _reading = reading;
        return reading;
    }

    /// <summary>How many models are known (hand-written and read), for the dev log's load times; waits for the read.</summary>
    public static int Count => ByName.Count;

    /// <summary>The models are read (or are read on the first ask, as in the tests): asking for one doesn't wait.</summary>
    public static bool Loaded => _reading?.IsCompleted ?? true;

    /// <summary>Waits up to <paramref name="milliseconds"/> for the models: true once they're read.</summary>
    public static bool WaitLoaded(int milliseconds)
    {
        if (_reading is not { } reading || reading.IsCompleted) return true;
        if (milliseconds <= 0) return false;
        try { return reading.Wait(milliseconds); }
        catch (AggregateException) { return true; } // a failed read is rethrown by the first ask
    }

    /// <summary>
    /// The models and the classes whose hooks couldn't be read, made once and never changed after (shared by every thread); the orbs' programs by
    /// their kind (an orb is never a listener: the engine runs its programs where OrbCmd and OrbQueue do).
    /// </summary>
    private sealed record LoadedModels(IReadOnlyDictionary<string, SimModel> ByName, IReadOnlyDictionary<string, string> Unread, IReadOnlyDictionary<OrbKind, ReadModel> Orbs,
        IReadOnlyDictionary<string, ReadModel> Enchantments, IReadOnlyDictionary<string, LoadedRule> Rules, IReadOnlyDictionary<string, TurnEndGap> TurnEndGaps,
        IReadOnlyDictionary<string, TurnStartGap> TurnStartGaps);

    /// <summary>A power class's rules as the engine asks them (PowerRule), its scaling compiled once.</summary>
    private sealed record LoadedRule(bool AllowNegative, Func<SimState, int, decimal>? Scaled, string? Unread, bool Debuff, bool Counter);

    private static readonly Lazy<LoadedModels> Models = new(() => _reading?.GetAwaiter().GetResult() ?? Load(), LazyThreadSafetyMode.ExecutionAndPublication);

    private static IReadOnlyDictionary<string, SimModel> ByName => Models.Value.ByName;

    private static readonly Lazy<Dictionary<string, SimModel>> HandWrittenByName = new(() =>
    {
        var byName = new Dictionary<string, SimModel>(StringComparer.Ordinal);
        foreach (var type in typeof(SimModel).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(SimModel).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null) continue;
            var model = (SimModel)Activator.CreateInstance(type)!;
            byName[model.Name] = model;
        }
        return byName;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Reads the models once: the hand-written ones and the read ones, with the classes whose hooks a game update made unreadable
    /// (ModelSpec.Unread) and why. Everything it makes is its own until it's published, and never changed after.
    /// </summary>
    private static LoadedModels Load()
    {
        var byName = new Dictionary<string, SimModel>(HandWrittenByName.Value, StringComparer.Ordinal);
        var unread = new Dictionary<string, string>(StringComparer.Ordinal);
        var orbs = new Dictionary<OrbKind, ReadModel>();
        var enchantments = new Dictionary<string, ReadModel>(StringComparer.Ordinal);
        // A read model never stands in for a hand-written one: a class is one or the other (the reader skips a hand-written class: ReadModels).
        foreach (var spec in Read?.Invoke() ?? Enumerable.Empty<ModelSpec>())
        {
            if (byName.ContainsKey(spec.Name)) continue;
            if (spec.Unread != null) { unread[spec.Name] = spec.Unread; continue; }
            if (spec.Kind == ModelKind.Orb)
            {
                try { if (spec.Orb is { } kind) orbs[kind] = new ReadModel(spec); }
                catch (InvalidOperationException exception) { unread[spec.Name] = $"{spec.Name}'s code couldn't be compiled ({exception.Message}): what it does isn't simulated"; }
                continue;
            }
            // An affliction (decision #225): a fact of its card, nothing to run; only an unread one (above) changes a simulation.
            if (spec.Kind == ModelKind.Affliction) continue;
            // An enchantment (decision #217): its programs run where its card plays, never as a listener.
            if (spec.Kind == ModelKind.Enchantment)
            {
                try { enchantments[spec.Name] = new ReadModel(spec); }
                catch (InvalidOperationException exception) { unread[spec.Name] = $"{spec.Name}'s code couldn't be compiled ({exception.Message}): what it does isn't simulated"; }
                continue;
            }
            // A program the simulator can't compile (a command on a creature its engine functions don't name) is the class unread, never a
            // failure of every model (decision #92).
            try { byName.Add(spec.Name, new ReadModel(spec)); }
            catch (InvalidOperationException exception) { unread[spec.Name] = $"{spec.Name}'s hooks couldn't be compiled ({exception.Message}): what it does isn't simulated"; }
        }
        var rules = new Dictionary<string, LoadedRule>(StringComparer.Ordinal);
        foreach (var rule in ReadRules?.Invoke() ?? Enumerable.Empty<PowerRule>())
        {
            Func<SimState, int, decimal>? scaled = null;
            var unreadRule = rule.Unread;
            try { if (rule.ScaledForPlayers is { } value) scaled = ReadModel.CompileScaling(rule.Name, value); }
            catch (InvalidOperationException exception) { unreadRule = $"{rule.Name}'s scaling for the players couldn't be compiled ({exception.Message})"; }
            rules[rule.Name] = new LoadedRule(rule.AllowNegative, scaled, unreadRule, rule.Debuff, rule.Counter);
        }
        var turnEndGaps = new Dictionary<string, TurnEndGap>(StringComparer.Ordinal);
        foreach (var gap in ReadTurnEndGaps?.Invoke() ?? Enumerable.Empty<TurnEndGap>())
            if (!byName.ContainsKey(gap.Name) && (gap.ActsForAnEnemy || gap.ActsForAPlayer)) turnEndGaps[gap.Name] = gap;
        // A hand-written model may leave a turn's-start hook of its class unanswered (decision #306): its gap stays; a read one runs itself there.
        var turnStartGaps = new Dictionary<string, TurnStartGap>(StringComparer.Ordinal);
        foreach (var gap in ReadTurnStartGaps?.Invoke() ?? Enumerable.Empty<TurnStartGap>())
            if ((!byName.ContainsKey(gap.Name) || IsHandWritten(gap.Name)) && (gap.ActsForAnEnemy || gap.ActsForAPlayer)) turnStartGaps[gap.Name] = gap;
        return new LoadedModels(byName, unread, orbs, enchantments, rules, turnEndGaps, turnStartGaps);
    }

    /// <summary>The power can go below 0 (PowerModel.AllowNegative, read from its code: decision #234): ShouldRemoveDueToAmount takes it at exactly 0.</summary>
    public static bool AllowsNegative(PowerId power) => Models.Value.Rules.GetValueOrDefault(power.Name)?.AllowNegative == true;

    /// <summary>
    /// The power at <paramref name="amount"/> is a debuff (PowerModel.GetTypeForAmount, its Type, StackType and AllowNegative read from its code:
    /// decision #293): a counter that can go below 0 is one below 0 (Strength -2); a debuff that can't is a buff below 0; any other is its Type.
    /// </summary>
    public static bool IsDebuffAt(PowerId power, int amount)
    {
        if (Models.Value.Rules.GetValueOrDefault(power.Name) is not { } rule) return false;
        if (rule.Counter && rule.AllowNegative && amount < 0) return true;
        if (!rule.AllowNegative && rule.Debuff && amount < 0) return false;
        return rule.Debuff;
    }

    /// <summary>
    /// A new <paramref name="power"/> on an enemy as PowerCmd.Apply lands it in a fight of more than one player: <paramref name="amount"/> scaled by its
    /// class's GetScaledAmountForMultiplayer (read from its code when its ShouldScaleInMultiplayer is true: decision #234), whole (ApplyInternal's
    /// cast). Unscaled for one that isn't; one whose scaling couldn't be read makes the simulation unknown.
    /// </summary>
    public static int ScaledForPlayers(SimState state, PowerId power, int amount)
    {
        if (Models.Value.Rules.GetValueOrDefault(power.Name) is not { } rule) return amount;
        if (rule.Unread is { } why) { state.Unmodelled ??= why; return amount; }
        return rule.Scaled is { } scaled ? (int)scaled(state, amount) : amount;
    }

    /// <summary>An enchantment's programs read from its class's code (ModelKind.Enchantment), or null: none to run, or not read (UnreadReason).</summary>
    public static ReadModel? Enchantment(string name) => Models.Value.Enchantments.GetValueOrDefault(name);

    /// <summary>An orb kind's programs read from its class's code (ModelKind.Orb), or null: not read (an unread class: <see cref="OrbUnreadReason"/>).</summary>
    public static ReadModel? Orb(OrbKind kind) => Models.Value.Orbs.GetValueOrDefault(kind);

    /// <summary>Why an orb kind's class couldn't be read (its class's name: the kind and "Orb"), or a reason the orb isn't simulated.</summary>
    public static string OrbUnreadReason(OrbKind kind) =>
        UnreadReason(kind + "Orb") ?? $"{kind}Orb's code isn't read: what it does isn't simulated";

    /// <summary>The class has a hand-written model (Sim/Models): it keeps it until a parity proof converts it (decision #195), so it isn't read.</summary>
    public static bool IsHandWritten(string name) => HandWrittenByName.Value.ContainsKey(name);

    /// <summary>The hand-written model of a class, or null (made without reading the game's code: safe while the read models load).</summary>
    public static SimModel? HandWritten(string name) => HandWrittenByName.Value.GetValueOrDefault(name);

    /// <summary>The hand-written models' names (Sim/Models), each a game class's or one of the simulator's own.</summary>
    public static IEnumerable<string> HandWrittenNames => HandWrittenByName.Value.Keys;

    /// <summary>
    /// Why a class's hooks couldn't be read, when they read whole at the reviewed builds and don't now (a game update): a simulation it's in
    /// is unknown (SimState.Unmodelled), never exact without it (decision #92). Null: read, hand-written, or never modelled.
    /// </summary>
    public static string? UnreadReason(string name) => Models.Value.Unread.GetValueOrDefault(name);

    /// <summary>
    /// Why the class, owned by an enemy (<paramref name="enemyOwner"/>) or a player, acts as the enemies' side's turn ends where the simulator doesn't
    /// follow it (TurnEndGap, decision #303): the state after that turn's end is unknown. Null: modelled, or it never acts there, or, in a
    /// projection (<paramref name="forecasting"/>), what it does there changes nothing a projection follows (TurnEndGap.ForecastIgnores, decision #308).
    /// </summary>
    public static string? TheirTurnEndGap(string name, bool enemyOwner, bool forecasting = false) =>
        Models.Value.TurnEndGaps.GetValueOrDefault(name) is { } gap && (enemyOwner ? gap.ActsForAnEnemy : gap.ActsForAPlayer) && !(forecasting && gap.ForecastIgnores) ? gap.Why : null;

    /// <summary>
    /// Why the class, owned by an enemy (<paramref name="enemyOwner"/>) or a player, acts as the players' next turn starts where a projection doesn't
    /// follow it (TurnStartGap, decision #306): the state after that turn's start is unknown. Null: modelled there, or it never acts there, or what it
    /// does there changes nothing a projection follows (TurnStartGap.ForecastIgnores, decision #308: only a projection runs a turn's start).
    /// </summary>
    public static string? YourTurnStartGap(string name, bool enemyOwner) =>
        Models.Value.TurnStartGaps.GetValueOrDefault(name) is { } gap && (enemyOwner ? gap.ActsForAnEnemy : gap.ActsForAPlayer) && !gap.ForecastIgnores ? gap.Why : null;

    public static SimModel? Get(string name) => ByName.GetValueOrDefault(name);

    public static bool Has(string name) => ByName.ContainsKey(name);

    /// <summary>The model decides a card play's count itself (it overrides ModifyCardPlayCount: Echo Form): the adapter leaves its part out (CardPlays.ModelledReplays).</summary>
    public static bool DecidesPlayCount(string name) => Get(name) is { } model && model.Overrides(nameof(SimModel.ModifyCardPlayCount));

    public static IEnumerable<string> Names => ByName.Keys;
}
