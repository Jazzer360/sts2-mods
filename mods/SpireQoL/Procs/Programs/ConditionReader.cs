using System.Reflection;
using SpireQoL.Sim;

namespace SpireQoL.Procs.Programs;

/// <summary>What a step's condition becomes for the simulator (ConditionReader.Read).</summary>
public enum ConditionWay
{
    /// <summary>No condition: the step runs on every way through the play.</summary>
    Always,
    /// <summary>A simulator condition (SimCondition), asked of each branch's state when the step's action runs.</summary>
    Simulated,
    /// <summary>Decided before the play (Holds), nothing in the play can change it (the card's own values, its upgrade, a pick: decision #188).</summary>
    Decided,
    /// <summary>A test with no simulator translation yet (Reason): decided on the board before the play, a gap the card scan lists.</summary>
    NotRead,
}

/// <summary>A step's condition as the simulator takes it.</summary>
public sealed record ConditionReading(ConditionWay Way)
{
    /// <summary>Simulated: the condition.</summary>
    public SimCondition? Condition { get; init; }

    /// <summary>Decided: whether it holds on the board (null: no board, or the board can't tell: it runs, as the evaluator's practice).</summary>
    public bool? Holds { get; init; }

    /// <summary>Decided or NotRead: why.</summary>
    public string Reason { get; init; } = "";

    /// <summary>Tests the code stores in a local before the play's first command (Ki Hop's wasImpaled): asked at the play's start (AskAction).</summary>
    public IReadOnlyList<SimCondition> AskFirst { get; init; } = Array.Empty<SimCondition>();
}

/// <summary>
/// The card code's tests translated into simulator conditions (decision #190: conditions apart from effects). Each test the game's code
/// makes is translated here once, for every card that makes it, never per card:
/// <list type="bullet">
/// <item>an attack's results' WasTargetKilled: the play's last attack killed (AttackKilledCondition);</item>
/// <item>Osty.CheckMissingWithAnim(Owner), Owner.IsOstyAlive: Osty missing;</item>
/// <item>ArrowCmd.CreatureIsImpaledByArrow(target), ...ByPlayersArrow(target, Owner): the target impaled;</item>
/// <item>your hand's cards (a count, Any, Except the card itself), the ones a lambda lets through (Any, Count: a filter followed through the
/// play), and CardPlaysFinished this turn by you or of this card (a count, Any), each compared with a number the board gives;</item>
/// <item>a property of the card read from its getter's code (Ftl.CanDrawCard, Restlessness.IsOnlyCardInHand), translated the same way;</item>
/// <item>the card's own values (a card value, a constant, its upgrade, an auto-property: Mad Science's rider) and a pick's card (decision
/// #188): decided before the play.</item>
/// </list>
/// Anything else is NotRead, with the test named: decided on the board as before, and listed by the card scan as a gap.
/// </summary>
public static class ConditionReader
{
    /// <summary>
    /// Where a condition is read: the card's type, how to read a getter's code, and the board (null: no board, the catalog's read of the
    /// code alone) with the card's program.
    /// </summary>
    public sealed record Context(Type CardType, Func<MethodBase, List<CodeOp>> ReadCode, ProgramBoard? Board, CardProgram Program);

