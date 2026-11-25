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

    /// <summary>NotRead: the test the simulator doesn't ask yet (the card scan reviews it by its kind: CardCoverage.ConditionGapKinds).</summary>
    public ProgramValue? Unread { get; init; }

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
    public sealed record Context(Type CardType, Func<MethodBase, List<CodeOp>> ReadCode, ProgramBoard? Board, ModelProgram Program);

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
    public static IReadOnlyList<HandFilter> HandFiltersOf(Type cardType, ModelProgram program)
    {
        var filters = new List<HandFilter>();
        foreach (var step in program.All())
        {
            if (step.Condition == null) continue;
            foreach (var literal in step.Condition.Literals)
                if (literal.Test.Parts().Select(HandQuery).FirstOrDefault(query => query?.Lambda != null) is { Lambda: { } lambda } && filters.All(filter => filter.Key != FilterKey(cardType, literal.At)))
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
        // A local the code stores, read later: asked where it's stored. Only before the play's first command can that be placed; one stored
        // with no command between its store and its test (Molten Fist's num, right before its if) is the same asked at the test.
        if (NoCommandBetween(context, storedAt, literal.At)) return reading with { Condition = condition };
        if (context.Program.Commands.Any(command => command.Offset < storedAt))
            return new ConditionReading(ConditionWay.NotRead) { Reason = $"a test stored after the play's first command ({literal.Test})" };
        return reading with { Condition = condition, AskFirst = reading.AskFirst.Append(reading.Condition!).ToList() };
    }

    /// <summary>No command of the play runs between a local's store and the test that reads it (its branch's offset, in the same method).</summary>
    private static bool NoCommandBetween(Context context, int storedAt, string at)
    {
        var colon = at.LastIndexOf(':');
        if (colon < 0 || !int.TryParse(at[(colon + 1)..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var testAt) || testAt < storedAt) return false;
        return !context.Program.Commands.Any(command => command.Offset > storedAt && command.Offset < testAt);
    }

    /// <summary>A value's test, positive (a literal's direction is applied by the caller), its At kept for the engine's once-a-play asking.</summary>
    private static ConditionReading Test(ProgramValue test, Context context, string? at, int depth)
    {
        ConditionReading Simulated(SimCondition condition) => new(ConditionWay.Simulated) { Condition = condition with { At = at } };
        ConditionReading NotRead(string why) => new(ConditionWay.NotRead) { Reason = why, Unread = test };

        // A comparison the code makes by calling its type's operator (decimal's op_GreaterThan(a, b)): the comparison.
        if (test is QueryValue { Of: null, Arguments: [var compared, var against] } call && OperatorComparisons.TryGetValue(call.Name, out var comparison))
            return Test(new CompareValue(comparison, compared, against), context, at, depth);
        // A helper of the card's own class it's given the card to (Sovereign Blade's GetOwnerParryAmount(this)): what it returns for the card played.
        if (depth < 2 && test.Parts().Any(part => OwnHelperCall(part, context) != null))
            return Test(test.Map(part => OwnHelperCall(part, context)), context, at, depth + 1);

        // The attack's kill: Any over the results of AttackCommand.Execute of WasTargetKilled (Sunder, Knockout Blow), or of a card's CreatureCmd.Damage
        // (Timber, v0.114.0: its damage inside its attack context, simulated as its attack; any other card's, a DamageAction): both record the kill.
        if (test is QueryValue { Name: "Any" } any && any.Parts().Any(part => part is QueryValue { Name: "WasTargetKilled" })
            && any.Parts().Any(part => part is ResultValue result && (result.Command.StartsWith("AttackCommand.", StringComparison.Ordinal)
                || result.Command == "CreatureCmd.Damage")))
            return Simulated(new AttackKilledCondition());
        // Osty: missing (CheckMissingWithAnim of the card's owner), alive (its owner's IsOstyAlive).
        if (test is QueryValue { Name: "CheckMissingWithAnim", Of: null, Arguments: [var player] } && IsOwner(player))
            return Simulated(new OstyMissingCondition());
        if (test is QueryValue { Name: "IsOstyAlive", IsProperty: true, Of: var ostyOwner } && ostyOwner != null && IsOwner(ostyOwner))
            return Simulated(new NotCondition(new OstyMissingCondition()));
        // The fight going on: a play the simulator runs is in a fight (the card's CombatState) that isn't over (CombatManager.IsOverOrEnding).
        if (test is QueryValue { Name: "IsOverOrEnding" }) return new ConditionReading(ConditionWay.Decided) { Holds = false, Reason = "the fight goes on" };
        if (test is QueryValue { Name: "CombatState", Of: NamedValue { Name: "this" } }) return new ConditionReading(ConditionWay.Decided) { Holds = true, Reason = "the card is in the fight" };
        // The run's room a combat room (CombatState.RunState.CurrentRoom is CombatRoom: The Hunt's): a play the simulator runs is in a fight's room.
        if (test is QueryValue { Name: "is", Type: "CombatRoom" or "MegaCrit.Sts2.Core.Rooms.CombatRoom", Of: { } room } && Unstored(room) is QueryValue { Name: "CurrentRoom" })
            return new ConditionReading(ConditionWay.Decided) { Holds = true, Reason = "a play is in a combat room" };
        // A shot's result: it had an arrow to shoot at a living target (ArrowCmd.ShootArrowAtTarget, TryShootArrow: Hobbling Shot's debuffs).
        if (test is ResultValue { Command: "ArrowCmd.ShootArrowAtTarget" or "ArrowCmd.TryShootArrow" })
            return Simulated(new ShotHadArrowCondition());
        // Your arrows: any to shoot (ArrowCmd.PlayerHasAnyArrows(Owner)).
        if (test is QueryValue { Name: "PlayerHasAnyArrows", Of: null, Arguments: [var archer] } && IsOwner(archer))
            return Simulated(new CountCondition(SimCounted.ArrowsAvailable, SimCompare.Greater, Than: 0));
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
        // A question to the fight's log (decision #191: the history ledger): whether there's any, or its count compared with a number.
        if (LoggedTest(test, context, at) is { } logged) return logged;
        // The board the simulator keeps: the target's powers and intent, your exhaust pile.
        if (BoardTest(test, context, at) is { } board) return board;
        // A property of the card: its upgrade, an auto-property (set when it's made: Mad Science's rider), or a getter read from its code.
        if (test is QueryValue { IsProperty: true, Of: NamedValue { Name: "this" }, Name: var property })
        {
            if (property == "IsUpgraded" || IsStoredOnCard(context, property)) return Decided(test, context, "the card's own value");
            if (depth < 2 && ProgramReader.ReadProperty(context.CardType, property, context.ReadCode) is { } getter) return Nested(getter, context, at, depth + 1);
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

    /// <summary>
    /// A question to the fight's log as a condition on the ledger's count (decision #191): Any is the count above 0; a Count compared with a
    /// number the card's own values give. A question the ledger doesn't count (a reviewed gap) isn't read; null when it isn't one.
    /// </summary>
    private static ConditionReading? LoggedTest(ProgramValue test, Context context, string? at)
    {
        ConditionReading Simulated(HistoryQuery query, SimCompare compare, int than) =>
            new(ConditionWay.Simulated) { Condition = new CountCondition(SimCounted.History, compare, than) { Query = query, At = at } };
        if (test is QueryValue { Type: "Enumerable", Name: "Any" } any && ProgramReader.ReadsHistory(any))
            return HistoryQueryReader.Read(any) is { Query: { } query } ? Simulated(query, SimCompare.Greater, than: 0)
                : new ConditionReading(ConditionWay.NotRead) { Reason = "a question to the fight's log the ledger doesn't count", Unread = test };
        if (test is CompareValue { Left: var left, Right: var right } compare && Unstored(left) is QueryValue { Type: "Enumerable", Name: "Count" } count
            && ProgramReader.ReadsHistory(count) && IsCardsOwn(right, context) && Number(right, context) is { } number)
            return HistoryQueryReader.Read(count) is { Query: { } counted } ? Simulated(counted, Compared(compare.Op), number)
                : new ConditionReading(ConditionWay.NotRead) { Reason = "a question to the fight's log the ledger doesn't count", Unread = test };
        return null;
    }

    /// <summary>
    /// A test of the board the simulator keeps, asked as the step runs: the target has a power (HasPower&lt;T&gt;: Bubble Bubble's poison), its
    /// amount of one compared with a number (Molten Fist's Vulnerable, 0 when it's dead), it intends to attack (Go for the Eyes), the cards
    /// in your exhaust pile compared with a number (Pact's End). Null when it's none of those.
    /// </summary>
    private static ConditionReading? BoardTest(ProgramValue test, Context context, string? at)
    {
        ConditionReading Simulated(SimCondition condition) => new(ConditionWay.Simulated) { Condition = condition with { At = at } };
        if (test is QueryValue { Name: "HasPower", GenericArguments: [var power], Of: { } holder } && IsTarget(holder))
            return Simulated(new CountCondition(SimCounted.TargetPower, SimCompare.Greater, Than: 0) { Power = PowerId.Of(power) });
        if (test is QueryValue { Name: "HasPower", GenericArguments: [var ownPower], Of: { } ownHolder } && IsYou(ownHolder))
            return Simulated(new CountCondition(SimCounted.OwnerPower, SimCompare.Greater, Than: 0) { Power = PowerId.Of(ownPower) });
        if (test is QueryValue { Name: "IntendsToAttack", Of: QueryValue { Name: "Monster", Of: { } monster } } && IsTarget(monster))
            return Simulated(new TargetIntendsToAttackCondition());
        // Every power on the target answers that its death sets off Fatal (cardPlay.Target.Powers.All(p => p.ShouldOwnerDeathTriggerFatal()): The
        // Hunt's reward): the target's own answer, read from its powers' code (SimEnemy.DeathSetsOffFatal), asked as the step runs.
        if (test is QueryValue { Type: "Enumerable", Name: "All", Arguments: [QueryValue { Name: "Powers", Of: { } powersOf }, LambdaValue { Body.Terms: [[{ Holds: true, Test: QueryValue { Name: "ShouldOwnerDeathTriggerFatal", Of: ParameterValue asked } }]] } fatal] }
            && IsTarget(powersOf) && asked.Name == fatal.Parameter)
            return Simulated(new TargetDeathSetsOffFatalCondition());
        // The target alive or dead (Creature.IsAlive, IsDead: its HP above 0 or not).
        if (test is QueryValue { Name: "IsAlive" or "IsDead", IsProperty: true, Of: { } living } && IsTarget(living))
            return Simulated(new CountCondition(SimCounted.TargetHp, test is QueryValue { Name: "IsAlive" } ? SimCompare.Greater : SimCompare.LessOrEqual, Than: 0));
        if (test is not CompareValue { Left: var left, Right: var right } compare || !IsCardsOwn(right, context)) return null;
        var number = Number(right, context);
        if (number == null && context.Board != null) return null;
        if (TargetPowerOf(Unstored(left)) is { } amountOf)
            return Simulated(new CountCondition(SimCounted.TargetPower, Compared(compare.Op), number ?? 0) { Power = PowerId.Of(amountOf) });
        // Your amount of a power compared with a number (Cho Ki's Stretch).
        if (Unstored(left) is QueryValue { Name: "GetPowerAmount", GenericArguments: [var yours], Of: { } you } && IsYou(you))
            return Simulated(new CountCondition(SimCounted.OwnerPower, Compared(compare.Op), number ?? 0) { Power = PowerId.Of(yours) });
        if (IsExhaustPileCount(Unstored(left)))
            return Simulated(new CountCondition(SimCounted.ExhaustPile, Compared(compare.Op), number ?? 0));
        // The target's HP compared with a number (Judgement's threshold).
        if (Unstored(left) is QueryValue { Name: "CurrentHp", IsProperty: true, Of: { } hurt } && IsTarget(hurt))
            return Simulated(new CountCondition(SimCounted.TargetHp, Compared(compare.Op), number ?? 0));
        return null;
    }

    /// <summary>
    /// The power whose amount on the target the value is: GetPowerAmount&lt;T&gt;() of the target, or that when it's alive and 0 when it isn't
    /// (Molten Fist: target.IsAlive ? target.GetPowerAmount&lt;VulnerablePower&gt;() : 0); null for anything else.
    /// </summary>
    internal static string? TargetPowerOf(ProgramValue value)
    {
        if (value is QueryValue { Name: "GetPowerAmount", GenericArguments: [var power], Of: { } holder } && IsTarget(holder)) return power;
        if (value is WaysValue { Ways: [var first, var second] }
            && new[] { first, second }.SingleOrDefault(way => way.Value is ConstantValue { Value: 0 }) is { When: { } dead } && dead.Terms is [[{ Test: QueryValue { Name: "IsAlive" } deadTest, Holds: false }]] && IsTarget(deadTest.Of!)
            && new[] { first, second }.SingleOrDefault(way => way.Value is not ConstantValue) is { Value: var amount, When: { } alive } && alive.Terms is [[{ Test: QueryValue { Name: "IsAlive" } aliveTest, Holds: true }]] && IsTarget(aliveTest.Of!))
            return TargetPowerOf(amount);
        return null;
    }

    /// <summary>The cards in your exhaust pile: Count(CardPile.GetCards(Owner, [Exhaust])) or GetPile(Exhaust, Owner).Cards.Count.</summary>
    private static bool IsExhaustPileCount(ProgramValue value)
    {
        static bool IsExhaust(ProgramValue pile) => pile is ConstantValue { Value: var type } && HistoryQueryReader.LoadedEnumName("PileType", type) == "Exhaust";
        if (value is QueryValue { Type: "Enumerable", Name: "Count", Arguments: [QueryValue { Name: "GetCards", Arguments: [var owner, ArrayValue { Items: [var pile] }] }] })
            return IsOwner(owner) && IsExhaust(pile);
        if (value is QueryValue { Name: "Count", IsProperty: true, Of: QueryValue { Name: "Cards", Of: QueryValue { Name: "GetPile", Arguments: [var pileType, var player] } } })
            return IsExhaust(pileType) && IsOwner(player);
        return false;
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
        // The card's X: what playing it now spends, fixed for the play (Repeat Shot at X 0).
        QueryValue { Name: "ResolveEnergyXValue" or "ResolveStarXValue", Of: NamedValue { Name: "this" } } => true,
        // The hand's limit (CardPile.MaxCardsInHand): the game's fixed number.
        QueryValue { Type: "CardPile", Name: "MaxCardsInHand", Of: null, IsProperty: true } => true,
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

    /// <summary>The card owner's creature (this.Owner.Creature).</summary>
    private static bool IsYou(ProgramValue value) => Unstored(value) is QueryValue { Name: "Creature", Of: { } owner } && IsOwner(owner);

    /// <summary>
    /// The power whose amount on the target the value is, also through a null check of it (Dominate: target.GetPower&lt;T&gt;()?.Amount ?? 0);
    /// null for anything else.
    /// </summary>
    internal static string? TargetPowerAmountOf(ProgramValue value)
    {
        value = Unstored(value);
        if (TargetPowerOf(value) is { } power) return power;
        if (value is WaysValue { Ways: [var first, var second] }
            && new[] { first, second }.Count(way => way.Value is ConstantValue { Value: 0 }) == 1
            && new[] { first, second }.SingleOrDefault(way => way.Value is not ConstantValue).Value is QueryValue { Name: "Amount", Of: QueryValue { Name: "GetPower", GenericArguments: [var held], Of: { } holder } }
            && IsTarget(holder))
            return held;
        return null;
    }

    private static ProgramValue Unstored(ProgramValue value) => value is StoredValue stored ? Unstored(stored.Value) : value;

    /// <summary>A number type's comparison operators, called by name (decimal's).</summary>
    private static readonly Dictionary<string, Comparison> OperatorComparisons = new(StringComparer.Ordinal)
    {
        ["op_Equality"] = Comparison.Equal,
        ["op_Inequality"] = Comparison.NotEqual,
        ["op_LessThan"] = Comparison.Less,
        ["op_LessThanOrEqual"] = Comparison.LessOrEqual,
        ["op_GreaterThan"] = Comparison.Greater,
        ["op_GreaterThanOrEqual"] = Comparison.GreaterOrEqual,
    };

    /// <summary>
    /// What a call to a static helper of the card's own class, given the card, returns for the card played (Sovereign Blade's GetOwnerParryAmount(this):
    /// its owner's Parry): read from its code with its parameters what the call passes, the way the played card takes (every test on the way
    /// one the card in play passes: <see cref="PlayedCardPasses"/>). Null for any other value, or a helper with no such one way.
    /// </summary>
    private static ProgramValue? OwnHelperCall(ProgramValue value, Context context)
    {
        if (value is not QueryValue { Of: null, IsProperty: false } call || call.Type != context.CardType.Name
            || !call.Arguments.Any(argument => argument is NamedValue { Name: "this" })) return null;
        var method = context.CardType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .SingleOrDefault(candidate => candidate.Name == call.Name && candidate.GetParameters().Length == call.Arguments.Count);
        if (method == null) return null;
        WaysValue ways;
        try { ways = ProgramReader.ReadReturnedWays(context.CardType, method, context.ReadCode); }
        catch { return null; }
        var parameters = method.GetParameters();
        ProgramValue Passed(ProgramValue part) => part.Map(inner => inner is ParameterValue parameter
            && Array.FindIndex(parameters, candidate => candidate.Name == parameter.Name) is var index and >= 0 ? call.Arguments[index] : null);
        var taken = ways.Ways.Where(way => way.When.Terms.Any(term => term.All(literal => PlayedCardPasses(Passed(literal.Test), literal.Holds)))).ToList();
        return taken.Count == 1 ? Passed(taken[0].Value) : null;
    }

    /// <summary>
    /// The card being played passes the test (or fails it, <paramref name="holds"/> false) wherever it's played: it's mutable, has an owner and a
    /// pile, and that pile (its play pile) is one of the fight's; it's in a fight.
    /// </summary>
    private static bool PlayedCardPasses(ProgramValue test, bool holds) => holds && test is QueryValue
    {
        IsProperty: true,
        Name: "IsMutable" or "Owner" or "Pile" or "CombatState" or "IsCombatPile",
        Of: NamedValue { Name: "this" } or QueryValue { Name: "Pile", Of: NamedValue { Name: "this" } },
    } tested && (tested.Name == "IsCombatPile") == tested.Of is QueryValue;

    // ---- what a decided test reads (decision #311)

    /// <summary>
    /// The first input of <paramref name="condition"/> the play itself could change (a creature's HP, block or powers, a pile's cards, your orbs,
    /// the enemies there are, the fight's log, what a command gave, a card field its own code stores, a value the reader couldn't follow), named;
    /// null when every input is fixed for the play. What may be decided on the board before the play (decision #311): only a test of inputs
    /// fixed for it. Read from each part of each test, never from the card: every query is a change the play can make to what it gives unless
    /// it's one of <see cref="FixedQueries"/> (the card's own values and upgrade, the fight's room and run, the owner's character and card
    /// pools, the objects the play is about, a roll's source of randomness, the collection operators), each of whose own inputs is read in
    /// turn; a pick's card is a card (decision #188).
    /// </summary>
    public static string? ChangeableInput(ProgramCondition condition, Context context) =>
        condition.Literals.Select(literal => ChangeableInput(literal.Test, context)).FirstOrDefault(input => input != null);

    /// <summary>The first input of one test the play itself could change, named; null when it reads only what's fixed for the play.</summary>
    public static string? ChangeableInput(ProgramValue test, Context context)
    {
        var stored = context.Program.All().OfType<StoreStep>().Select(store => store.Field).ToHashSet(StringComparer.Ordinal);
        // A pick's card is a card (decision #188); a roll's source of randomness is never read (the roll is a branch per outcome, never
        // looked at before it): its receiver (NextItem, TakeRandom) or its last argument (GetDistinctForCombat, StableShuffle), as the game declares them.
        var asRead = test.Map(part => part switch
        {
            ResultValue result when result.Command.StartsWith("CardSelectCmd.", StringComparison.Ordinal) => new ConstantValue(1),
            QueryValue { Name: "NextItem" or "TakeRandom" } roll => roll with { Of = null },
            QueryValue { Name: "GetDistinctForCombat" or "StableShuffle", Arguments.Count: > 0 } roll => roll with { Arguments = roll.Arguments.Take(roll.Arguments.Count - 1).ToList() },
            _ => null,
        });
        foreach (var part in asRead.Parts())
            switch (part)
            {
                case UnknownValue unknown:
                    return $"a value the reader couldn't follow ({unknown})";
                case ResultValue result:
                    return $"what {result.Command} gave";
                case QueryValue query when !IsFixedQuery(query, context, stored):
                    return query.Of is NamedValue { Name: "this" } ? $"the card's {query.Name}" : query.Name;
            }
        return null;
    }

    /// <summary>
    /// What the query gives is fixed for the play, given its inputs: one of <see cref="FixedQueries"/>, an operator, the card's upgrade or X, or
    /// the card's own state (a property stored on it, a field of its class) that its own code doesn't store to (<paramref name="storedByThePlay"/>).
    /// </summary>
    private static bool IsFixedQuery(QueryValue query, Context context, IReadOnlySet<string> storedByThePlay)
    {
        if (query.Of is NamedValue { Name: "this" } && storedByThePlay.Contains(query.Name)) return false;
        if (FixedQueries.Contains(query.Name)) return true;
        if (query.Of == null && query.Name.StartsWith("op_", StringComparison.Ordinal)) return true;
        // The game's own fixed numbers (a hand's limit).
        if (query.Of == null && IsCardsOwn(query, context)) return true;
        if (query.Of is not NamedValue { Name: "this" }) return false;
        if (query.Name is "IsUpgraded" or "ResolveEnergyXValue" or "ResolveStarXValue") return true;
        if (query.IsProperty) return IsStoredOnCard(context, query.Name);
        return query.Arguments.Count == 0 && IsFieldOfCard(context.CardType, query.Name);
    }

    /// <summary>A field of the card's class or a base class (a read of it is a QueryValue with no arguments, not a property).</summary>
    private static bool IsFieldOfCard(Type cardType, string field)
    {
        for (var type = cardType; type != null; type = type.BaseType)
            if (type.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null) return true;
        return false;
    }

    /// <summary>
    /// The test is read before the play runs any command (decision #311), so on its first play it reads the board as the play starts: a test
    /// of a step that's in no loop or command's delegate with no command before it in the code (none the reader couldn't follow, none only
    /// another way through the code runs: an if's other side), or one whose every input the play can change is in a local the code stores
    /// before its first command (Optimism's count of your Sadness, Storm of Steel's hand).
    /// </summary>
    public static bool ReadBeforeAnyCommand(ProgramValue test, CommandStep step, Context context)
    {
        var program = context.Program;
        if (program.Steps.Any(top => ReferenceEquals(top, step))
            && !program.All().TakeWhile(before => !ReferenceEquals(before, step))
                .Any(before => before is CommandStep or SpireQoL.Procs.Programs.UnknownStep && !Exclusive(before.Condition, step.Condition)))
            return true;
        bool StoredFirst(int offset) => !program.All().Any(before => before is CommandStep or SpireQoL.Procs.Programs.UnknownStep && before.Offset < offset);
        if (!test.Parts().Any(part => part is StoredValue stored && StoredFirst(stored.Offset))) return false;
        return ChangeableInput(test.Map(part => part is StoredValue stored && StoredFirst(stored.Offset) ? new ConstantValue(0) : null), context) == null;
    }

    /// <summary>No way through the code runs both: each way of one takes a branch the other way of the other doesn't (an if and its else).</summary>
    public static bool Exclusive(ProgramCondition? first, ProgramCondition? second) =>
        first != null && second != null && !first.IsAlways && !second.IsAlways
        && first.Terms.All(firstWay => second.Terms.All(secondWay =>
            firstWay.Any(literal => secondWay.Any(other => other.At == literal.At && other.Holds != literal.Holds))));

    /// <summary>
    /// The queries a play can't change what they give (ChangeableInput): each reads its own inputs (what it's asked of, its arguments), which are
    /// read in turn. Anything not here is an input the play can change.
    /// </summary>
    internal static readonly IReadOnlySet<string> FixedQueries = new HashSet<string>(StringComparer.Ordinal)
    {
        // The objects the play is about, as references (what's asked of them is read in turn): the card's owner, their creature, the target, a
        // teammate's player, the fight's state and run, the game's fight manager.
        "Owner", "Creature", "Player", "Target", "CombatState", "RunState", "Instance",
        // Facts of the fight a play can't change: its room (a combat room for every play), the fight going on (the simulator stops at its end),
        // the owner's character, unlocks and card pools.
        "CurrentRoom", "is", "IsOverOrEnding", "Character", "CardPool", "UnlockState", "CardMultiplayerConstraint", "GetUnlockedCards",
        // A card's kind, asked of a card a collection gives (the collection is read in turn): its type, tags, targeting, whether it upgrades.
        "Type", "Tags", "TargetType", "IsUpgradable", "Rarity",
        // A roll: what it picks from is its argument (read in turn); its source of randomness is left out before (ChangeableInput).
        "NextItem", "GetDistinctForCombat", "StableShuffle", "TakeRandom",
        // Collection and number operators: what they give is their inputs'.
        "Where", "Select", "SelectMany", "ToList", "ToArray", "AsEnumerable", "Count", "Any", "All", "FirstOrDefault", "First", "OfType", "Except",
        "Take", "Contains", "Min", "Max", "MinBy", "MaxBy", "Sum", "HasValue", "Value", ".ctor",
    };
}
