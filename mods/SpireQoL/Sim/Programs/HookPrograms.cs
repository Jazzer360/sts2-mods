using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>
/// The game hooks a model read from its code can answer (decision #195), each where the simulator runs the same hook for every model
/// (SimModel): the card play's, the card events' (AfterCardEvent by its event), a power landing, and the damage and block math.
/// </summary>
public enum HookName
{
    BeforeCardPlayed,
    AfterCardPlayed,
    AfterCardDrawn,
    AfterCardExhausted,
    AfterCardDiscarded,
    AfterCardGeneratedForCombat,
    AfterShuffle,
    AfterStarsSpent,
    AfterStarsGained,
    AfterEnergySpent,
    AfterArrowDislodged,
    AfterPowerAmountChanged,
    BeforeSideTurnEndVeryEarly,
    BeforeSideTurnEndEarly,
    BeforeSideTurnEnd,
    AfterSideTurnEnd,
    AfterSideTurnEndLate,
    ModifyCardPlayCount,
    AfterModifyingCardPlayCount,
    AfterDamageGiven,
    AfterDamageReceived,
    AfterDeath,
    ModifyDamageAdditive,
    ModifyDamageMultiplicative,
    ModifyDamageMultiplicativeLate,
    ModifyBlockAdditive,
    ModifyBlockMultiplicative,
    /// <summary>PowerModel.AfterApplied: a new instance of the power landed (a copy of an instanced one: each application).</summary>
    AfterApplied,
}

/// <summary>
/// How a power's applications are kept (the game's PowerInstanceType): one instance its amounts stack into, a copy of its own per application
/// (Instanced: each The Bomb played counts down on its own), or a copy per applier (InstancedPerApplier: each player's Strangle).
/// </summary>
public enum PowerCopies { One, EachApplication, PerApplier }

