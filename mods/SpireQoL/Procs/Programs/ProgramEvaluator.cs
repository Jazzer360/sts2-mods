using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace SpireQoL.Procs.Programs;

/// <summary>
/// Where a card's condition is evaluated: the card, the target its play is aimed at (null: none, or not known), and how many of its
/// replay's plays have finished before the one evaluated (Echo Form's second play: 1).
/// </summary>
public sealed record ProgramBoard(CardModel Card, Creature? Target = null, int PlaysBefore = 0);

/// <summary>
/// Evaluates what a card's code tests (ProgramCondition, ProgramValue) against the board as it is before the play, the way the game
/// would: each query is translated once, for every card that asks it, never per card (decision #182, docs/CARD_PROGRAMS.md):
/// <list type="bullet">
/// <item>a property of the game's objects is read (the game's own answer: Ftl.CanDrawCard, a card's Type); a method is called only
/// when it only asks (Is..., Has..., Can..., Get..., Contains, HappenedThisTurn), never anything about the RNG;</item>
/// <item>Any, All, Count, Where, FirstOrDefault, Except, OfType, ToList over a collection, with the code's own lambda;</item>
/// <item>your hand as the play sees it: the played card has left it (it's in play while its OnPlay runs);</item>
/// <item>Osty.CheckMissingWithAnim(player) is the player's Osty missing (it only plays an animation besides);</item>
/// <item>on a replay's later plays, the earlier plays have finished (a query over CardPlaysFinished counts them: FTL's plays this
/// turn, Fetch's first play), a test a loop ran until it failed stays failed (Wrest's arrows), and an arrow test follows your
/// hits (Ki Hop: SimEngine.PlaysStartingImpaled).</item>
/// </list>
/// What it can't evaluate is unknown (null), never guessed.
/// </summary>
public static class ProgramEvaluator
{
    /// <summary>What a value is when it can't be told.</summary>
    private static readonly object Unknown = new();

    /// <summary>The play itself (cardPlay): its Target, Card and Player.</summary>
    private static readonly object ThePlay = new();

    private sealed record Context(ProgramBoard Board, CardProgram Program, IReadOnlyDictionary<string, object?> Parameters);

    private sealed record Closure(LambdaValue Lambda, Context Context);

    /// <summary>Whether <paramref name="condition"/> holds on the board: null when it can't be told.</summary>
    public static bool? Holds(ProgramCondition condition, ProgramBoard board, CardProgram program) =>
        Holds(condition, new Context(board, program, new Dictionary<string, object?>()));

    /// <summary>The value on the board (a number as a decimal, an object, null): null when it can't be told.</summary>
    public static object? Value(ProgramValue value, ProgramBoard board, CardProgram program)
    {
        var result = Evaluate(value, new Context(board, program, new Dictionary<string, object?>()));
        return result == Unknown ? null : result;
    }

    /// <summary>A number the value works out to on the board (a card value, a count, a constant); null when it can't be told.</summary>
    public static decimal? Number(ProgramValue value, ProgramBoard board, CardProgram program) =>
        ToNumber(Evaluate(value, new Context(board, program, new Dictionary<string, object?>())));

    /// <summary>Whether <paramref name="condition"/> holds for <paramref name="item"/> (the card a loop or a pick takes: "each").</summary>
    public static bool? HoldsFor(ProgramCondition condition, object? item, ProgramBoard board, CardProgram program) =>
        Holds(condition, new Context(board, program, new Dictionary<string, object?> { ["each"] = item }));

    /// <summary>Whether a lambda (a filter) holds for <paramref name="item"/>.</summary>
    public static bool? Passes(LambdaValue lambda, object? item, ProgramBoard board, CardProgram program) =>
        Holds(lambda.Body, new Context(board, program, new Dictionary<string, object?> { [lambda.Parameter] = item }));

