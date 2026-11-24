using SpireQoL.Sim;

namespace SpireQoL.Procs.Programs;

/// <summary>What a question to the game's log of the fight is, for the simulator (HistoryQueryReader.Read).</summary>
/// <param name="Code">The question as the code asks it (its last Enumerable call: Count, Any...).</param>
public sealed record HistoryReading(QueryValue Code)
{
    /// <summary>The query the history ledger counts (decision #191); null for a gap.</summary>
    public HistoryQuery? Query { get; init; }

    /// <summary>It asks whether there's any (Any): its count is above 0. Otherwise it's the count (Count).</summary>
    public bool AsksAny { get; init; }

    /// <summary>The kind of gap (HistoryQueryReader.GapKinds), when it isn't a query the ledger counts; null otherwise.</summary>
    public string? Gap { get; init; }

    /// <summary>What it tests that the ledger doesn't (a gap's detail).</summary>
    public string Why { get; init; } = "";
}

/// <summary>
/// The game's log of the fight as the code asks it (decision #191: the history ledger). Every question a card, relic or power asks of
/// CombatManager.Instance.History is an entry kind (CardPlaysStarted, OfType&lt;CardExhaustedEntry&gt;...), the code's own tests of each
/// entry (its lambda: HappenedThisTurn, whose it is, the card's type or tag, the power's class, the detail flags), then Count or Any.
/// Each is translated here, once, for every model that asks it (never per card): a HistoryQuery the ledger counts, or a gap of a reviewed
/// kind (GapKinds), each saying what reads it instead. A test of a shape this doesn't know is a gap of no reviewed kind, and the catalog
/// scan (HistoryScanTests) fails on it.
/// </summary>
public static class HistoryQueryReader
{
    /// <summary>The kinds of question the ledger doesn't count, and what reads them instead (reviewed).</summary>
    public static readonly IReadOnlyDictionary<string, string> GapKinds = new Dictionary<string, string>
    {
        ["the last turn"] = "HappenedLastPlayerTurn: asked as the player's next turn starts (Bolas, Bounce, Thrumming Hatchet, Emotion Chip, History Course), outside the simulated turn; each hook is reviewed in the CoverageLedger",
        ["this card's plays"] = "a test of the card itself (e.CardPlay.Card == this): Fetch's is SimCounted.CardPlaysFinished (this card's plays before the card play, then each play of a replay); Helix Drill's energy is read before the play",
        ["one card play"] = "a test of one card play (e.CardPlay == cardPlay or !=): Unmovable's block counts the earlier plays' gains apart from its own play's (SimKeys.CardBlock less PlayCardBlockGains); Helix Drill's energy is read before the play",
        ["the target's"] = "the damage the card's target took (e.Receiver == target: Gang Up, Beat Into Shape): a calculated value on the target, worked out by the adapter for each target before the play",
        ["an affliction"] = "cards afflicted (CardAfflictedEntry: Chains of Binding): the simulator doesn't afflict cards; the power's hook is reviewed in the CoverageLedger",
        ["not a count"] = "a value that isn't a count of entries (Sum of an amount, LastOrDefault: Helix Drill's energy spent, Radiate's stars, History Course's card): read before the play",
        ["more than one way"] = "a test with more than one way through it (Nostalgia's Attack or Skill): its model reads its own counters (SimKeys.Plays by type)",
    };

    /// <summary>The kinds of entry, by their class name.</summary>
    private static readonly Dictionary<string, HistoryKind> EntryKinds = Enum.GetValues<HistoryKind>().ToDictionary(kind => kind + "Entry");