/// <summary>
/// What a hook's code asks of its parameters and its model, each one game query (or a small family of them) translated once for every
/// model that asks it (SimAdapter.HookFacts): a closed set, as the card conditions' SimTest is.
/// </summary>
public enum HookFact
{
    /// <summary>The power's own amount (base.Amount).</summary>
    Amount,
    /// <summary>The card played is its owner's (cardPlay.Card.Owner == base.Owner, or .Creature == base.Owner).</summary>
    PlayIsOwners,
    /// <summary>The hook's card's type (the card played, drawn, exhausted or made), as SimCardType's number.</summary>
    CardType,
    /// <summary>The card played is Ethereal (its Keywords contain Ethereal).</summary>
    CardEthereal,
    /// <summary>The card played has the tag named in Of (its Tags contain Shiv).</summary>
    CardTag,
    /// <summary>The card played's cost as it's played (EnergyCost.GetResolved()).</summary>
    CardEnergy,
    /// <summary>A card event's card is its owner's (card.Owner == base.Owner; a made card's creator is base.Owner).</summary>
    EventIsOwners,
    /// <summary>The event's card is of the class named in Of (card is Sadness).</summary>
    CardIs,
    /// <summary>It's its owner's side's turn (CurrentSide == Side): the players', until the enemy turn starts.</summary>
    OwnersTurn,
    /// <summary>The power landing was applied by its owner (applier == base.Owner).</summary>
    ApplierIsOwner,
    /// <summary>The power landing is the one named in Of (power is VulnerablePower).</summary>
    PowerIs,
    /// <summary>The power landing is one of the classes listed in Of, comma-separated (power is ITemporaryPower: the classes that are).</summary>
    PowerIn,
    /// <summary>How much of the power landed (amount).</summary>
    PowerAmount,
    /// <summary>The power landing is a debuff for its amount (power.GetTypeForAmount(amount) == Debuff).</summary>
    PowerIsDebuff,
    /// <summary>The stars a spend took (AfterStarsSpent's amount): the stars of the play spending them, as the simulator spends stars (one event a play).</summary>
    StarsSpent,
    /// <summary>The power landed on an enemy (power.Owner.IsEnemy).</summary>
    PowerOnEnemy,
    /// <summary>The damage's dealer is the owner (dealer == base.Owner).</summary>
    DealerIsOwner,
    /// <summary>The damage's dealer is one of the owner's pets (base.Owner.Pets.Contains(dealer): their Osty).</summary>
    DealerIsOwnersPet,
    /// <summary>The damage's or block's target is the owner (target == base.Owner).</summary>
    TargetIsOwner,
    /// <summary>The damage is a powered attack (props.IsPoweredAttack()).</summary>
    PoweredAttack,
    /// <summary>The block is a powered card's or move's (props.IsPoweredCardOrMonsterMoveBlock()).</summary>
    PoweredBlock,
    /// <summary>The damage or block is a card's or a monster move's (props.IsCardOrMonsterMove(): its Move flag), powered or not.</summary>
    CardOrMonsterMove,
    /// <summary>The damage or block comes from a card (cardSource != null).</summary>
    FromCard,
    /// <summary>The damage's or block's card is the owner's (cardSource.Owner.Creature == base.Owner).</summary>
    CardIsOwners,
    /// <summary>The owner takes part in the side's turn ending (participants.Contains(base.Owner)).</summary>
    TakesPart,
    /// <summary>
    /// The amount the model bound to the play in progress is taken (its own data's Remove(cardPlay.Card, out amount), asked once a hook run):
    /// 1 when one was bound, which is then gone; the amount is <see cref="BoundTaken"/>.
    /// </summary>
    TakeBound,
    /// <summary>The amount this hook run took from what the model bound to the play (Remove's out value; 0 when none was).</summary>
    BoundTaken,
    /// <summary>The card play's count as the listeners before it left it (ModifyCardPlayCount's playCount).</summary>
    PlayCountSoFar,
    /// <summary>The HP a hit took past block (AfterDamageGiven's and AfterDamageReceived's result.UnblockedDamage).</summary>
    HitUnblocked,
    /// <summary>The owner's block (base.Owner.Creature.Block).</summary>
    OwnerBlock,
    /// <summary>The cards in the owner's hand (PileType.Hand.GetPile(base.Owner).Cards.Count; IsEmpty is this at 0).</summary>
    OwnerHandCount,
    /// <summary>The owner is dead (base.Owner.IsDead): a player who died, an enemy with no HP left.</summary>
    OwnerDead,
    /// <summary>The players' turn number this turn (base.Owner.PlayerCombatState.TurnNumber: SimState.TurnNumber).</summary>
    TurnNumber,
    /// <summary>The owner's orbs of the kind named in Of (OrbQueue.Orbs.Count(o => o is FrostOrb): an OrbKind's name).</summary>
    OwnerOrbCount,
    /// <summary>
    /// The creature that died is the one who applied this power (AfterDeath's creature == base.Applier): the power keeps its applier as its Data
    /// (DataCreature), from the adapter's read and from where it lands (ModelSpec.UsesApplier).
    /// </summary>
    DeadIsApplier,
    /// <summary>The creature that died is the owner (AfterDeath's creature == base.Owner).</summary>
    DeadIsOwner,
    /// <summary>The power has an applier (base.Applier isn't null): its Data names one (ModelSpec.UsesApplier).</summary>
    HasApplier,
    /// <summary>The power's applier is a player (base.Applier?.Player isn't null).</summary>
    ApplierIsPlayer,
    /// <summary>The card played is its applier's (cardPlay.Card.Owner == base.Applier.Player).</summary>
    PlayIsAppliers,
    /// <summary>The creature named in Creature is there (dealer != null, target != null).</summary>
    CreatureThere,
    /// <summary>The creature named in Creature is a player (creature.IsPlayer, creature.Player != null): a pet (Osty) isn't.</summary>
    CreatureIsPlayer,
    /// <summary>The creature named in Creature is a monster (creature.IsMonster): an enemy, or a pet (Osty).</summary>
    CreatureIsMonster,
    /// <summary>The creature named in Creature has the power named in Of (creature.HasPower&lt;T&gt;(), creature.GetPower&lt;T&gt;() != null).</summary>
    CreatureHasPower,
    /// <summary>The creature named in Creature is a player with the relic named in Of (creature.Player?.GetRelic&lt;T&gt;() != null).</summary>
    CreatureHasRelic,
    /// <summary>The amount of the power named in Of on the creature named in Creature (a called power's base.Amount: Cruelty's).</summary>
    CreaturePowerAmount,
    /// <summary>The creatures named in Creature and Other are the same one (target == base.Owner of a model called on another creature).</summary>
    SameCreature,
    /// <summary>The creatures named in Creature and Other are on the same side (creature.Side == base.Owner.Side).</summary>
    SameSide,
    /// <summary>The creature named in Creature is on the players' side (its Side is the side whose turn ends: the simulator ends the players').</summary>
    CreatureOnPlayersSide,
    /// <summary>It's the enemies' turn (CombatState.CurrentSide == CombatSide.Enemy): from the enemy turn's start.</summary>
    EnemiesTurn,
}