    /// <summary>The step's condition (its own and every loop's around it) for the simulator.</summary>
    public static ConditionReading Read(ProgramCondition condition, Context context)
    {
        if (condition.IsAlways) return new ConditionReading(ConditionWay.Always);
        if (condition.IsNever) return new ConditionReading(ConditionWay.Decided) { Holds = false, Reason = "no way through the code reaches it" };
        var ways = new List<SimCondition>();
        var decidedReasons = new List<string>();
        var askFirst = new List<SimCondition>();
        bool? anyDecided = false;
        foreach (var term in condition.Terms)
        {
            var parts = new List<SimCondition>();
            bool? termHolds = true;
            foreach (var literal in term)
            {
                var reading = Literal(literal, context);
                switch (reading.Way)
                {
                    case ConditionWay.NotRead:
                        return reading;
                    case ConditionWay.Decided:
                        decidedReasons.Add(reading.Reason);
                        if (reading.Holds == false) termHolds = false;
                        else if (reading.Holds == null && termHolds == true) termHolds = null;
                        break;
                    case ConditionWay.Simulated:
                        parts.Add(reading.Condition!);
                        askFirst.AddRange(reading.AskFirst);
                        break;
                }
            }
            if (termHolds == false) continue; // a way the card's own values close
            if (parts.Count == 0)
            {
                // A way open on the card's own values alone: the step runs whenever that way's decided tests hold.
                if (termHolds == true) return new ConditionReading(ConditionWay.Decided) { Holds = true, Reason = string.Join("; ", decidedReasons.Distinct()) };
                anyDecided = null;
                continue;
            }
            ways.Add(parts.Count == 1 ? parts[0] : new AllCondition(parts));
        }
        if (ways.Count == 0)
            return new ConditionReading(ConditionWay.Decided) { Holds = anyDecided, Reason = string.Join("; ", decidedReasons.Distinct()) };
        if (anyDecided == null) return new ConditionReading(ConditionWay.NotRead) { Reason = "a way the board can't decide beside a simulated one" };
        return new ConditionReading(ConditionWay.Simulated) { Condition = ways.Count == 1 ? ways[0] : new AnyCondition(ways), AskFirst = askFirst };
    }

    /// <summary>A filter of the cards in your hand a step's condition asks about (Impatience's Attacks): its name and lambda.</summary>
    public sealed record HandFilter(string Key, LambdaValue Lambda);

    /// <summary>
    /// The filters of your hand's cards the card's conditions ask about (Any or Count of your hand's cards with a lambda), each named by
    /// the card and its test (SimCondition's Filter): what a card joining your hand during the play counts for (SimDrawCard.TakenBy).
    /// </summary>
    public static IReadOnlyList<HandFilter> HandFiltersOf(Type cardType, CardProgram program)
    {
        var filters = new List<HandFilter>();
        foreach (var step in program.All())
        {
            if (step.Condition == null) continue;
            foreach (var literal in step.Condition.Literals)
                if (HandQuery(Unstored(literal.Test)) is { Lambda: { } lambda } && filters.All(filter => filter.Key != FilterKey(cardType, literal.At)))
                    filters.Add(new HandFilter(FilterKey(cardType, literal.At), lambda));
        }
        return filters;
    }

    private static string FilterKey(Type cardType, string at) => SimEngine.ConditionFilterPrefix + cardType.Name + "#" + at;

    // ---- one test

    private static ConditionReading Literal(ConditionLiteral literal, Context context)
    {
        var test = literal.Test;
        var stored = test is StoredValue;
        var storedAt = test is StoredValue storedValue ? storedValue.Offset : 0;
        test = Unstored(test);
        // Its test as the code makes it, its direction apart: the engine keeps one answer per test (its At), whichever way each step asks it.
        var positive = literal.Holds;
        var reading = Test(test, context, literal.At, depth: 0);
        if (reading.Way == ConditionWay.Decided && !positive) reading = reading with { Holds = reading.Holds is { } holds ? !holds : null };
        if (reading.Way != ConditionWay.Simulated) return reading;
        var condition = positive ? reading.Condition! : new NotCondition(reading.Condition!);
        if (!stored) return reading with { Condition = condition };
        // A local the code stores, read later: asked where it's stored. Only before the play's first command can that be placed.
        if (context.Program.Commands.Any(command => command.Offset < storedAt))
            return new ConditionReading(ConditionWay.NotRead) { Reason = $"a test stored after the play's first command ({literal.Test})" };
        return reading with { Condition = condition, AskFirst = reading.AskFirst.Append(reading.Condition!).ToList() };
    }