    /// <summary>
    /// The question <paramref name="code"/> asks (an Enumerable query over the log), translated. <paramref name="enumName"/>: an enum's
    /// member name by its type and value, from the code's own branch (CardType, CardTag); null: the loaded game's.
    /// </summary>
    public static HistoryReading Read(QueryValue code, Func<string, long, string?>? enumName = null)
    {
        enumName ??= LoadedEnumName;
        HistoryReading Gap(string kind, string why) => new(code) { Gap = kind, Why = why };
        if (code.Name is not ("Count" or "Any")) return Gap("not a count", code.Name);
        if (code.Arguments.Count == 0) return Gap("unknown", "no source");
        if (Source(code.Arguments[0]) is not { } kind) return Gap("unknown", "source " + code.Arguments[0]);
        var lambda = code.Arguments.Count > 1 ? code.Arguments[1] as LambdaValue : null;
        if (code.Arguments.Count > 1 && lambda == null) return Gap("unknown", "a predicate that isn't a lambda");
        var query = new HistoryQuery(kind, HistoryScope.ThisFight, HistoryWhose.Anyone);
        if (kind == HistoryKind.CardAfflicted) return Gap("an affliction", code.ToString());
        if (lambda != null)
        {
            if (lambda.Body.Terms.Count != 1) return Gap("more than one way", lambda.ToString());
            // Every test read; a reviewed kind of gap is named before an unknown test (Emotion Chip: the last turn, whatever else it tests).
            string? unknown = null;
            foreach (var literal in lambda.Body.Terms[0])
            {
                var (next, gap, why) = Test(query, literal, lambda.Parameter, enumName);
                if (gap == "unknown") unknown ??= why;
                else if (gap != null) return Gap(gap, why);
                else query = next;
            }
            if (unknown != null) return Gap("unknown", unknown);
        }
        return new HistoryReading(code) { Query = query, AsksAny = code.Name == "Any" };
    }

    /// <summary>The entries the source names: CardPlaysStarted, CardPlaysFinished, or Entries.OfType&lt;XEntry&gt;.</summary>
    private static HistoryKind? Source(ProgramValue source) => source switch
    {
        QueryValue { Name: "CardPlaysStarted" } plays when CardProgramReader.ReadsHistory(plays) => HistoryKind.CardPlayStarted,
        QueryValue { Name: "CardPlaysFinished" } plays when CardProgramReader.ReadsHistory(plays) => HistoryKind.CardPlayFinished,
        QueryValue { Name: "OfType", GenericArguments: [var entry], Arguments: [QueryValue { Name: "Entries" } entries] } when CardProgramReader.ReadsHistory(entries)
            => EntryKinds.TryGetValue(entry, out var kind) ? kind : null,
        _ => null,
    };