/// <summary>A creature a hook's code names: its model's owner, or one of what the hook was given (the dealer, the target, the dead...).</summary>
public enum HookWho
{
    /// <summary>The model's owner (base.Owner, base.Owner.Creature).</summary>
    Owner,
    /// <summary>The damage's dealer (dealer: a Modify hook's, a hit's).</summary>
    Dealer,
    /// <summary>The damage's or block's target (target: a Modify hook's, a hit's).</summary>
    Target,
    /// <summary>The creature that died (AfterDeath's creature).</summary>
    Dead,
    /// <summary>The creature the power landing landed on (power.Owner in AfterPowerAmountChanged).</summary>
    PowerOwner,
    /// <summary>Who applied this power (base.Applier, kept as its Data).</summary>
    Applier,
}

/// <summary>A creature a hook's code names (HookWho), or the player who owns it when it's a pet (creature.PetOwner.Creature: Osty's player).</summary>
public sealed record HookCreature(HookWho Who)
{
    public bool PetOwner { get; init; }

    public override string ToString() => PetOwner ? Who + ".PetOwner" : Who.ToString();
}

/// <summary>An operation of the code on two values (decimal throughout; a true-or-false is 1 or 0).</summary>
public enum HookOperation { Add, Subtract, Multiply, Divide, Remainder, Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual, And, Or }

/// <summary>A value a hook's code works out (an amount, a test), read from its instructions: decimal, a true-or-false is 1 or 0.</summary>
public abstract record HookValue;

/// <summary>A constant in the code.</summary>
public sealed record HookConstant(decimal Value) : HookValue;

/// <summary>What the hook's parameters or its model say (HookFact), with the class it names when it names one.</summary>
public sealed record HookFactValue(HookFact Fact) : HookValue
{
    public string? Of { get; init; }

    /// <summary>The creature a creature's fact is about (CreatureThere, CreatureHasPower, SameCreature...); null for the others.</summary>
    public HookCreature? Creature { get; init; }

    /// <summary>The other creature a comparison of two is about (SameCreature, SameSide).</summary>
    public HookCreature? Other { get; init; }
}

/// <summary>
/// One of the model's values (base.DynamicVars[Key]), as the adapter read it (SimEngine.Var); its canonical value (the class's CanonicalVars, read
/// from its code) when the state has none.
/// </summary>
public sealed record HookVar(string Key) : HookValue
{
    public decimal Canonical { get; init; }
}

/// <summary>
/// One of the model's own fields or properties (Kunai's AttacksPlayedThisTurn): simulator state (SimPlayer.Fields, SimEnemy.Fields), seeded from
/// the game and changed by its programs' stores.
/// </summary>
public sealed record HookField(string Field) : HookValue
{
    /// <summary>The prefix of one of a model's own values kept as its state ("Var.CardsLeft"): one its code stores, or each copy's own.</summary>
    public const string VarPrefix = "Var.";

    /// <summary>
    /// What it is before anything stores it (a new instance's: one of its own values its code stores, "Var.CardsLeft", starts at the canonical
    /// value its CanonicalVars give it; any other field at 0).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal Canonical { get; init; }
}

/// <summary>
/// A question to the game's log of the fight (CombatManager.Instance.History: Count or Any of its entries, decision #191), as the history
/// ledger counts it for the owner's player (SimEngine.HistoryCount): its count, or 1 when there's any (Any). A count the ledger doesn't know
/// makes the simulation unknown, never 0.
/// </summary>
public sealed record HookHistory(HistoryQuery Query) : HookValue
{
    public bool Any { get; init; }
}

