namespace SpireQoL.Procs.Programs;

/// <summary>
/// A value a card's code works out, read from its instructions (decision #182): what a condition tests, an argument, a lambda's body.
/// Read from code only, never from the game's state.
/// </summary>
public abstract record ProgramValue
{
    /// <summary>The value's own query and every value inside it (receivers, arguments, lambda bodies, stored values).</summary>
    public virtual IEnumerable<ProgramValue> Parts() { yield return this; }

    /// <summary>
    /// The value with each part <paramref name="replace"/> gives a value for replaced (a helper's parameters by what its caller passed):
    /// a part it gives null for is kept, with its own parts mapped.
    /// </summary>
    public virtual ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) => replace(this) ?? this;
}

/// <summary>A constant in the code (a number, an enum's value, true as 1).</summary>
public sealed record ConstantValue(long Value) : ProgramValue
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A constant in the code that isn't a whole number (a float or double: a monster's move weight, 1f / 12f).</summary>
public sealed record RealValue(double Value) : ProgramValue
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>null.</summary>
public sealed record NullValue : ProgramValue
{
    public static readonly NullValue Instance = new();
    public override string ToString() => "null";
}

/// <summary>Something the code names directly: the card itself ("this"), a parameter of the play ("cardPlay"), a static type.</summary>
public sealed record NamedValue(string Name) : ProgramValue
{
    public static readonly NamedValue This = new("this");
    public override string ToString() => Name;
}

/// <summary>A lambda's parameter (Impatience's card c in your hand).</summary>
public sealed record ParameterValue(string Name) : ProgramValue
{
    public override string ToString() => Name;
}

/// <summary>A card value (DynamicVars.Cards: "Cards"; DynamicVars["PlayMax"]: "PlayMax").</summary>
public sealed record CardValue(string Key) : ProgramValue
{
    public override string ToString() => Key;
}

/// <summary>
/// A property or a method of the game's objects (CardModel.Type, ArrowCmd.CreatureIsImpaledByArrow(target), Enumerable.Any(cards,
/// predicate)): <paramref name="Of"/> is what it's asked of (null for a static method), <paramref name="ValueType"/> what it gives.
/// </summary>
public sealed record QueryValue(string Type, string Name, ProgramValue? Of, IReadOnlyList<ProgramValue> Arguments, string ValueType) : ProgramValue
{
    /// <summary>A property (read with no arguments), not a method.</summary>
    public bool IsProperty { get; init; }

    /// <summary>A generic method's type arguments (HasPower&lt;ArtifactPower&gt;: "ArtifactPower").</summary>
    public IReadOnlyList<string> GenericArguments { get; init; } = Array.Empty<string>();

    public override IEnumerable<ProgramValue> Parts()
    {
        yield return this;
        if (Of != null) foreach (var part in Of.Parts()) yield return part;
        foreach (var argument in Arguments) foreach (var part in argument.Parts()) yield return part;
    }

    public override ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) =>
        replace(this) ?? this with { Of = Of?.Map(replace), Arguments = Arguments.Select(argument => argument.Map(replace)).ToList() };

    public override string ToString()
    {
        if (Name == ".ctor") return "new " + Type + "(" + string.Join(", ", Arguments) + ")";
        if (Name == "is") return Of + " is " + Type;
        // A query's type arguments, except Enumerable's that only follow from what it's asked of (ToList, Where): OfType's and Cast's are a class it keeps.
        var name = (Of != null ? Of + "." + Name : Name) + (GenericArguments.Count > 0 && (Type != "Enumerable" || Name is "OfType" or "Cast") ? "<" + string.Join(",", GenericArguments) + ">" : "");
        return IsProperty ? name : name + "(" + string.Join(", ", Arguments) + ")";
    }
}

/// <summary>What a game command gave back (Pillage's CardPileCmd.Draw: the card it drew), awaited at <paramref name="Offset"/>.</summary>
public sealed record ResultValue(string Command, int Offset, IReadOnlyList<ProgramValue> Arguments) : ProgramValue
{
    public override IEnumerable<ProgramValue> Parts()
    {
        yield return this;
        foreach (var argument in Arguments) foreach (var part in argument.Parts()) yield return part;
    }

    public override ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) =>
        replace(this) ?? this with { Arguments = Arguments.Select(argument => argument.Map(replace)).ToList() };

    public override string ToString() => "result of " + Command;
}

/// <summary>
/// A local the code stores once and reads later (Ki Hop's wasImpaled, taken before its attack): what was stored, and where. A test
/// of it is a test of the board as it was there, not when it's read.
/// </summary>
public sealed record StoredValue(string Name, ProgramValue Value, int Offset) : ProgramValue
{
    public override IEnumerable<ProgramValue> Parts()
    {
        yield return this;
        foreach (var part in Value.Parts()) yield return part;
    }