    /// <summary>One test of the lambda's, on the query so far: the query with it, or the gap it makes.</summary>
    private static (HistoryQuery Query, string? Gap, string Why) Test(HistoryQuery query, ConditionLiteral literal, string entry, Func<string, long, string?> enumName)
    {
        (HistoryQuery, string?, string) Unknown() => (query, "unknown", literal.ToString());
        (HistoryQuery, string?, string) With(HistoryQuery changed) => (changed, null, "");
        var test = literal.Test;
        // A true-or-false the code worked out on several ways (Nostalgia: an Attack or a Skill): more than one way.
        if (test is ConditionValue) return (query, "more than one way", literal.ToString());
        // A true-or-false compared with 0 (e.CardPlay.IsAutoPlay == 0): the test, the other way.
        if (test is CompareValue { Op: Comparison.Equal or Comparison.NotEqual, Right: ConstantValue { Value: 0 }, Left: QueryValue { ValueType: "Boolean" } flag })
            return Test(query, literal with { Test = flag, Holds = literal.Holds != (((CompareValue)test).Op == Comparison.Equal) }, entry, enumName);
        // A class test as a true-or-false (e.Power is DoomPower): the same as is T != null.
        if (test is QueryValue { Name: "is" } isTest && literal.Holds) test = new CompareValue(Comparison.NotEqual, isTest, NullValue.Instance);
        // A comparison that doesn't hold is the opposite one that does (the compiler's !(a == b)).
        if (!literal.Holds && test is CompareValue negated) return Test(query, literal with { Test = negated with { Op = CompareValue.Negated(negated.Op) }, Holds = true }, entry, enumName);
        var path = test is QueryValue tested ? PathOf(tested, entry) : null;
        // When it happened.
        if (path is ["HappenedThisTurn"] && literal.Holds) return With(query with { Scope = HistoryScope.ThisTurn });
        if (path is ["HappenedLastPlayerTurn"]) return (query, "the last turn", literal.ToString());
        // A test that part of the entry is there (e.CardPlay, e.Dealer: not null): it holds when a later test of it does.
        if (path is { Count: > 0 } && literal.Holds && test is QueryValue { ValueType: not ("Boolean" or "Int32" or "Int64" or "Decimal" or "Single" or "Double") }) return With(query);
        // A true-or-false of the entry: the detail flags.
        if (path is ["CardPlay", "IsFirstInSeries"] && literal.Holds) return With(query with { Details = query.Details | HistoryDetail.FirstInSeries });
        if (path is ["WasEthereal"] && literal.Holds) return With(query with { Details = query.Details | HistoryDetail.Ethereal });
        if (path is ["CardPlay", "IsAutoPlay"] && !literal.Holds) return With(query with { Details = query.Details | HistoryDetail.NotAutoPlay });
        if (path is ["FromHandDraw"] && !literal.Holds) return With(query with { Details = query.Details | HistoryDetail.NotHandDraw });
        if (path is ["CardPlay", "Resources", "EnergyValue"] && !literal.Holds) return With(query with { Details = query.Details | HistoryDetail.ZeroEnergy });
        if (test is QueryValue { Name: "IsPoweredAttack", Arguments: [QueryValue props] } && PathOf(props, entry) is ["Result", "Props"] && literal.Holds)
            return With(query with { Details = query.Details | HistoryDetail.PoweredAttack });
        // A card's tag (e.CardPlay.Card.Tags.Contains(CardTag.Shiv)).
        if (test is QueryValue { Name: "Contains", Arguments: [QueryValue tags, ConstantValue tag] } && PathOf(tags, entry) is ["CardPlay", "Card", "Tags"] or ["Card", "Tags"] && literal.Holds)
            return enumName("CardTag", tag.Value) is { } tagName && Enum.TryParse<SimCardTag>(tagName, out var simTag) && query.CardTag == null
                ? With(query with { CardTag = simTag }) : Unknown();
        if (test is not CompareValue compare || !literal.Holds) return Unknown();
        var left = compare.Left is QueryValue leftQuery ? PathOf(leftQuery, entry) : null;
        // x is T != null: the class of the entry's power, card or orb.
        if (compare is { Op: Comparison.NotEqual, Right: NullValue, Left: QueryValue { Name: "is", Of: QueryValue isOf } classTest } && PathOf(isOf, entry) is { } classOf)
        {
            switch (classOf)
            {
                case ["Power"]: return With(query with { Power = PowerId.Of(classTest.Type) });
                case ["Card"]: return With(query with { Card = CardId.Of(classTest.Type) });
                case ["Orb"]: return Enum.TryParse<OrbKind>(classTest.Type.Replace("Orb", "", StringComparison.Ordinal), out var orb) ? With(query with { Orb = orb }) : Unknown();
                case ["Affliction"]: return (query, "an affliction", literal.ToString());
            }
            return Unknown();
        }
        // A null test of part of the entry that a later test reads (e.CardPlay != null before e.CardPlay.Player): it holds when that does.
        if (compare is { Op: Comparison.NotEqual, Right: NullValue } && left != null && left.Count > 0) return With(query);
        if (left is null) return Unknown();
        switch (compare.Op, left)
        {
            case (Comparison.Equal, ["CardPlay", "Card", "Type"] or ["Card", "Type"]):
                return compare.Right is ConstantValue type && enumName("CardType", type.Value) is { } typeName && Enum.TryParse<SimCardType>(typeName, out var simType)
                    ? With(query with { CardType = simType }) : Unknown();
            case (Comparison.Greater, ["Result", "UnblockedDamage"]) when compare.Right is ConstantValue { Value: 0 }:
                return With(query with { Details = query.Details | HistoryDetail.Unblocked });
            case (Comparison.Equal or Comparison.NotEqual, ["CardPlay", "Card"]):
                return (query, "this card's plays", literal.ToString());
            case (Comparison.Equal or Comparison.NotEqual, ["CardPlay"]):
                return (query, "one card play", literal.ToString());
            case (Comparison.Equal, ["Receiver"]) when compare.Right is ParameterValue { Name: "target" } or NamedValue { Name: "target" }:
                return (query, "the target's", literal.ToString());
            case (Comparison.Equal, _) when Whose(left, query.Kind) && OwnerOf(compare.Right) is { } whose:
                return query.Whose == HistoryWhose.Anyone || query.Whose == whose ? With(query with { Whose = whose }) : Unknown();
        }
        return Unknown();
    }