    /// <summary>A value's test, positive (a literal's direction is applied by the caller), its At kept for the engine's once-a-play asking.</summary>
    private static ConditionReading Test(ProgramValue test, Context context, string? at, int depth)
    {
        ConditionReading Simulated(SimCondition condition) => new(ConditionWay.Simulated) { Condition = condition with { At = at } };
        ConditionReading NotRead(string why) => new(ConditionWay.NotRead) { Reason = why };

        // The attack's kill: Any over the results of AttackCommand.Execute of WasTargetKilled (Sunder, Knockout Blow, Timber).
        if (test is QueryValue { Name: "Any" } any && any.Parts().Any(part => part is QueryValue { Name: "WasTargetKilled" })
            && any.Parts().Any(part => part is ResultValue result && result.Command.StartsWith("AttackCommand.", StringComparison.Ordinal)))
            return Simulated(new AttackKilledCondition());
        // Osty: missing (CheckMissingWithAnim of the card's owner), alive (its owner's IsOstyAlive).
        if (test is QueryValue { Name: "CheckMissingWithAnim", Of: null, Arguments: [var player] } && IsOwner(player))
            return Simulated(new OstyMissingCondition());
        if (test is QueryValue { Name: "IsOstyAlive", IsProperty: true, Of: var ostyOwner } && ostyOwner != null && IsOwner(ostyOwner))
            return Simulated(new NotCondition(new OstyMissingCondition()));
        // Arrows in the target: anyone's, or the card owner's.
        if (test is QueryValue { Name: "CreatureIsImpaledByArrow", Arguments: [var impaled] } && IsTarget(impaled))
            return Simulated(new TargetImpaledCondition(ByOwner: false));
        if (test is QueryValue { Name: "CreatureIsImpaledByPlayersArrow", Arguments: [var impaledByYou, var arrowOwner] } && IsTarget(impaledByYou) && IsOwner(arrowOwner))
            return Simulated(new TargetImpaledCondition(ByOwner: true));
        // A true-or-false the code worked out from tests (a local set on several ways): those tests.
        if (test is ConditionValue flag) return Nested(flag.Condition, context, at, depth);
        // A true-or-false compared with 0 or 1 (Restlessness' Any(...) == 0, the compiler's not): the test itself, or its negation.
        if (test is CompareValue { Op: Comparison.Equal or Comparison.NotEqual, Right: ConstantValue { Value: 0 or 1 } truth } truthTest
            && Unstored(truthTest.Left) is QueryValue { Name: "Any" or "All" } or QueryValue { ValueType: "Boolean" } && !IsCount(Unstored(truthTest.Left)))
        {
            var inner = Test(Unstored(truthTest.Left), context, at, depth);
            var negated = (truthTest.Op == Comparison.Equal) == (truth.Value == 0);
            if (!negated) return inner;
            return inner.Way switch
            {
                ConditionWay.Simulated => inner with { Condition = new NotCondition(inner.Condition! with { At = null }) { At = at } },
                ConditionWay.Decided => inner with { Holds = inner.Holds is { } holds ? !holds : null },
                _ => inner,
            };
        }
        // A count, or a test that there's any: of your hand, or of the plays finished this turn.
        if (CountedTest(test, context, at) is { } counted) return counted;
        // A property of the card: its upgrade, an auto-property (set when it's made: Mad Science's rider), or a getter read from its code.
        if (test is QueryValue { IsProperty: true, Of: NamedValue { Name: "this" }, Name: var property })
        {
            if (property == "IsUpgraded" || IsStoredOnCard(context, property)) return Decided(test, context, "the card's own value");
            if (depth < 2 && CardProgramReader.ReadProperty(context.CardType, property, context.ReadCode) is { } getter) return Nested(getter, context, at, depth + 1);
            return NotRead($"the card's {property}, a getter its code doesn't show");
        }
        // A pick's card (decision #188: a pick is a card, never a particular one).
        if (test.Parts().Any(part => part is ResultValue result && result.Command.StartsWith("CardSelectCmd.", StringComparison.Ordinal)))
            return Decided(test, context, "a pick is a card (decision #188)", holds: true);
        if (IsCardsOwn(test, context)) return Decided(test, context, "the card's own value");
        return NotRead(test.ToString()!);
    }