/// <summary>Two values the code combines or compares.</summary>
public sealed record HookMath(HookOperation Operation, HookValue Left, HookValue Right) : HookValue;

/// <summary>
/// One test the code makes at one place (<paramref name="At"/>: its method and offset), 1 when it holds: asked once a hook run, where a step first
/// needs it, as the game asks it once at its branch (MiniRegent's !UsedThisTurn, asked before the store that sets it, holds for the Strength after it).
/// </summary>
public sealed record HookTest(string At, HookValue Test) : HookValue;

/// <summary>Not: 1 when the value is 0.</summary>
public sealed record HookNot(HookValue Inner) : HookValue;

/// <summary>One way through a method to a value it returns: the value, when the way's test holds.</summary>
public sealed record HookWay(HookValue When, HookValue Value);

/// <summary>What a method returns: the value of the first of its ways whose test holds (each return of the code, in order).</summary>
public sealed record HookWays(IReadOnlyList<HookWay> Ways) : HookValue;

/// <summary>Who a hook's command acts on.</summary>
public enum HookTarget
{
    /// <summary>The model's owner (base.Owner, base.Owner.Creature, base.Owner.Player): damage to it, through the pipeline on it.</summary>
    Owner,
    /// <summary>Every enemy that can be hit (CombatState.HittableEnemies).</summary>
    AllEnemies,
    /// <summary>A random enemy that can be hit (a roll over HittableEnemies): a branch per enemy, never the roll.</summary>
    RandomEnemy,
    /// <summary>The creature the power landing landed on (power.Owner in AfterPowerAmountChanged).</summary>
    PowerOwner,
    /// <summary>The creature a hit landed on (AfterDamageGiven's and AfterDamageReceived's target).</summary>
    HitTarget,
}

/// <summary>One command of a hook's code, on the condition its code runs it on (null: always).</summary>
public abstract record HookStep
{
    public HookValue? When { get; init; }
}

/// <summary>CreatureCmd.GainBlock to the owner: its amount and props through the block pipeline (SimEngine.ProcBlock).</summary>
public sealed record GainBlockStep(HookValue Amount, HookValue Props) : HookStep;

/// <summary>CreatureCmd.Damage to <paramref name="Target"/> through the damage pipeline, from the owner unless NoDealer.</summary>
public sealed record DamageStep(HookTarget Target, HookValue Amount, HookValue Props) : HookStep
{
    /// <summary>No dealer (the dealer argument is null).</summary>
    public bool NoDealer { get; init; }
}

/// <summary>CreatureCmd.Heal of the owner: its amount, up to its max HP (no modifiers: the game's heal has none).</summary>
public sealed record HealStep(HookValue Amount) : HookStep;

/// <summary>CardPileCmd.Draw for the owner: its count of cards, each a card event.</summary>
public sealed record DrawStep(HookValue Count) : HookStep;

/// <summary>PlayerCmd.GainEnergy for the owner.</summary>
public sealed record GainEnergyStep(HookValue Amount) : HookStep;

/// <summary>
/// PlayerCmd.GainStars for the owner: one stars-gained event (the game's one AfterStarsGained), as a card's GainStars is (SimAdapter.Steps); the
/// simulator keeps no count of stars.
/// </summary>
public sealed record GainStarsStep(HookValue Amount) : HookStep;

/// <summary>PowerCmd.Apply of <paramref name="Power"/> on <paramref name="Target"/>, applied by the owner.</summary>
public sealed record ApplyPowerStep(string Power, HookTarget Target, HookValue Amount) : HookStep
{
    /// <summary>It lands as a debuff (Artifact takes it): the power's type for its amount, as the adapter read it.</summary>
    public bool Debuff { get; init; }

    /// <summary>
    /// It lands as a debuff when its amount, worked out as it runs, is below 0 (a power that can go below 0, its AllowNegative: Monologue's
    /// Strength taken back, -StrengthApplied; power.GetTypeForAmount).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DebuffWhenNegative { get; init; }
}

/// <summary>A store to one of the model's own fields or properties a program reads (this.AttacksPlayedThisTurn = ... + 1).</summary>
public sealed record StoreFieldStep(string Field, HookValue Value) : HookStep;