    /// <summary>
    /// The path of properties from the entry (e.CardPlay.Card.Type: CardPlay, Card, Type), a method of it last (e.HappenedThisTurn(x):
    /// HappenedThisTurn); null when the value isn't asked of the entry.
    /// </summary>
    private static List<string>? PathOf(QueryValue value, string entry)
    {
        var path = new List<string>();
        ProgramValue? current = value;
        while (current is QueryValue or StoredValue)
        {
            if (current is StoredValue stored)
            {
                current = stored.Value; // a struct kept in a local to call its getter (e.CardPlay.Resources)
                continue;
            }
            var query = (QueryValue)current;
            if (query.Name == "is") return null;
            path.Insert(0, query.Name);
            current = query.Of;
        }
        return current is ParameterValue parameter && parameter.Name == entry ? path : null;
    }

    /// <summary>The entry's own creature or player, as the code tests it for each kind (whose it is).</summary>
    private static bool Whose(List<string> path, HistoryKind kind) => path switch
    {
        ["Actor"] or ["Actor", "Player"] => kind != HistoryKind.CardGenerated,
        ["CardPlay", "Player"] or ["CardPlay", "Player", "Creature"] => kind is HistoryKind.CardPlayStarted or HistoryKind.CardPlayFinished or HistoryKind.BlockGained,
        ["Creator"] => kind == HistoryKind.CardGenerated,
        ["Applier"] => kind == HistoryKind.PowerReceived,
        ["Receiver"] => kind is HistoryKind.DamageReceived or HistoryKind.BlockGained,
        _ => false,
    };

    /// <summary>
    /// Whose a value is: the owner (the card's, relic's or power's Owner, its Creature or Player; a calculated value's card's), or the owner's
    /// Osty; null for anything else.
    /// </summary>
    private static HistoryWhose? OwnerOf(ProgramValue value)
    {
        if (value is QueryValue { Name: "Osty", Of: { } ostyOwner } && IsOwner(ostyOwner)) return HistoryWhose.OwnersOsty;
        if (IsOwner(value)) return HistoryWhose.Owner;
        if (value is QueryValue { Name: "Creature" or "Player", Of: { } of } && IsOwner(of)) return HistoryWhose.Owner;
        return null;
    }

    /// <summary>The Owner of the model asking: its own (this), or a calculated value's card (its lambda's parameter, or that captured).</summary>
    private static bool IsOwner(ProgramValue value) => value is QueryValue { Name: "Owner", Of: NamedValue { Name: not "closure" } or ParameterValue };

    /// <summary>An enum member's name in the loaded game (CardType 1: Attack).</summary>
    public static string? LoadedEnumName(string enumType, long value)
    {
        var type = LoadedEnums.GetOrAdd(enumType, name => typeof(MegaCrit.Sts2.Core.Models.CardModel).Assembly.GetTypes()
            .FirstOrDefault(candidate => candidate.IsEnum && candidate.Name == name && candidate.Namespace?.StartsWith("MegaCrit", StringComparison.Ordinal) == true));
        return type == null ? null : Enum.GetName(type, Enum.ToObject(type, value));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Type?> LoadedEnums = new();
}
