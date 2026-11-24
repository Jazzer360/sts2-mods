using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>What a simulator condition tests (decision #190): each one a test the game's code makes, asked of the simulator's own state.</summary>
public enum SimTest
{
    /// <summary>The play's last attack killed a creature it hit (its DamageResults' WasTargetKilled: Sunder, Knockout Blow, Timber).</summary>
    AttackKilled,
    /// <summary>The play owner's Osty is missing or dead (Osty.CheckMissingWithAnim; IsOstyAlive is its negation).</summary>
    OstyMissing,
    /// <summary>The play's target is impaled by anyone's arrow, or by the play owner's (ArrowCmd.CreatureIsImpaledByArrow, ...ByPlayersArrow).</summary>
    TargetImpaled,
    /// <summary>A count on the state compared with a number (your hand's cards, the ones a filter lets through, plays finished this turn).</summary>
    Count,
    /// <summary>Another condition doesn't hold.</summary>
    Not,
    /// <summary>Every one of several conditions holds.</summary>
    All,
    /// <summary>At least one of several conditions holds.</summary>
    Any,
}

/// <summary>How a counted condition compares its count with its number.</summary>
public enum SimCompare { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }

/// <summary>What a counted condition counts, on the state when it's asked.</summary>
public enum SimCounted
{
    /// <summary>The cards in the play owner's hand (the played card has left it: it's in play while its OnPlay runs).</summary>
    HandCards,
    /// <summary>
    /// Of those, the ones a filter of the card's lets through (Impatience's Attacks): set as the card play starts and followed as cards leave
    /// and join the hand (SimState.TakeMatching, as a filtered take's are); not known once a card joined whose kind wasn't read.
    /// </summary>
    HandMatching,
    /// <summary>The card plays the play owner finished this turn, any card (their turn counts: FTL's CanDrawCard).</summary>
    PlaysFinishedThisTurn,
    /// <summary>This card's own plays finished this turn: the ones before the card play, then each play of a replay (Fetch's HasBeenPlayedThisTurn).</summary>
    CardPlaysFinished,
}

/// <summary>
/// A condition the simulator asks of its own state when the action it guards runs (decision #190: conditions apart from effects): any
/// condition can guard any effect (WhenAction), so a card's combination of the two needs no code of its own. The adapter translates the
/// card's code's tests (ProgramCondition) into these, each translated once for every card.
/// </summary>
public abstract record SimCondition(SimTest Test)
{
    /// <summary>What it tests.</summary>
    [JsonIgnore] public SimTest Test { get; } = Test;

    /// <summary>
    /// Where the card's code asks it (its branch: ConditionLiteral.At), when it's one of the code's tests: asked there once a play, and
    /// that answer kept for the rest of the play (a later step under the same if, its else), as the code asks it once. Null: asked each time.
    /// </summary>
    public string? At { get; init; }
}

/// <summary>The play's last attack killed a creature it hit (DamageResult.WasTargetKilled over the attack's results).</summary>
public sealed record AttackKilledCondition() : SimCondition(SimTest.AttackKilled);

/// <summary>The play owner's Osty is missing or dead (Osty.CheckMissingWithAnim).</summary>
public sealed record OstyMissingCondition() : SimCondition(SimTest.OstyMissing);

/// <summary>The play's target is impaled: by anyone's arrow, or with <paramref name="ByOwner"/> by the play owner's.</summary>
public sealed record TargetImpaledCondition(bool ByOwner) : SimCondition(SimTest.TargetImpaled);

/// <summary>A count on the state (<paramref name="Counted"/>) compared with <paramref name="Than"/>.</summary>
public sealed record CountCondition(SimCounted Counted, SimCompare Compare, int Than) : SimCondition(SimTest.Count)
{
    /// <summary>HandMatching: the filter's name (the card and its test), followed through the card play in SimState.TakeMatching.</summary>
    public string? Filter { get; init; }

    /// <summary>HandMatching: the cards in the hand the filter lets through as the card play starts (the adapter's count).</summary>
    public int Matching { get; init; }

    /// <summary>CardPlaysFinished: this card's plays finished this turn before the card play (the adapter's count from the game's history).</summary>
    public int Before { get; init; }
}

/// <summary>The condition doesn't hold.</summary>
public sealed record NotCondition(SimCondition Inner) : SimCondition(SimTest.Not);

/// <summary>Every one of <paramref name="Parts"/> holds.</summary>
public sealed record AllCondition(IReadOnlyList<SimCondition> Parts) : SimCondition(SimTest.All);

/// <summary>At least one of <paramref name="Parts"/> holds.</summary>
public sealed record AnyCondition(IReadOnlyList<SimCondition> Parts) : SimCondition(SimTest.Any);

/// <summary>
/// The actions <paramref name="Then"/> run on the branches where <paramref name="Condition"/> holds, asked as this action runs (decision
/// #190): any effect under any condition (Sunder's energy, Timber's attack on every enemy, Impatience's draws, Sacrifice's block).
/// </summary>
public sealed record WhenAction(SimCondition Condition, IReadOnlyList<SimAction> Then) : SimAction;

/// <summary>
/// Asks a condition now and keeps its answer for the rest of the play (a local the card's code stores before later steps: Ki Hop's and Bow
/// Smack's wasImpaled, taken before their attack). Its tests need an At to be kept.
/// </summary>
public sealed record AskAction(SimCondition Condition) : SimAction;