/// <summary>
/// A value bound to the play in progress, in the model's own data (amountsForPlayedCards.Add(cardPlay.Card, base.Amount) at BeforeCardPlayed):
/// what its AfterCardPlayed takes back (HookFact.TakeBound), so a power the play itself applies doesn't act on that play.
/// </summary>
public sealed record BindStep(HookValue Value) : HookStep;

/// <summary>What the model bound to the play in progress, taken (a Remove(cardPlay.Card, out amount) made for its out value: HookFact.BoundTaken).</summary>
public sealed record UnbindStep : HookStep;

/// <summary>A counter loop (for i &lt; Times): its commands, in order, Times times; a firing logged on the first round only (the game's one Flash).</summary>
public sealed record RepeatStep(HookValue Times, IReadOnlyList<HookStep> Steps) : HookStep;

/// <summary>OrbCmd.Channel&lt;T&gt; for the owner: one orb of the kind named (SimEngine.ChannelKind).</summary>
public sealed record ChannelStep(OrbKind Orb) : HookStep;

/// <summary>PowerCmd.Remove(this): the power itself goes from its owner.</summary>
public sealed record RemovePowerStep : HookStep;

/// <summary>PowerCmd.Decrement(this), PowerCmd.TickDownDuration(this): the power itself goes down by one, gone at 0.</summary>
public sealed record DecrementPowerStep : HookStep;

/// <summary>One hook of a model, as its code runs it: its commands in order, or for a Modify hook the value it returns.</summary>
public sealed record HookProgram(HookName Hook, IReadOnlyList<HookStep> Steps)
{
    /// <summary>A Modify hook's value (null for a hook that runs commands).</summary>
    public HookValue? Returns { get; init; }
}

/// <summary>
/// A tab judged on how the turn ends (SimModel.OutcomeTab), read from what a model's AfterDeath waits on (HookReader.OutcomeOf): its owner's death
/// (an enemy's: a warning on a play that can kill it), another death on its owner's side (an enemy's: a warning on a play that kills another
/// enemy), a death on the other side (a player's: a bonus on a play that kills an enemy in every outcome).
/// </summary>
public enum HookOutcome { KillsIt, KillsAnother, KillsOne }

/// <summary>Whether a model is a power (on a creature: its PowerSet entry) or a relic (on a player: its RelicSet entry).</summary>
public enum ModelKind { Power, Relic }

/// <summary>
/// A power or a relic as its code says (decision #195): its hooks' programs, read from the game's code by the adapter
/// (SimAdapter.HookSpecs), and its tab, from what its commands give. Pure data: the same in the game and in the tests (written as JSON).
/// </summary>
public sealed record ModelSpec(string Name, ModelKind Kind, IReadOnlyList<HookProgram> Hooks)
{
    /// <summary>Its tab, from its commands (block, damage, draws, energy; a trigger otherwise): the hand-written models' rule.</summary>
    public TabKind? Tab { get; init; }

    /// <summary>
    /// Its tab is crossed (a warning: the play helps someone else), when a play anyone makes, not only its owner, sets off something that helps
    /// its owner (block, a buff on itself): on an enemy, your play helping it (Enrage). Read from its programs (HookReader.HarmfulOf).
    /// </summary>
    public bool Harmful { get; init; }

    /// <summary>Its tab is judged on how the turn ends, by what its AfterDeath waits on (HookReader.OutcomeOf); it has no play tab then. Null: none.</summary>
    public HookOutcome? Outcome { get; init; }

    /// <summary>
    /// How its applications are kept (a power's InstanceType): each copy, with its own amount, its own state and its applier, runs its hooks on
    /// its own, in the order they landed (ReadModel). One: none of its own.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PowerCopies Copies { get; init; }

    /// <summary>
    /// Why its hooks weren't read, for a class whose hooks read whole at the reviewed builds and no longer do (a game update): no program, and
    /// every simulation it's in is unknown (SimState.Unmodelled, decision #92). Null: read.
    /// </summary>
    public string? Unread { get; init; }

