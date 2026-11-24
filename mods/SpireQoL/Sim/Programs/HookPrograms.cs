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
    AfterSideTurnEnd,
    ModifyDamageAdditive,
    ModifyDamageMultiplicative,
    ModifyDamageMultiplicativeLate,
    ModifyBlockAdditive,
    ModifyBlockMultiplicative,
}

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
    /// <summary>The damage or block comes from a card (cardSource != null).</summary>
    FromCard,
    /// <summary>The damage's or block's card is the owner's (cardSource.Owner.Creature == base.Owner).</summary>
    CardIsOwners,
    /// <summary>The owner takes part in the side's turn ending (participants.Contains(base.Owner)).</summary>
    TakesPart,
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
}

/// <summary>
/// One of the model's values (base.DynamicVars[Key]), as the adapter read it (SimEngine.Var); its canonical value (the class's CanonicalVars, read
/// from its code) when the state has none.
/// </summary>
public sealed record HookVar(string Key) : HookValue
{
    public decimal Canonical { get; init; }
}

/// <summary>Two values the code combines or compares.</summary>
public sealed record HookMath(HookOperation Operation, HookValue Left, HookValue Right) : HookValue;

/// <summary>Not: 1 when the value is 0.</summary>
public sealed record HookNot(HookValue Inner) : HookValue;

/// <summary>One way through a method to a value it returns: the value, when the way's test holds.</summary>
public sealed record HookWay(HookValue When, HookValue Value);

/// <summary>What a method returns: the value of the first of its ways whose test holds (each return of the code, in order).</summary>
public sealed record HookWays(IReadOnlyList<HookWay> Ways) : HookValue;

/// <summary>Who a hook's command acts on.</summary>
public enum HookTarget
{
    /// <summary>The model's owner (base.Owner, base.Owner.Creature, base.Owner.Player).</summary>
    Owner,
    /// <summary>Every enemy that can be hit (CombatState.HittableEnemies).</summary>
    AllEnemies,
    /// <summary>A random enemy that can be hit (a roll over HittableEnemies): a branch per enemy, never the roll.</summary>
    RandomEnemy,
    /// <summary>The creature the power landing landed on (power.Owner in AfterPowerAmountChanged).</summary>
    PowerOwner,
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

/// <summary>CardPileCmd.Draw for the owner: its count of cards, each a card event.</summary>
public sealed record DrawStep(HookValue Count) : HookStep;

/// <summary>PlayerCmd.GainEnergy for the owner.</summary>
public sealed record GainEnergyStep(HookValue Amount) : HookStep;

/// <summary>PowerCmd.Apply of <paramref name="Power"/> on <paramref name="Target"/>, applied by the owner.</summary>
public sealed record ApplyPowerStep(string Power, HookTarget Target, HookValue Amount) : HookStep
{
    /// <summary>It lands as a debuff (Artifact takes it): the power's type for its amount, as the adapter read it.</summary>
    public bool Debuff { get; init; }
}

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

    /// <summary>Every value its programs work out (each condition, amount and return, with the values inside them).</summary>
    public IEnumerable<HookValue> Values()
    {
        static IEnumerable<HookValue> Inside(HookValue? value) => value switch
        {
            null => Array.Empty<HookValue>(),
            HookMath math => new[] { value }.Concat(Inside(math.Left)).Concat(Inside(math.Right)),
            HookNot not => new[] { value }.Concat(Inside(not.Inner)),
            HookWays ways => new[] { value }.Concat(ways.Ways.SelectMany(way => Inside(way.When).Concat(Inside(way.Value)))),
            _ => new[] { value },
        };
        static IEnumerable<HookValue> OfStep(HookStep step) => Inside(step.When).Concat(step switch
        {
            GainBlockStep gain => Inside(gain.Amount).Concat(Inside(gain.Props)),
            DamageStep damage => Inside(damage.Amount).Concat(Inside(damage.Props)),
            DrawStep draw => Inside(draw.Count),
            GainEnergyStep energy => Inside(energy.Amount),
            ApplyPowerStep apply => Inside(apply.Amount),
            _ => Array.Empty<HookValue>(),
        });
        return Hooks.SelectMany(program => Inside(program.Returns).Concat(program.Steps.SelectMany(OfStep)));
    }

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