    /// <summary>The items a collection value holds on the board (your hand without the played card); null when it can't be told.</summary>
    public static IReadOnlyList<object?>? Items(ProgramValue value, ProgramBoard board, CardProgram program) =>
        Evaluate(value, new Context(board, program, new Dictionary<string, object?>())) is IEnumerable items and not string ? items.Cast<object?>().ToList() : null;

    /// <summary>The condition asks about the play's target (cardPlay.Target): it can differ from one target to the next.</summary>
    public static bool AsksTarget(ProgramCondition condition) =>
        condition.Literals.Any(literal => literal.Test.Parts().Any(part => part is QueryValue { Name: "Target", Of: NamedValue { Name: "cardPlay" } }));

    /// <summary>
    /// Every test in it is one the evaluator translates (nothing the reader couldn't follow, no command's result, which only the play
    /// itself gives): the card scan flags a draw on a condition that isn't.
    /// </summary>
    public static bool CanEvaluate(ProgramCondition condition) =>
        condition.Literals.All(literal => literal.Test.Parts().All(part => part is not (UnknownValue or ResultValue)));

    private static bool? Holds(ProgramCondition condition, Context context)
    {
        if (condition.IsAlways) return true;
        bool? any = false;
        foreach (var term in condition.Terms)
        {
            bool? all = true;
            foreach (var literal in term)
            {
                var truth = Truth(Evaluate(literal.Test, context));
                if (truth == null) { all = null; continue; }
                if (truth != literal.Holds) { all = false; break; }
            }
            if (all == true) return true;
            if (all == null) any = null;
        }
        return any;
    }

    private static bool? Truth(object? value) => value switch
    {
        _ when value == Unknown => null,
        null => false,
        bool flag => flag,
        _ when ToNumber(value) is { } number => number != 0m,
        _ => true,
    };