    /// <summary>
    /// A getter's or a stored flag's condition, translated whole and asked as one test where its caller asks it (its caller's At keeps the
    /// answer; its own tests are asked each time it is). A filter of your hand inside one isn't followed (its name is its caller's test).
    /// </summary>
    private static ConditionReading Nested(ProgramCondition condition, Context context, string? at, int depth)
    {
        var inner = Read(condition, context);
        if (inner.Way != ConditionWay.Simulated) return inner;
        if (inner.AskFirst.Count > 0 || SimEngine.ConditionParts(inner.Condition!).Any(part => part is CountCondition { Counted: SimCounted.HandMatching }))
            return new ConditionReading(ConditionWay.NotRead) { Reason = "a getter's own stored test or filter of your hand" };
        return inner with { Condition = Unplaced(inner.Condition!) with { At = at } };
    }

    /// <summary>The condition with no test's place kept (each asked whenever it's asked).</summary>
    private static SimCondition Unplaced(SimCondition condition) => condition switch
    {
        NotCondition not => not with { Inner = Unplaced(not.Inner), At = null },
        AllCondition all => all with { Parts = all.Parts.Select(Unplaced).ToList(), At = null },
        AnyCondition any => any with { Parts = any.Parts.Select(Unplaced).ToList(), At = null },
        _ => condition with { At = null },
    };


    // ---- counts

    /// <summary>What a count or an Any tests, as a CountCondition; null when it isn't one of the counts the simulator reads.</summary>
    private static ConditionReading? CountedTest(ProgramValue test, Context context, string? at)
    {
        // Any(...): the count is above 0.
        if (test is QueryValue { Type: "Enumerable", Name: "Any" } anyQuery)
            return Count(anyQuery, SimCompare.Greater, than: 0, context, at);
        if (test is CompareValue { Left: var left, Right: var right } compare && Unstored(left) is { } counted && IsCount(counted)
            && Number(right, context) is var number && (number != null || context.Board == null) && IsCardsOwn(right, context))
            return Count(counted, Compared(compare.Op), number ?? 0, context, at);
        return null;
    }

    private static bool IsCount(ProgramValue value) =>
        value is QueryValue { Name: "Count", Type: "Enumerable" } || value is QueryValue { Name: "Count", IsProperty: true, Of: { } counted } && Unstored(counted) is QueryValue { Name: "Cards" };

    private static ConditionReading? Count(ProgramValue query, SimCompare compare, int than, Context context, string? at)
    {
        ConditionReading Simulated(CountCondition condition) => new(ConditionWay.Simulated) { Condition = condition with { At = at } };
        // Your hand: the cards (the card played isn't among them), or the ones a lambda lets through.
        if (HandQuery(query) is { } hand)
        {
            if (hand.Lambda == null) return Simulated(new CountCondition(SimCounted.HandCards, compare, than));
            var key = at != null ? FilterKey(context.CardType, at) : null;
            if (key == null) return new ConditionReading(ConditionWay.NotRead) { Reason = "a filter of your hand's cards with no place in the code" };
            int? matching = context.Board == null ? 0 : MatchingNow(hand.Source, hand.Lambda, context);
            if (matching == null) return new ConditionReading(ConditionWay.NotRead) { Reason = "a filter of your hand's cards the board can't tell" };
            return Simulated(new CountCondition(SimCounted.HandMatching, compare, than) { Filter = key, Matching = matching.Value });
        }
        // The card plays finished this turn: by you (any card), or of this card.
        if (query is QueryValue { Type: "Enumerable", Name: "Any" or "Count", Arguments: [var plays, LambdaValue whose] }
            && plays.Parts().Any(part => part is QueryValue { Name: "CardPlaysFinished" }) && PlaysTested(whose) is { } counted)
        {
            if (counted == SimCounted.PlaysFinishedThisTurn) return Simulated(new CountCondition(counted, compare, than));
            var before = context.Board == null ? 0 : ProgramEvaluator.Value(query, context.Board with { PlaysBefore = 0 }, context.Program) switch
            {
                bool played => played ? 1 : 0,
                decimal count => (int)count,
                _ => (int?)null,
            };
            if (before == null) return new ConditionReading(ConditionWay.NotRead) { Reason = "this card's plays this turn, which the board can't tell" };
            return Simulated(new CountCondition(counted, compare, than) { Before = before.Value });
        }
        return null;
    }