    public override ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) => replace(this) ?? this with { Value = Value.Map(replace) };

    public override string ToString() => Name.Length == 0 ? Value.ToString()! : Name + "=" + Value;
}

/// <summary>
/// What a call gave back through its out parameter (Dictionary.Remove(cardPlay.Card, out var amount): the amount removed): the call (its
/// own value, with its receiver and arguments) and which of its parameters.
/// </summary>
public sealed record OutValue(ProgramValue Call, int Parameter) : ProgramValue
{
    public override IEnumerable<ProgramValue> Parts()
    {
        yield return this;
        foreach (var part in Call.Parts()) yield return part;
    }

    public override ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) => replace(this) ?? this with { Call = Call.Map(replace) };

    public override string ToString() => $"out {Parameter} of {Call}";
}

/// <summary>How two values compare.</summary>
public enum Comparison { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }

/// <summary>A comparison (x == 1, i &lt; Cards, x != null): true or false.</summary>
public sealed record CompareValue(Comparison Op, ProgramValue Left, ProgramValue Right) : ProgramValue
{
    /// <summary>Compared as unsigned numbers (a negative one is above every positive one: (uint)(x - 3) &lt;= 3).</summary>
    public bool Unsigned { get; init; }

    public override ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) => replace(this) ?? this with { Left = Left.Map(replace), Right = Right.Map(replace) };

    public override IEnumerable<ProgramValue> Parts()
    {
        yield return this;
        foreach (var part in Left.Parts()) yield return part;
        foreach (var part in Right.Parts()) yield return part;
    }

    public override string ToString() => $"{Left} {Symbol(Op)} {ProgramText.Constant(Left, Right)}";

    /// <summary>The comparison that holds when this one doesn't.</summary>
    public static Comparison Negated(Comparison comparison) => comparison switch
    {
        Comparison.Equal => Comparison.NotEqual,
        Comparison.NotEqual => Comparison.Equal,
        Comparison.Less => Comparison.GreaterOrEqual,
        Comparison.LessOrEqual => Comparison.Greater,
        Comparison.Greater => Comparison.LessOrEqual,
        _ => Comparison.Less,
    };

    private static string Symbol(Comparison comparison) => comparison switch
    {
        Comparison.Equal => "==",
        Comparison.NotEqual => "!=",
        Comparison.Less => "<",
        Comparison.LessOrEqual => "<=",
        Comparison.Greater => ">",
        _ => ">=",
    };
}

/// <summary>Arithmetic on two values (add, sub, mul, div, rem).</summary>
public sealed record ArithmeticValue(string Op, ProgramValue Left, ProgramValue Right) : ProgramValue
{
    public override IEnumerable<ProgramValue> Parts()
    {
        yield return this;
        foreach (var part in Left.Parts()) yield return part;
        foreach (var part in Right.Parts()) yield return part;
    }

    public override ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) => replace(this) ?? this with { Left = Left.Map(replace), Right = Right.Map(replace) };

    public override string ToString() => $"({Left} {Op} {Right})";
}

/// <summary>A lambda (a predicate given to Any, All, Where, Count...): its parameter and what it returns, as a condition.</summary>
public sealed record LambdaValue(string Parameter, ProgramCondition Body) : ProgramValue
{
    public override IEnumerable<ProgramValue> Parts()
    {
        yield return this;
        foreach (var literal in Body.Literals) foreach (var part in literal.Test.Parts()) yield return part;
    }

    public override ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) => replace(this) ?? this with { Body = Body.Map(replace) };

    public override string ToString() => Parameter + " => " + Body;
}

/// <summary>
/// A true-or-false the code works out on several ways (bool flag = a ? true : false; a local set in each branch): when it's true, as a
/// condition.
/// </summary>
public sealed record ConditionValue(ProgramCondition Condition) : ProgramValue
{
    public override IEnumerable<ProgramValue> Parts()
    {
        yield return this;
        foreach (var literal in Condition.Literals) foreach (var part in literal.Test.Parts()) yield return part;
    }

    public override ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) => replace(this) ?? this with { Condition = Condition.Map(replace) };

    public override string ToString() => "(" + Condition + ")";
}

/// <summary>
/// A value two or more ways through the code leave in one place (x = a ? 3 : 2; target?.GetPowerAmount()): each way's condition and value,
/// in the code's order. It's the value of the first way whose condition holds.
/// </summary>
public sealed record WaysValue(IReadOnlyList<(ProgramCondition When, ProgramValue Value)> Ways) : ProgramValue
{
    public override IEnumerable<ProgramValue> Parts()
    {
        yield return this;
        foreach (var (when, value) in Ways)
        {
            foreach (var literal in when.Literals) foreach (var part in literal.Test.Parts()) yield return part;
            foreach (var part in value.Parts()) yield return part;
        }
    }