    private static decimal? ToNumber(object? value) => value switch
    {
        decimal number => number,
        int number => number,
        long number => number,
        uint number => number,
        bool flag => flag ? 1m : 0m,
        Enum enumValue => Convert.ToDecimal(enumValue, System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    private static object? Evaluate(ProgramValue value, Context context)
    {
        try
        {
            return value switch
            {
                ConstantValue constant => (decimal)constant.Value,
                NullValue => null,
                NamedValue { Name: "this" } => context.Board.Card,
                NamedValue { Name: "cardPlay" } => ThePlay,
                NamedValue named when context.Parameters.TryGetValue(named.Name, out var bound) => bound,
                ParameterValue parameter => context.Parameters.TryGetValue(parameter.Name, out var bound) ? bound : Unknown,
                CardValue card => CardNumber(card.Key, context.Board),
                StoredValue stored => Evaluate(stored.Value, context),
                ConditionValue flag => Holds(flag.Condition, context) is { } holds ? holds : Unknown,
                WaysValue ways => FirstWay(ways, context),
                CompareValue compare => Compare(compare, context),
                ArithmeticValue arithmetic => Arithmetic(arithmetic, context),
                ArrayValue array => array.Items.Select(item => Evaluate(item, context)).ToList() is var items && items.Any(item => item == Unknown) ? Unknown : items,
                LambdaValue lambda => new Closure(lambda, context),
                QueryValue query => Query(query, context),
                _ => Unknown,
            };
        }
        catch
        {
            return Unknown; // a member that throws (the game's template card has no owner): not known
        }
    }

    /// <summary>The value of the first way whose condition holds; unknown when one before it can't be told.</summary>
    private static object? FirstWay(WaysValue ways, Context context)
    {
        foreach (var (when, value) in ways.Ways)
            switch (Holds(when, context))
            {
                case null: return Unknown;
                case true: return Evaluate(value, context);
            }
        return Unknown;
    }

    private static object CardNumber(string key, ProgramBoard board)
    {
        if (!board.Card.DynamicVars.TryGetValue(key, out var dynamicVar)) return Unknown;
        return dynamicVar is CalculatedVar calculated ? calculated.Calculate(board.Target) : dynamicVar.BaseValue;
    }

    private static object? Compare(CompareValue compare, Context context)
    {
        var left = Evaluate(compare.Left, context);
        var right = Evaluate(compare.Right, context);
        if (left == Unknown || right == Unknown) return Unknown;
        if (ToNumber(left) is { } leftNumber && ToNumber(right) is { } rightNumber)
        {
            // Unsigned: a negative number is above every positive one ((uint)(x - 3) <= 3 holds for 3 to 6 only).
            if (compare.Unsigned)
            {
                if (leftNumber < 0) leftNumber = decimal.MaxValue;
                if (rightNumber < 0) rightNumber = decimal.MaxValue;
            }
            return compare.Op switch
            {
                Comparison.Equal => leftNumber == rightNumber,
                Comparison.NotEqual => leftNumber != rightNumber,
                Comparison.Less => leftNumber < rightNumber,
                Comparison.LessOrEqual => leftNumber <= rightNumber,
                Comparison.Greater => leftNumber > rightNumber,
                _ => leftNumber >= rightNumber,
            };
        }
        var same = ReferenceEquals(left, right) || left != null && left.Equals(right);
        return compare.Op switch
        {
            Comparison.Equal => same,
            Comparison.NotEqual => !same,
            // x > null (cgt.un): x isn't null.
            Comparison.Greater when right == null => left != null,
            _ => Unknown,
        };
    }

    private static object Arithmetic(ArithmeticValue arithmetic, Context context)
    {
        if (ToNumber(Evaluate(arithmetic.Left, context)) is not { } left || ToNumber(Evaluate(arithmetic.Right, context)) is not { } right) return Unknown;
        return arithmetic.Op switch
        {
            "add" => left + right,
            "sub" => left - right,
            "mul" => left * right,
            "div" when right != 0 => decimal.Truncate(left / right),
            "rem" when right != 0 => left % right,
            _ => Unknown,
        };
    }

    // ---- queries

    private static object? Query(QueryValue query, Context context)
    {
        var asked = query.Of == null ? null : Evaluate(query.Of, context);
        if (asked == Unknown || asked != null && Forbidden(asked.GetType())) return Unknown;
        var board = context.Board;
        if (asked == ThePlay)
            return query.Name switch
            {
                "Target" => board.Target ?? Unknown,
                "Card" => board.Card,
                "Player" => board.Card.Owner,
                _ => Unknown,
            };
        var arguments = new object?[query.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = Evaluate(query.Arguments[i], context);
            if (arguments[i] == Unknown) return Unknown;
        }
        // Osty.CheckMissingWithAnim: Osty missing or dead (it plays an animation besides; never called).
        if (query.Of == null && query.Name == "CheckMissingWithAnim" && arguments is [Player player]) return player.IsOstyMissing;
        if (board.PlaysBefore > 0 && LaterPlay(query, asked, arguments, context) is var later && later != Unknown) return later;
        // x is T: x when it's a T, else null.
        if (query.Name == "is" && query.Of != null) return asked != null && IsOfType(asked.GetType(), query.Type) ? asked : null;
        if (query.Of == null && query.Type == "Enumerable") return Linq(query, arguments, context);
        // Math.Min, Max, Abs over numbers (Dredge's Min(Cards, your hand's room)).
        if (query.Of == null && query.Type == "Math")
            return (query.Name, arguments.Select(ToNumber).ToList()) switch
            {
                ("Min", [{ } left, { } right]) => Math.Min(left, right),
                ("Max", [{ } left, { } right]) => Math.Max(left, right),
                ("Abs", [{ } only]) => Math.Abs(only),
                _ => Unknown,
            };
        if (query.Of == null) return StaticMember(query, arguments);
        if (asked == null) return Unknown;
        var result = Member(asked, query, arguments);
        // The hand the play sees: the card played has left it (it's in play while its OnPlay runs).
        if (query.Name == "Cards" && asked is CardPile { Type: PileType.Hand } && result is IEnumerable<CardModel> cards)
            return cards.Where(card => !ReferenceEquals(card, board.Card)).ToList();
        return result;
    }

    /// <summary>
    /// On a replay's later play, a query whose answer the earlier plays change: the card's own property over the finished plays
    /// (read from its code), a count or test over CardPlaysFinished, and the arrow tests. Unknown when it's none of those.
    /// </summary>
    private static object? LaterPlay(QueryValue query, object? asked, object?[] arguments, Context context)
    {
        var board = context.Board;
        // The card's own property that reads the finished plays (Ftl.CanDrawCard, Fetch.HasBeenPlayedThisTurn): its code, with them.
        if (query is { IsProperty: true, Of: NamedValue { Name: "this" } } && ReferenceEquals(asked, board.Card)
            && OwnProperty(board.Card.GetType(), query.Name) is { } getter && ReadsFinishedPlays(getter))
            return Holds(getter, context) is { } holds ? holds : Unknown;
        if (query.Of == null && query.Type == "Enumerable" && query.Arguments.Count > 0 && query.Arguments[0].Parts().Any(part => part is QueryValue { Name: "CardPlaysFinished" }))
        {
            // The earlier plays of this card are finished plays of it, by its owner, this turn: what such a query counts.
            var now = Linq(query, arguments, context);
            return query.Name switch
            {
                "Count" when ToNumber(now) is { } count => count + board.PlaysBefore,
                "Any" => true,
                _ => Unknown,
            };
        }
        if (query.Of == null && query.Type == "ArrowCmd" && arguments.Length >= 1 && arguments[0] is Creature target && board.Card.Owner is { } owner)
        {
            // A test a loop of the play ran until it failed (Wrest pulls every arrow of yours out): it fails on the later plays.
            if (query.Name == "CreatureIsImpaledByPlayersArrow" && context.Program.All().OfType<LoopStep>()
                    .Any(loop => loop.While is { Terms: [[{ Holds: true } literal]] } && literal.Test.ToString() == query.ToString()))
                return false;
            if (query.Name is "CreatureIsImpaledByArrow" or "CreatureIsImpaledByPlayersArrow")
            {
                var yours = Fool.ImpaledBy(target, owner.Creature);
                var teammate = query.Name == "CreatureIsImpaledByArrow"
                               && target.Powers.Any(power => power.GetType().Name == nameof(MegaCrit.Sts2.Core.Models.Powers.ImpaledPower) && power.Applier != owner.Creature);
                var plays = SpireQoL.Sim.SimEngine.PlaysStartingImpaled(teammate, yours == null ? 0 : Fool.ArrowsIn(yours), yours == null ? 3 : Fool.HitCounter(yours),
                    HitsOnTarget(context), board.PlaysBefore + 1);
                return plays > board.PlaysBefore;
            }
        }
        return Unknown;
    }

    /// <summary>The play's hits on its target, from its attacks: one each with no hit count, else the count's value.</summary>
    private static int HitsOnTarget(Context context)
    {
        var hits = 0;
        foreach (var attack in context.Program.Commands.Where(command => command.Command is { DeclaringType: "DamageCmd", Name: "Attack" } && command.Builder.Contains("Targeting")))
            hits += attack.HitCount == null ? 1 : int.TryParse(attack.HitCount, out var constant) ? constant : Sts2Mods.Core.Game.Sts2State.Var(context.Board.Card, attack.HitCount, fallback: 1);
        return hits;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, string), ProgramCondition?> Getters = new();

    /// <summary>A bool property of the card's own type (or a base type's), read from its code; null when there's none or it can't be read.</summary>
    private static ProgramCondition? OwnProperty(Type cardType, string name) => Getters.GetOrAdd((cardType, name), key =>
    {
        try { return CardProgramReader.ReadProperty(key.Item1, key.Item2, RuntimeCode.Read); }
        catch { return null; }
    });

    private static bool ReadsFinishedPlays(ProgramCondition getter) =>
        getter.Literals.Any(literal => literal.Test.Parts().Any(part => part is QueryValue { Name: "CardPlaysFinished" }));

    private static object? Linq(QueryValue query, object?[] arguments, Context context)
    {
        if (arguments.Length == 0 || arguments[0] is not IEnumerable source) return Unknown;
        var items = source.Cast<object?>().ToList();
        var predicate = arguments.Length > 1 ? arguments[1] as Closure : null;
        bool? Test(object? item) => predicate == null ? true : Holds(predicate.Lambda.Body, predicate.Context with
        {
            Parameters = new Dictionary<string, object?>(predicate.Context.Parameters) { [predicate.Lambda.Parameter] = item },
        });
        List<object?>? Filtered()
        {
            var kept = new List<object?>();
            foreach (var item in items)
                switch (Test(item))
                {
                    case null: return null;
                    case true: kept.Add(item); break;
                }
            return kept;
        }
        switch (query.Name)
        {
            case "ToList" or "ToArray" or "AsEnumerable" or "Distinct":
                return items;
            case "Any":
                return Filtered() is { } any ? any.Count > 0 : Unknown;
            case "All":
                return Filtered() is { } all ? all.Count == items.Count : Unknown;
            case "Count":
                return Filtered() is { } counted ? (decimal)counted.Count : Unknown;
            case "Where":
                return Filtered() is { } where ? where : Unknown;
            case "FirstOrDefault":
                // The first of your draw pile's cards is its hidden order (CLAUDE.md, "Only knowable information"): unknown when more
                // than one could be first. Which one is never read; its count and contents are.
                return Filtered() is { } first && !OrderHidden(first) ? first.FirstOrDefault() : Unknown;
            case "Except" when arguments.Length > 1 && arguments[1] is IEnumerable others:
            {
                var left = others.Cast<object?>().ToList();
                return items.Where(item => !left.Any(other => ReferenceEquals(other, item))).ToList();
            }
            case "Contains" when arguments.Length > 1:
                return items.Any(item => ReferenceEquals(item, arguments[1]) || item != null && item.Equals(arguments[1])
                                         || ToNumber(item) is { } number && number == ToNumber(arguments[1]));
            case "OfType" when query.GenericArguments.Count == 1:
                return items.Where(item => item != null && IsOfType(item.GetType(), query.GenericArguments[0])).ToList();
            default:
                return Unknown;
        }
    }

    /// <summary>Two or more of these are in a draw pile: which comes first is the pile's hidden order.</summary>
    internal static bool OrderHidden(IReadOnlyList<object?> items)
    {
        var inDrawPile = 0;
        foreach (var item in items)
            if (item is CardModel { Pile.Type: PileType.Draw } && ++inDrawPile > 1) return true;
        return false;
    }

    private static bool IsOfType(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (current.Name == name) return true;
        return type.GetInterfaces().Any(face => face.Name == name);
    }

    /// <summary>A method that only asks (no change, no roll): what the evaluator may call.</summary>
    private static bool OnlyAsks(string name) =>
        name is "Contains" or "HappenedThisTurn" or "Equals"
        || (name.StartsWith("Is", StringComparison.Ordinal) || name.StartsWith("Has", StringComparison.Ordinal) || name.StartsWith("Can", StringComparison.Ordinal)
            || name.StartsWith("CreatureIs", StringComparison.Ordinal) || name.StartsWith("PlayerHas", StringComparison.Ordinal)
            || name.StartsWith("Get", StringComparison.Ordinal) && name is not ("GetEnumerator" or "GetAwaiter" or "GetResult"));

    /// <summary>
    /// The game's random numbers and the run (its seed): never read, not a property of them nor one that gives one (CLAUDE.md, "Only
    /// knowable information"). By type, so no name of theirs is written here (RngBanTests).
    /// </summary>
    private static bool Forbidden(Type type) =>
        type.Namespace == "MegaCrit.Sts2.Core.Random" || type.Name.Contains("RunState", StringComparison.Ordinal)
        || type.GetInterfaces().Any(face => face.Name.Contains("RunState", StringComparison.Ordinal));

    private const BindingFlags Everything = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

    private static object? Member(object asked, QueryValue query, object?[] arguments)
    {
        var type = asked.GetType();
        if (query.IsProperty)
        {
            for (var current = type; current != null; current = current.BaseType)
                if (current.GetProperty(query.Name, Everything | BindingFlags.DeclaredOnly) is { GetMethod: not null } property && property.GetIndexParameters().Length == 0)
                    return Forbidden(property.PropertyType) ? Unknown : property.GetValue(asked);
            return Unknown;
        }
        if (!OnlyAsks(query.Name)) return Unknown;
        return Invoke(type, asked, query, arguments);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Type?> GameTypes = new();

    private static object? StaticMember(QueryValue query, object?[] arguments)
    {
        var type = GameTypes.GetOrAdd(query.Type, name =>
        {
            try { return typeof(CardModel).Assembly.GetTypes().FirstOrDefault(candidate => candidate.Name == name && candidate.Namespace?.StartsWith("MegaCrit", StringComparison.Ordinal) == true); }
            catch { return null; }
        });
        if (type == null || Forbidden(type)) return Unknown;
        if (query.IsProperty)
            return type.GetProperty(query.Name, Everything) is { GetMethod: not null } property && !Forbidden(property.PropertyType) ? property.GetValue(null) : Unknown;
        if (!OnlyAsks(query.Name)) return Unknown;
        return Invoke(type, null, query, arguments);
    }

    private static object? Invoke(Type type, object? asked, QueryValue query, object?[] arguments)
    {
        foreach (var method in type.GetMethods(Everything).Where(method => method.Name == query.Name && method.GetParameters().Length == arguments.Length))
        {
            var target = method;
            if (method.IsGenericMethodDefinition)
            {
                if (method.GetGenericArguments().Length != query.GenericArguments.Count) continue;
                var generic = query.GenericArguments.Select(name => GameTypes.GetOrAdd(name, simple =>
                {
                    try { return typeof(CardModel).Assembly.GetTypes().FirstOrDefault(candidate => candidate.Name == simple); }
                    catch { return null; }
                })).ToArray();
                if (generic.Any(found => found == null)) return Unknown;
                target = method.MakeGenericMethod(generic!);
            }
            if (Forbidden(target.ReturnType) || target.GetParameters().Any(parameter => Forbidden(parameter.ParameterType))) return Unknown;
            var parameters = target.GetParameters();
            var converted = new object?[arguments.Length];
            var fits = true;
            for (var i = 0; i < arguments.Length && fits; i++)
                fits = ConvertTo(arguments[i], parameters[i].ParameterType, out converted[i]);
            if (fits) return target.Invoke(asked, converted);
        }
        return Unknown;
    }

    /// <summary>An evaluated argument as the parameter's type: numbers to enums and ints, lists to arrays.</summary>
    private static bool ConvertTo(object? value, Type to, out object? converted)
    {
        converted = value;
        if (value == null) return !to.IsValueType;
        if (to.IsInstanceOfType(value)) return true;
        if (ToNumber(value) is { } number)
        {
            if (to.IsEnum) { converted = Enum.ToObject(to, (long)number); return true; }
            if (to == typeof(int)) { converted = (int)number; return true; }
            if (to == typeof(decimal)) { converted = number; return true; }
            if (to == typeof(bool)) { converted = number != 0m; return true; }
        }
        if (to.IsArray && value is IEnumerable items && to.GetElementType() is { } element)
        {
            var list = items.Cast<object?>().ToList();
            var array = Array.CreateInstance(element, list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                if (!ConvertTo(list[i], element, out var item)) return false;
                array.SetValue(item, i);
            }
            converted = array;
            return true;
        }
        return false;
    }
}