    /// <summary>Every value its programs work out (each condition, amount and return, with the values inside them).</summary>
    public IEnumerable<HookValue> Values()
    {
        static IEnumerable<HookValue> Inside(HookValue? value) => value switch
        {
            null => Array.Empty<HookValue>(),
            HookMath math => new[] { value }.Concat(Inside(math.Left)).Concat(Inside(math.Right)),
            HookNot not => new[] { value }.Concat(Inside(not.Inner)),
            HookTest test => new[] { value }.Concat(Inside(test.Test)),
            HookWays ways => new[] { value }.Concat(ways.Ways.SelectMany(way => Inside(way.When).Concat(Inside(way.Value)))),
            _ => new[] { value },
        };
        static IEnumerable<HookValue> OfStep(HookStep step) => Inside(step.When).Concat(step switch
        {
            GainBlockStep gain => Inside(gain.Amount).Concat(Inside(gain.Props)),
            DamageStep damage => Inside(damage.Amount).Concat(Inside(damage.Props)),
            DrawStep draw => Inside(draw.Count),
            GainEnergyStep energy => Inside(energy.Amount),
            GainStarsStep stars => Inside(stars.Amount),
            HealStep heal => Inside(heal.Amount),
            ApplyPowerStep apply => Inside(apply.Amount),
            StoreFieldStep store => Inside(store.Value),
            BindStep bind => Inside(bind.Value),
            RepeatStep repeat => Inside(repeat.Times).Concat(repeat.Steps.SelectMany(OfStep)),
            _ => Array.Empty<HookValue>(),
        });
        return Hooks.SelectMany(program => Inside(program.Returns).Concat(program.Steps.SelectMany(OfStep)));
    }

    /// <summary>Every step of its programs, a counter loop's included.</summary>
    public IEnumerable<HookStep> Steps()
    {
        static IEnumerable<HookStep> All(HookStep step) => step is RepeatStep repeat ? new[] { step }.Concat(repeat.Steps.SelectMany(All)) : new[] { step };
        return Hooks.SelectMany(program => program.Steps).SelectMany(All);
    }

    /// <summary>Its code asks who applied it (base.Applier): it keeps its applier as its Data (DataCreature), read and landed so.</summary>
    public bool UsesApplier() => Values().Any(value => value is HookFactValue { Fact: HookFact.DeadIsApplier or HookFact.HasApplier or HookFact.ApplierIsPlayer or HookFact.PlayIsAppliers }
        || value is HookFactValue fact && (fact.Creature?.Who == HookWho.Applier || fact.Other?.Who == HookWho.Applier));

    /// <summary>Its own fields and properties its programs read or store (the adapter seeds them from the game: SimReaders.Fields).</summary>
    public IEnumerable<string> Fields() =>
        Values().OfType<HookField>().Select(field => field.Field).Concat(Steps().OfType<StoreFieldStep>().Select(store => store.Field))
            .Distinct(StringComparer.Ordinal);

    /// <summary>The program of a hook, or null when it doesn't override it.</summary>
    public HookProgram? Of(HookName hook)
    {
        foreach (var program in Hooks) if (program.Hook == hook) return program;
        return null;
    }
}

/// <summary>How hook programs are written as JSON (the simulator tests' read models: Cases/read-models.json).</summary>
public static class HookProgramJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver { Modifiers = { ByKind } },
    };

    private static void ByKind(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo)
    {
        var kinds = typeInfo.Type == typeof(HookValue) || typeInfo.Type == typeof(HookStep)
            ? typeof(HookValue).Assembly.GetTypes().Where(type => !type.IsAbstract && typeInfo.Type.IsAssignableFrom(type)).OrderBy(type => type.Name, StringComparer.Ordinal).ToList()
            : null;
        if (kinds == null) return;
        typeInfo.PolymorphismOptions = new System.Text.Json.Serialization.Metadata.JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$kind",
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
        };
        foreach (var kind in kinds) typeInfo.PolymorphismOptions.DerivedTypes.Add(new System.Text.Json.Serialization.Metadata.JsonDerivedType(kind, kind.Name));
    }

    public static string Write(IEnumerable<ModelSpec> specs) => System.Text.Json.JsonSerializer.Serialize(specs.OrderBy(spec => spec.Name, StringComparer.Ordinal).ToList(), Options);

    public static List<ModelSpec> Read(string json) => System.Text.Json.JsonSerializer.Deserialize<List<ModelSpec>>(json, Options) ?? new List<ModelSpec>();
}