    public override ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) =>
        replace(this) ?? this with { Ways = Ways.Select(way => (way.When.Map(replace), way.Value.Map(replace))).ToList() };

    public override string ToString() => "{" + string.Join("; ", Ways.Select(way => (way.When.IsAlways ? "" : way.When + ": ") + way.Value)) + "}";
}

/// <summary>An array the code builds (new[] { a, b }: a pile list given to a query).</summary>
public sealed record ArrayValue(IReadOnlyList<ProgramValue> Items) : ProgramValue
{
    public override IEnumerable<ProgramValue> Parts()
    {
        yield return this;
        foreach (var item in Items) foreach (var part in item.Parts()) yield return part;
    }

    public override ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) => replace(this) ?? this with { Items = Items.Select(item => item.Map(replace)).ToList() };

    public override string ToString() => "[" + string.Join(", ", Items) + "]";
}

/// <summary>
/// A value with fields the code set on it (a struct parameter returned after stores through its address: Rebound's location with its pileType
/// and position set), each field's value the last one stored.
/// </summary>
public sealed record FieldsSetValue(ProgramValue Of, IReadOnlyList<(string Field, ProgramValue Value)> Fields) : ProgramValue
{
    public override IEnumerable<ProgramValue> Parts()
    {
        yield return this;
        foreach (var part in Of.Parts()) yield return part;
        foreach (var (_, value) in Fields) foreach (var part in value.Parts()) yield return part;
    }

    public override ProgramValue Map(Func<ProgramValue, ProgramValue?> replace) =>
        replace(this) ?? this with { Of = Of.Map(replace), Fields = Fields.Select(set => (set.Field, set.Value.Map(replace))).ToList() };

    public override string ToString() => $"{Of} with {string.Join(", ", Fields.Select(set => $"{set.Field} = {set.Value}"))}";
}

/// <summary>Something the reader couldn't follow, named by what it saw.</summary>
public sealed record UnknownValue(string Text) : ProgramValue
{
    public override string ToString() => "?" + Text;
}

/// <summary>
/// One test a branch makes, and which way: <paramref name="Test"/> holds (or doesn't) at the branch <paramref name="At"/> (its method and
/// offset: two tests of the same thing at two places are two literals).
/// </summary>
public readonly record struct ConditionLiteral(string At, ProgramValue Test, bool Holds)
{
    public ConditionLiteral Negate() => this with { Holds = !Holds };

    /// <summary>The same branch and way (the test is the branch's own).</summary>
    public bool Equals(ConditionLiteral other) => At == other.At && Holds == other.Holds;

    public override int GetHashCode() => HashCode.Combine(At, Holds);

    public override string ToString() => Holds ? Test.ToString() : Test is CompareValue compare
        ? (compare with { Op = CompareValue.Negated(compare.Op) }).ToString()
        : "not " + Test;
}

/// <summary>
/// When a step runs: an or of ands of tests (each term the tests along one way through the code to it), simplified so that both ways
/// round an if that rejoin say nothing (a visual's null check, an if with an else). <see cref="Always"/> is no condition.
/// </summary>
public sealed class ProgramCondition
{
    private readonly IReadOnlyList<IReadOnlyList<ConditionLiteral>> _terms;

    private ProgramCondition(IReadOnlyList<IReadOnlyList<ConditionLiteral>> terms) => _terms = terms;

    /// <summary>Holds on every way: no condition.</summary>
    public static readonly ProgramCondition Always = new(new[] { (IReadOnlyList<ConditionLiteral>)Array.Empty<ConditionLiteral>() });

    /// <summary>Holds on none.</summary>
    public static readonly ProgramCondition Never = new(Array.Empty<IReadOnlyList<ConditionLiteral>>());

    public static ProgramCondition Of(ConditionLiteral literal) => new(new[] { (IReadOnlyList<ConditionLiteral>)new[] { literal } });

    /// <summary>The ways it holds: each a list of tests that all hold.</summary>
    public IReadOnlyList<IReadOnlyList<ConditionLiteral>> Terms => _terms;

    public bool IsAlways => _terms.Count == 1 && _terms[0].Count == 0;

    /// <summary>The same condition with each test's values mapped (Map on each).</summary>
    public ProgramCondition Map(Func<ProgramValue, ProgramValue?> replace) =>
        new(_terms.Select(term => (IReadOnlyList<ConditionLiteral>)term.Select(literal => literal with { Test = literal.Test.Map(replace) }).ToList()).ToList());
    public bool IsNever => _terms.Count == 0;

