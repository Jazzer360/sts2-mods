using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Procs.Programs;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

/// <summary>
/// A monster's conditional move branches as simulator conditions (decision #195): each ConditionalBranch's lambda is read from its code
/// (ProgramReader.ReadReturned) and its tests translated, once for every monster that asks them, into what the simulator asks of a simulated
/// state as that monster: its HP against a number (Frog Knight's half HP: MonsterHp), a power it has (Lagavulin Matriarch's Asleep:
/// MonsterPower), its side's living creatures (Fabricator, Ovicopter, Living Shield: MonsterSideAlive); a property of its own is read from its
/// getter's code. A test the simulator doesn't follow (a flag of its own: Bowlbug Rock's IsOffBalance, Queen's HasAmalgamDied; the slot it
/// stands in) is decided as it stands when read (KnownCondition), as the game's lambda answers it then. Reads, never rolls.
/// </summary>
internal static class MoveConditions
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Each branch of a conditional state with its lambda (ConditionalBranch._conditionalLambda: null for a branch that always holds).</summary>
    internal static List<(string To, Delegate? Lambda)> BranchesOf(ConditionalBranchState state)
    {
        var list = new List<(string, Delegate?)>();
        if (typeof(ConditionalBranchState).GetProperty("States", Any)?.GetValue(state) is not System.Collections.IEnumerable branches) return list;
        foreach (var branch in branches)
        {
            var type = branch.GetType();
            var id = (string)(type.GetField("id", Any)?.GetValue(branch) ?? "");
            list.Add((id, Sts2State.Private<Delegate?>(branch, "_conditionalLambda", fallback: null)));
        }
        return list;
    }

    /// <summary>
    /// A branch's lambda as a simulator condition for <paramref name="monster"/>, or null when a test can't be translated nor told now. A
    /// branch with no lambda always holds.
    /// </summary>
    internal static SimCondition? Of(MonsterModel monster, Delegate? lambda)
    {
        if (lambda == null) return new KnownCondition(Holds: true);
        try
        {
            var owner = monster.GetType();
            var condition = Read.GetOrAdd(lambda.Method, method => ProgramReader.ReadReturned(owner, method, RuntimeCode.Read));
            return Translate(condition, monster, owner, depth: 0);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads every conditional branch's lambda of each monster class (a bool-returning method its move rules made: GenerateMoveStateMachine's
    /// lambdas, on the class or a closure of it) and the getters of its own properties they ask, into the caches Of asks, once a launch on the
    /// load's worker (decision #254): the first intent forecast of a fight otherwise read them on the game's thread. Code only, never a monster.
    /// </summary>
    /// <returns>How many were read.</returns>
    internal static int ReadAtLoad(IEnumerable<Type> monsters)
    {
        const BindingFlags declared = Any | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var read = 0;
        foreach (var monster in monsters)
        {
            if (monster.IsAbstract) continue;
            foreach (var owner in new[] { monster }.Concat(monster.GetNestedTypes(BindingFlags.NonPublic)))
                foreach (var method in owner.GetMethods(declared))
                {
                    if (method.ReturnType != typeof(bool) || method.GetParameters().Length != 0 || method.IsGenericMethodDefinition
                        || !method.Name.StartsWith("<GenerateMoveStateMachine>", StringComparison.Ordinal)) continue;
                    try
                    {
                        var condition = Read.GetOrAdd(method, lambda => ProgramReader.ReadReturned(monster, lambda, RuntimeCode.Read));
                        read++;
                        read += ReadGetters(condition, monster, depth: 0);
                    }
                    catch
                    {
                        // read on the fight's ask instead, as before
                    }
                }
        }
        return read;
    }

    /// <summary>The getters of the monster's own properties a condition asks (Translate follows them two deep), read into Getters.</summary>
    private static int ReadGetters(ProgramCondition condition, Type owner, int depth)
    {
        if (depth >= 2) return 0;
        var read = 0;
        foreach (var conjunction in condition.Terms)
            foreach (var literal in conjunction)
                foreach (var part in literal.Test.Parts())
                    if (part is QueryValue { IsProperty: true, Of: NamedValue { Name: "this" } } property
                        && Getters.GetOrAdd((owner, property.Name), key => ProgramReader.ReadProperty(key.Owner, key.Name, RuntimeCode.Read)) is { } getter)
                        read += 1 + ReadGetters(getter, owner, depth + 1);
        return read;
    }

    /// <summary>Each lambda's code read once (decision #31); what it's translated to reads the monster now, so that's worked out each time.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<MethodInfo, ProgramCondition> Read = new();

    /// <summary>A monster's own true-or-false properties' getters, each read once.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Owner, string Name), ProgramCondition?> Getters = new();

    private static SimCondition? Translate(ProgramCondition condition, MonsterModel monster, Type owner, int depth)
    {
        if (condition.IsAlways) return new KnownCondition(Holds: true);
        if (condition.IsNever) return new KnownCondition(Holds: false);
        var ors = new List<SimCondition>();
        foreach (var conjunction in condition.Terms)
        {
            var ands = new List<SimCondition>();
            foreach (var literal in conjunction)
            {
                if (Test(literal.Test, monster, owner, depth) is not { } test) return null;
                ands.Add(literal.Holds ? test : new NotCondition(test));
            }
            ors.Add(ands.Count == 1 ? ands[0] : new AllCondition(ands));
        }
        return ors.Count == 1 ? ors[0] : new AnyCondition(ors);
    }

    /// <summary>One test the lambda makes, as the simulator's: what it follows, else decided now; null when neither.</summary>
    private static SimCondition? Test(ProgramValue test, MonsterModel monster, Type owner, int depth)
    {
        switch (test)
        {
            // this.Creature.HasPower<T>(): its amount of the power above 0.
            case QueryValue { Name: "HasPower", GenericArguments: [var power] } has when IsItsCreature(has.Of):
                return new CountCondition(SimCounted.MonsterPower, SimCompare.Greater, Than: 0) { Power = PowerId.Of(power) };
            // x == 0 of a true-or-false (the code's !x).
            case CompareValue { Op: Comparison.Equal or Comparison.NotEqual, Right: ConstantValue { Value: 0 } } flag when IsTrueOrFalse(flag.Left, monster, owner):
                return Test(flag.Left, monster, owner, depth) is { } inner ? flag.Op == Comparison.Equal ? new NotCondition(inner) : inner : null;
            case CompareValue compare when Counted(compare.Left) is { } counted && Number(compare.Right, monster) is { } than:
                return counted with { Compare = Compared(compare.Op), Than = than };
            case CompareValue compare when Counted(compare.Right) is { } counted && Number(compare.Left, monster) is { } than:
                return counted with { Compare = Compared(Swapped(compare.Op)), Than = than };
            // A property of its own (Fabricator's CanFabricate): what its getter returns.
            case QueryValue { IsProperty: true, Of: NamedValue { Name: "this" } } property when depth < 2
                && Getters.GetOrAdd((owner, property.Name), key => ProgramReader.ReadProperty(key.Owner, key.Name, RuntimeCode.Read)) is { } getter
                && Translate(getter, monster, owner, depth + 1) is { } read:
                return read;
        }
        // A test the simulator doesn't follow: as it stands now.
        return ProgramEvaluator.Value(test, ProgramBoard.Of(monster), new ModelProgram(owner.Name, Array.Empty<ProgramStep>())) switch
        {
            bool holds => new KnownCondition(holds),
            decimal number => new KnownCondition(number != 0),
            int number => new KnownCondition(number != 0),
            _ => null,
        };
    }

    /// <summary>A count the simulator follows, on the monster's own state: its HP, its side's living creatures (with or without itself).</summary>
    private static CountCondition? Counted(ProgramValue value)
    {
        if (value is QueryValue { Name: "CurrentHp" } hp && IsItsCreature(hp.Of)) return new CountCondition(SimCounted.MonsterHp, SimCompare.Equal, Than: 0);
        // Count(GetTeammatesOf(this.Creature), c => c.IsAlive [and c != this.Creature]).
        if (value is QueryValue { Name: "Count", Type: "Enumerable", Arguments: [QueryValue { Name: "GetTeammatesOf", Arguments: [var whose] }, LambdaValue lambda] }
            && IsItsCreature(whose))
        {
            var literals = lambda.Body.Terms.Count == 1 ? lambda.Body.Terms[0] : null;
            if (literals == null) return null;
            var alive = literals.Any(literal => literal.Holds && literal.Test is QueryValue { Name: "IsAlive", Of: ParameterValue or NamedValue });
            var notItself = literals.Any(literal => literal.Test is CompareValue { Op: Comparison.NotEqual } other && IsItsCreature(other.Right) && literal.Holds
                                                    || literal.Test is CompareValue { Op: Comparison.Equal } same && IsItsCreature(same.Right) && !literal.Holds);
            if (!alive || literals.Count != (notItself ? 2 : 1)) return null;
            return new CountCondition(SimCounted.MonsterSideAlive, SimCompare.Equal, Than: 0) { ExcludingItself = notItself };
        }
        return null;
    }

    /// <summary>this.Creature: the monster's own creature.</summary>
    private static bool IsItsCreature(ProgramValue? value) => value is QueryValue { Name: nameof(MonsterModel.Creature), Of: NamedValue { Name: "this" } };

    private static bool IsTrueOrFalse(ProgramValue value, MonsterModel monster, Type owner) =>
        value is QueryValue { Name: "HasPower" } || value is QueryValue { ValueType: nameof(Boolean) }
        || ProgramEvaluator.Value(value, ProgramBoard.Of(monster), new ModelProgram(owner.Name, Array.Empty<ProgramStep>())) is bool;

    /// <summary>A number the code compares with, worked out now (MaxHp / 2: the max HP doesn't change during a turn).</summary>
    private static int? Number(ProgramValue value, MonsterModel monster) =>
        ProgramEvaluator.Value(value, ProgramBoard.Of(monster), new ModelProgram(monster.GetType().Name, Array.Empty<ProgramStep>())) switch
        {
            decimal number => (int)number,
            int number => number,
            _ => null,
        };

    private static SimCompare Compared(Comparison comparison) => comparison switch
    {
        Comparison.Equal => SimCompare.Equal,
        Comparison.NotEqual => SimCompare.NotEqual,
        Comparison.Less => SimCompare.Less,
        Comparison.LessOrEqual => SimCompare.LessOrEqual,
        Comparison.Greater => SimCompare.Greater,
        _ => SimCompare.GreaterOrEqual,
    };

    /// <summary>The comparison with its sides swapped (3 &lt; x is x &gt; 3).</summary>
    private static Comparison Swapped(Comparison comparison) => comparison switch
    {
        Comparison.Less => Comparison.Greater,
        Comparison.LessOrEqual => Comparison.GreaterOrEqual,
        Comparison.Greater => Comparison.Less,
        Comparison.GreaterOrEqual => Comparison.LessOrEqual,
        _ => comparison,
    };
}