    /// <summary>A query over your hand's cards (GetPile(Hand).Cards, or less the card itself), and its lambda; null when it isn't one.</summary>
    private sealed record HandQueryParts(ProgramValue Source, LambdaValue? Lambda);

    private static HandQueryParts? HandQuery(ProgramValue value)
    {
        value = Unstored(value);
        if (value is QueryValue { Name: "Count", IsProperty: true, Of: { } cards } && IsHandCards(cards)) return new HandQueryParts(cards, Lambda: null);
        if (value is QueryValue { Type: "Enumerable", Name: "Any" or "Count", Arguments: [var source, ..] } query && IsHandCards(source))
            return new HandQueryParts(source, query.Arguments.Count > 1 ? query.Arguments[1] as LambdaValue : null) is { } parts
                   && (query.Arguments.Count == 1 || parts.Lambda != null) ? parts : null;
        return null;
    }

    /// <summary>Your hand's cards: PileType.Hand's pile of the owner, its Cards; a list of them; them less the card itself.</summary>
    private static bool IsHandCards(ProgramValue value)
    {
        value = Unstored(value);
        if (value is QueryValue { Name: "ToList" or "ToArray" or "AsEnumerable", Arguments: [var inner] }) return IsHandCards(inner);
        if (value is QueryValue { Name: "Except", Arguments: [var from, var except] } && except is ArrayValue or QueryValue { Name: ".ctor" }
            && except.Parts().Any(part => part is NamedValue { Name: "this" }) && !except.Parts().Any(part => part is QueryValue { Name: not ".ctor" }))
            return IsHandCards(from); // the card itself is in play, never in your hand, while its OnPlay runs
        return CardEffects.IsYourWholeHand(value);
    }

    /// <summary>What a lambda over CardPlaysFinished counts: this turn's, by you, or of this card; null for anything else.</summary>
    private static SimCounted? PlaysTested(LambdaValue lambda)
    {
        if (lambda.Body.Terms is not [var tests]) return null;
        var thisTurn = false;
        SimCounted? whose = null;
        foreach (var literal in tests)
        {
            // A comparison that doesn't hold is the opposite comparison that does (the compiler's !(a != b)).
            var test = literal.Test is CompareValue compare && !literal.Holds ? compare with { Op = CompareValue.Negated(compare.Op) } : literal.Test;
            if (!literal.Holds && test is not CompareValue) return null;
            if (test is QueryValue { Name: "HappenedThisTurn" }) thisTurn = true;
            else if (test is CompareValue { Op: Comparison.Equal, Left: QueryValue { Name: "Player", Of: QueryValue { Name: "CardPlay" } }, Right: var player } && IsOwner(player))
                whose = SimCounted.PlaysFinishedThisTurn;
            else if (test is CompareValue { Op: Comparison.Equal, Left: QueryValue { Name: "Card", Of: QueryValue { Name: "CardPlay" } }, Right: NamedValue { Name: "this" } })
                whose = SimCounted.CardPlaysFinished;
            else return null;
        }
        return thisTurn ? whose : null;
    }