    /// <summary>Every test in it.</summary>
    public IEnumerable<ConditionLiteral> Literals => _terms.SelectMany(term => term);

    public ProgramCondition And(ProgramCondition other)
    {
        if (IsAlways) return other;
        if (other.IsAlways) return this;
        var terms = new List<IReadOnlyList<ConditionLiteral>>();
        foreach (var left in _terms)
            foreach (var right in other._terms)
                terms.Add(left.Concat(right).Distinct().ToList());
        return Simplified(terms);
    }

    public ProgramCondition And(ConditionLiteral literal) => And(Of(literal));

    /// <summary>When this doesn't hold: not any of its ways (each way fails on one of its tests).</summary>
    public ProgramCondition Not()
    {
        if (IsAlways) return Never;
        if (IsNever) return Always;
        var not = Always;
        foreach (var term in _terms)
            not = not.And(term.Aggregate(Never, (either, literal) => either.Or(Of(literal.Negate()))));
        return not;
    }

    public ProgramCondition Or(ProgramCondition other)
    {
        if (IsNever) return other;
        if (other.IsNever) return this;
        return Simplified(_terms.Concat(other._terms).ToList());
    }

    /// <summary>
    /// This condition inside <paramref name="outer"/> (a loop's body inside the loop's own condition): the tests the outer one makes on
    /// every way left out, as the loop step already carries them.
    /// </summary>
    public ProgramCondition Without(ProgramCondition? outer)
    {
        if (outer == null || outer.IsAlways || outer._terms.Count != 1) return this;
        var known = outer._terms[0];
        return Simplified(_terms.Select(term => (IReadOnlyList<ConditionLiteral>)term.Where(literal => !known.Contains(literal)).ToList()).ToList());
    }

    private static ProgramCondition Simplified(List<IReadOnlyList<ConditionLiteral>> terms)
    {
        var sets = terms.Select(term => new List<ConditionLiteral>(term.Distinct())).ToList();
        // A way that tests both ways of one branch is no way.
        sets.RemoveAll(term => term.Any(literal => term.Contains(literal.Negate())));
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < sets.Count && !changed; i++)
                for (var j = 0; j < sets.Count && !changed; j++)
                {
                    if (i == j) continue;
                    var first = sets[i];
                    var second = sets[j];
                    // Absorption: a way whose tests include all of another's adds nothing (a or a and b: a).
                    if (first.All(second.Contains) && (first.Count < second.Count || i < j))
                    {
                        sets.RemoveAt(j);
                        changed = true;
                        continue;
                    }
                    // a and l, or b and not l, where a's tests are in b's: b needn't test l (a or (not a and b): a or b).
                    foreach (var literal in first)
                    {
                        var negated = literal.Negate();
                        if (!second.Contains(negated) || !first.Where(other => !other.Equals(literal)).All(second.Contains)) continue;
                        sets[j] = second.Where(other => !other.Equals(negated)).ToList();
                        changed = true;
                        break;
                    }
                }
        }
        return new ProgramCondition(sets.Select(term => (IReadOnlyList<ConditionLiteral>)term.OrderBy(literal => literal.At, StringComparer.Ordinal).ToList()).ToList());
    }

    public override string ToString() => IsNever ? "never" : IsAlways ? "always"
        : string.Join(" or ", _terms.Select(term => term.Count == 1 || _terms.Count == 1 ? string.Join(" and ", term) : "(" + string.Join(" and ", term) + ")"));
}

/// <summary>How the reader writes a value.</summary>
internal static class ProgramText
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Type?> Enums = new();

    /// <summary>
    /// <paramref name="value"/> as the constant it's compared with: an enum's member name when the other side is that enum (Type ==
    /// Attack), else the value itself.
    /// </summary>
    public static string Constant(ProgramValue other, ProgramValue value)
    {
        if (value is not ConstantValue constant || other is not QueryValue { ValueType: { Length: > 0 } valueType }) return value.ToString()!;
        var type = Enums.GetOrAdd(valueType, name =>
        {
            try
            {
                return typeof(MegaCrit.Sts2.Core.Models.CardModel).Assembly.GetTypes()
                    .FirstOrDefault(candidate => candidate.IsEnum && candidate.Name == name && candidate.Namespace?.StartsWith("MegaCrit", StringComparison.Ordinal) == true);
            }
            catch { return null; }
        });
        if (type == null) return value.ToString()!;
        try { return Enum.ToObject(type, constant.Value).ToString() ?? value.ToString()!; }
        catch { return value.ToString()!; }
    }
}