    /// <summary>The cards of your hand now a lambda lets through (the card played left out); null when one can't be told.</summary>
    private static int? MatchingNow(ProgramValue source, LambdaValue lambda, Context context)
    {
        if (ProgramEvaluator.Items(source, context.Board!, context.Program) is not { } cards) return null;
        var count = 0;
        foreach (var card in cards)
            switch (ProgramEvaluator.Passes(lambda, card, context.Board!, context.Program))
            {
                case null: return null;
                case true: count++; break;
            }
        return count;
    }

    // ---- decided before the play

    /// <summary>A test decided before the play: its value on the board (null with no board).</summary>
    private static ConditionReading Decided(ProgramValue test, Context context, string why, bool? holds = null)
    {
        if (holds == null && context.Board != null)
            holds = ProgramEvaluator.Holds(ProgramCondition.Of(new ConditionLiteral("decided", test, Holds: true)), context.Board, context.Program);
        return new ConditionReading(ConditionWay.Decided) { Holds = holds, Reason = why };
    }

    /// <summary>The value is the card's own: card values, constants, arithmetic and comparisons over them, its upgrade.</summary>
    private static bool IsCardsOwn(ProgramValue value, Context context) => value.Parts().All(part => part switch
    {
        CardValue or ConstantValue or ArithmeticValue or CompareValue or NullValue or StoredValue => true,
        NamedValue { Name: "this" or "Decimal.One" or "Decimal.Zero" or "Decimal.MinusOne" } => true,
        QueryValue { IsProperty: true, Of: NamedValue { Name: "this" }, Name: var property } => property == "IsUpgraded" || IsStoredOnCard(context, property),
        QueryValue { Name: "op_UnaryNegation" or "op_Addition" or "op_Subtraction" or "op_Multiply", Of: null } => true,
        _ => false,
    });

    /// <summary>
    /// A property of the card's type whose getter only reads a field of the card (an auto-property, or one backed by a field it saves: Mad
    /// Science's type and rider, set when it's made), never worked out from the board.
    /// </summary>
    private static bool IsStoredOnCard(Context context, string property)
    {
        for (var type = context.CardType; type != null; type = type.BaseType)
        {
            if (type.GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is not { GetMethod: { } getter }) continue;
            List<CodeOp> code;
            try { code = context.ReadCode(getter).Where(instruction => instruction.OpCode != System.Reflection.Emit.OpCodes.Nop).ToList(); }
            catch { return false; }
            return code is [{ OpCode: var load }, { OpCode: var field, Member.Kind: CodeMemberKind.Field }, { OpCode: var done }]
                   && load == System.Reflection.Emit.OpCodes.Ldarg_0 && field == System.Reflection.Emit.OpCodes.Ldfld && done == System.Reflection.Emit.OpCodes.Ret;
        }
        return false;
    }

    private static int? Number(ProgramValue value, Context context) =>
        context.Board != null && ProgramEvaluator.Number(value, context.Board, context.Program) is { } number ? (int)number
        : value is ConstantValue constant ? (int)constant.Value : null;

    private static SimCompare Compared(Comparison comparison) => comparison switch
    {
        Comparison.Equal => SimCompare.Equal,
        Comparison.NotEqual => SimCompare.NotEqual,
        Comparison.Less => SimCompare.Less,
        Comparison.LessOrEqual => SimCompare.LessOrEqual,
        Comparison.Greater => SimCompare.Greater,
        _ => SimCompare.GreaterOrEqual,
    };

    private static bool IsOwner(ProgramValue value) => Unstored(value) is QueryValue { Name: "Owner", Of: NamedValue { Name: "this" } };

    private static bool IsTarget(ProgramValue value) => Unstored(value) is QueryValue { Name: "Target", Of: NamedValue { Name: "cardPlay" } };

    private static ProgramValue Unstored(ProgramValue value) => value is StoredValue stored ? Unstored(stored.Value) : value;
}
