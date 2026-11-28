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
                    if (method.GetParameters().Length != 0 || method.IsGenericMethodDefinition || !method.Name.StartsWith("<GenerateMoveStateMachine>", StringComparison.Ordinal)) continue;
                    if (method.ReturnType == typeof(float))
                    {
                        // A random branch's weight (decision #411).
                        try { Weights.GetOrAdd(method, lambda => ProgramReader.ReadReturnedWays(monster, lambda, RuntimeCode.Read)); read++; }
                        catch { /* read on the fight's ask instead */ }
                        continue;
                    }
                    if (method.ReturnType != typeof(bool)) continue;
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

    private static SimCondition? Translate(ProgramCondition condition, MonsterModel monster, Type owner, int depth, bool ownState = false)
    {
        if (condition.IsAlways) return new KnownCondition(Holds: true);
        if (condition.IsNever) return new KnownCondition(Holds: false);
        var ors = new List<SimCondition>();
        foreach (var conjunction in condition.Terms)
        {
            var ands = new List<SimCondition>();
            foreach (var literal in conjunction)
            {
                if (Test(literal.Test, monster, owner, depth, ownState) is not { } test) return null;
                ands.Add(literal.Holds ? test : new NotCondition(test));
            }
            ors.Add(ands.Count == 1 ? ands[0] : new AllCondition(ands));
        }
        return ors.Count == 1 ? ors[0] : new AnyCondition(ors);
    }

    /// <summary>One test the lambda makes, as the simulator's: what it follows, else decided now; null when neither.</summary>
    private static SimCondition? Test(ProgramValue test, MonsterModel monster, Type owner, int depth, bool ownState = false)
    {
        switch (test)
        {
            // this.Creature.HasPower<T>(): its amount of the power above 0.
            case QueryValue { Name: "HasPower", GenericArguments: [var power] } has when IsItsCreature(has.Of):
                return new CountCondition(SimCounted.MonsterPower, SimCompare.Greater, Than: 0) { Power = PowerId.Of(power) };
            // A local the code stored (a power it fetched: Pheromone Spit's personalHivePower): its own power, tested for being there.
            case StoredValue stored when ItsPower(stored) is { } kept:
                return new CountCondition(SimCounted.MonsterPower, SimCompare.Greater, Than: 0) { Power = PowerId.Of(kept) };
            // Its own power fetched, tested for being there (Entomancer's Pheromone Spit: Powers.OfType<PersonalHivePower>().FirstOrDefault() not null).
            case var fetched when ItsPower(fetched) is { } held:
                return new CountCondition(SimCounted.MonsterPower, SimCompare.Greater, Than: 0) { Power = PowerId.Of(held) };
            // x == 0 of a true-or-false (the code's !x).
            case CompareValue { Op: Comparison.Equal or Comparison.NotEqual, Right: ConstantValue { Value: 0 } } flag when IsTrueOrFalse(flag.Left, monster, owner):
                return Test(flag.Left, monster, owner, depth, ownState) is { } inner ? flag.Op == Comparison.Equal ? new NotCondition(inner) : inner : null;
            // A number of its own its moves count (decision #411: Two-Tailed Rat's TurnsUntilSummonable <= 0, CallForBackupCount < 3), in its fields.
            case CompareValue compare when CounterKey(owner, compare.Left) is { } counter && Number(compare.Right, monster) is { } limit:
                return new CountCondition(SimCounted.MonsterCounter, Compared(compare.Op), Than: limit) { Field = counter };
            case CompareValue compare when CounterKey(owner, compare.Right) is { } counter && Number(compare.Left, monster) is { } limit:
                return new CountCondition(SimCounted.MonsterCounter, Compared(Swapped(compare.Op)), Than: limit) { Field = counter };
            // A true-or-false method of its own with no arguments (Two-Tailed Rat's CanSummon): what its code returns.
            case QueryValue { IsProperty: false, Of: NamedValue { Name: "this" }, Arguments.Count: 0, ValueType: nameof(Boolean) } call when depth < 2
                && OwnMethodCode(owner, call.Name) is { } body && Translate(body, monster, owner, depth + 1, ownState) is { } returned:
                return returned;
            // No slot of the encounter free (string.IsNullOrEmpty(Encounter?.GetNextSlot(CombatState)): Two-Tailed Rat's room to call for backup).
            case var slotTest when IsFreeSlotTest(slotTest):
                return new CountCondition(SimCounted.FreeSlots, SimCompare.Equal, Than: 0);
            // A teammate whose next move is one of its (Any of its side's creatures but itself, c.Monster.NextMove.Id.Equals("CALL_FOR_BACKUP_MOVE")).
            case QueryValue { Type: "Enumerable", Name: "Any", Arguments: [var items, LambdaValue lambda] } when Teammates(items) is { } excludingItself && NextMoveTested(lambda) is { } move:
                return new CountCondition(SimCounted.SideNextMove, SimCompare.Greater, Than: 0) { Move = move, ExcludingItself = excludingItself };
            case CompareValue compare when Counted(compare.Left) is { } counted && Number(compare.Right, monster) is { } than:
                return counted with { Compare = Compared(compare.Op), Than = than };
            case CompareValue compare when Counted(compare.Right) is { } counted && Number(compare.Left, monster) is { } than:
                return counted with { Compare = Compared(Swapped(compare.Op)), Than = than };
            // A move's step on a true-or-false of its own state a power's code sets (Bowlbug Rock's IsOffBalance, which Imbalanced sets): its state in the
            // simulated monster's fields (MonsterState, decision #271), seeded from the game's monster (StateFields).
            case QueryValue { IsProperty: true, Of: NamedValue { Name: "this" }, Name: var stateName } when ownState && OwnStateKey(owner, stateName) is { } key:
                return new CountCondition(SimCounted.MonsterState, SimCompare.NotEqual, Than: 0) { Field = key };
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

    /// <summary>
    /// No slot of the encounter free: string.IsNullOrEmpty of the encounter's next free slot (IsNextFreeSlot), as the code tests it before adding a creature
    /// there (Two-Tailed Rat's CanSummon and its Call for Backup).
    /// </summary>
    internal static bool IsFreeSlotTest(ProgramValue test) =>
        test is QueryValue { Type: "String", Name: "IsNullOrEmpty", Arguments: [var slot] } && IsNextFreeSlot(slot);

    /// <summary>
    /// The encounter's next slot no enemy stands in, as the code gets it: EncounterModel.GetNextSlot(CombatState) (whose code is Slots.FirstOrDefault(s =&gt;
    /// Enemies.All(c =&gt; c.SlotName != s))), or that pick made in place (Call for Backup's LastOrDefault over the encounter's Slots, testing every enemy's
    /// SlotName), kept in a local or not; empty when every slot is taken.
    /// </summary>
    internal static bool IsNextFreeSlot(ProgramValue slot)
    {
        var value = StepValues.Unstored(slot);
        if (slot.Parts().Any(part => part is QueryValue { Name: "GetNextSlot" })) return true;
        return value is QueryValue { Name: "FirstOrDefault" or "LastOrDefault", Arguments: [QueryValue { Name: "Slots", Of: QueryValue { Name: "Encounter" } }, LambdaValue lambda, ..] }
            && lambda.Parts().Any(part => part is QueryValue { Name: "All", Arguments: [QueryValue { Name: "Enemies" }, ..] })
            && lambda.Parts().Any(part => part is QueryValue { Name: "SlotName" });
    }

    /// <summary>
    /// A move step's condition (decision #271: Bowlbug Rock's Headbutt stunning itself when IsOffBalance) as the simulator's, asked of the monster as the
    /// step runs: what the branch conditions follow, and a true-or-false of its own with a public setter (its state a power's code can set on it, a
    /// MonsterStateStep) as that state in its fields; null when a test can't be translated.
    /// </summary>
    internal static SimCondition? OfStep(MonsterModel monster, ProgramCondition condition)
    {
        try { return Translate(condition, monster, monster.GetType(), depth: 0, ownState: true); }
        catch { return null; }
    }

    /// <summary>
    /// A monster's own true-or-false with a public setter, as a power's store names it ("Monster.BowlbugRock.IsOffBalance": ProgramReader.OwnersMonsterPrefix,
    /// the class that declares it): its key among a simulated monster's fields; null for anything else.
    /// </summary>
    internal static string? OwnStateKey(Type owner, string name) =>
        owner.GetProperty(name, BindingFlags.Instance | BindingFlags.Public) is { PropertyType: var type, SetMethod.IsPublic: true, DeclaringType: { } declaring } && type == typeof(bool)
            ? ProgramReader.OwnersMonsterPrefix + declaring.Name + "." + name : null;

    /// <summary>
    /// The monster's own state a move's actions ask (a WhenAction's MonsterState conditions), each as the game's monster has it now (1 or 0): what the
    /// simulated monster's fields start with.
    /// </summary>
    internal static NamedCounts StateFields(MonsterModel monster, IEnumerable<SimAction> actions, NamedCounts fields)
    {
        static IEnumerable<CountCondition> Counted(SimCondition? condition) => condition switch
        {
            CountCondition { Counted: SimCounted.MonsterState } counted => new[] { counted },
            NotCondition not => Counted(not.Inner),
            AllCondition all => all.Parts.SelectMany(Counted),
            AnyCondition any => any.Parts.SelectMany(Counted),
            _ => Array.Empty<CountCondition>(),
        };
        foreach (var counted in actions.OfType<WhenAction>().SelectMany(when => Counted(when.Condition)))
        {
            if (counted.Field is not { } field) continue;
            var name = field[(field.LastIndexOf('.') + 1)..];
            if (monster.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(monster) is bool holds) fields = fields.With(field, holds ? 1 : 0);
        }
        return fields;
    }

    /// <summary>A monster's own true-or-false methods' code (CanSummon), each read once; null where it has none of that name.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Owner, string Name), ProgramCondition?> Methods = new();

    private static ProgramCondition? OwnMethodCode(Type owner, string name) =>
        Methods.GetOrAdd((owner, name), key => OwnMethod(key.Owner, key.Name) is { } method ? ProgramReader.ReadReturned(key.Owner, method, RuntimeCode.Read) : null);

    private static MethodInfo? OwnMethod(Type owner, string name)
    {
        for (var type = owner; type != null && type != typeof(MonsterModel); type = type.BaseType)
            if (type.GetMethod(name, Any | BindingFlags.DeclaredOnly, Type.EmptyTypes) is { } method && method.ReturnType == typeof(bool)) return method;
        return null;
    }

    /// <summary>
    /// A number of the monster's own (an int property with a setter: this.TurnsUntilSummonable) as its key in an enemy's fields
    /// ("Monster.TwoTailedRat.TurnsUntilSummonable": the class declaring it), or null for anything else.
    /// </summary>
    internal static string? CounterKey(Type owner, ProgramValue value)
    {
        if (value is not QueryValue { IsProperty: true, Of: NamedValue { Name: "this" }, Name: var name }) return null;
        return owner.GetProperty(name, Any) is { PropertyType: var type, CanWrite: true, DeclaringType: { } declaring } && type == typeof(int)
            ? ProgramReader.OwnersMonsterPrefix + declaring.Name + "." + name : null;
    }

    /// <summary>
    /// The numbers of its own a monster class's move rules ask (decision #411: its conditional branches' and its random branches' weights' lambdas, and
    /// the true-or-false methods and properties of its own they call, two deep), by their keys: what the adapter seeds an enemy's fields with and its
    /// moves' stores keep. Read once per class.
    /// </summary>
    internal static IReadOnlySet<string> CounterKeys(Type monster) => Counters.GetOrAdd(monster, type =>
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        const BindingFlags declared = Any | BindingFlags.Static | BindingFlags.DeclaredOnly;
        void Walk(IEnumerable<ProgramValue> parts, int depth)
        {
            foreach (var part in parts)
            {
                if (CounterKey(type, part) is { } key) keys.Add(key);
                if (depth >= 2 || part is not QueryValue { Of: NamedValue { Name: "this" } } own) continue;
                var called = own.IsProperty
                    ? Getters.GetOrAdd((type, own.Name), getter => ProgramReader.ReadProperty(getter.Owner, getter.Name, RuntimeCode.Read))
                    : own.Arguments.Count == 0 ? OwnMethodCode(type, own.Name) : null;
                if (called != null) Walk(called.Literals.SelectMany(literal => literal.Test.Parts()).ToList(), depth + 1);
            }
        }
        try
        {
            foreach (var holder in new[] { type }.Concat(type.GetNestedTypes(BindingFlags.NonPublic)))
                foreach (var method in holder.GetMethods(declared))
                {
                    if (method.GetParameters().Length != 0 || method.IsGenericMethodDefinition || !method.Name.StartsWith("<GenerateMoveStateMachine>", StringComparison.Ordinal)) continue;
                    if (method.ReturnType == typeof(bool))
                        Walk(Read.GetOrAdd(method, lambda => ProgramReader.ReadReturned(type, lambda, RuntimeCode.Read)).Literals.SelectMany(literal => literal.Test.Parts()).ToList(), depth: 0);
                    else if (method.ReturnType == typeof(float))
                        Walk(Weights.GetOrAdd(method, lambda => ProgramReader.ReadReturnedWays(type, lambda, RuntimeCode.Read)).Parts().ToList(), depth: 0);
                }
        }
        catch
        {
            // a lambda that can't be read: what it asks isn't followed
        }
        return keys;
    });

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlySet<string>> Counters = new();

    /// <summary>Each random branch's weight lambda's code, read once (decision #411).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<MethodInfo, WaysValue> Weights = new();

    /// <summary>
    /// A random branch's weight lambda (RandomBranchState.StateWeight.weightLambda) as the simulator's (decision #411): each way its code returns, the
    /// condition it's reached on as the simulator's and the number it returns (Two-Tailed Rat's 1 while it can't summon, 1/12 when it can). Null when
    /// a way's weight isn't a number the code holds (a weight kept in a closure: AddBranch's constant), a condition can't be translated, or every
    /// condition is decided now: the weight is as the game's lambda gives it now.
    /// </summary>
    internal static IReadOnlyList<(SimCondition When, double Weight)>? WeightWays(MonsterModel monster, Delegate? lambda)
    {
        if (lambda == null) return null;
        try
        {
            var owner = monster.GetType();
            var ways = Weights.GetOrAdd(lambda.Method, method => ProgramReader.ReadReturnedWays(owner, method, RuntimeCode.Read));
            var read = new List<(SimCondition When, double Weight)>();
            foreach (var (when, value) in ways.Ways)
            {
                double? weight = value switch { ConstantValue constant => constant.Value, RealValue real => real.Value, _ => null };
                if (weight == null || Translate(when, monster, owner, depth: 0) is not { } condition) return null;
                read.Add((condition, weight.Value));
            }
            return read.Count == 0 || read.All(way => way.When is KnownCondition) ? null : read;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Its side's creatures as a lambda goes over them: GetTeammatesOf(this.Creature) (false), or Where(that, c =&gt; c != this.Creature) (true: without
    /// itself), a ToList of either, or one kept in a local; null for anything else.
    /// </summary>
    private static bool? Teammates(ProgramValue items)
    {
        if (items is StoredValue kept) items = kept.Value; // kept in a local first (CanSummon's list)
        if (items is QueryValue { Name: "ToList", Arguments: [var listed] }) items = listed;
        if (items is QueryValue { Name: "GetTeammatesOf", Arguments: [var whose] } && IsItsCreature(whose)) return false;
        if (items is not QueryValue { Name: "Where", Arguments: [QueryValue { Name: "GetTeammatesOf", Arguments: [var side] }, LambdaValue filter] } || !IsItsCreature(side)) return null;
        var literals = filter.Body.Terms.Count == 1 ? filter.Body.Terms[0] : null;
        return literals is [{ Test: CompareValue { Op: Comparison.NotEqual } other, Holds: true }] && IsItsCreature(other.Right) ? true : null;
    }

    /// <summary>The move id a lambda compares its creature's next move with (c.Monster.NextMove.Id.Equals("X"), or ==), or null.</summary>
    private static string? NextMoveTested(LambdaValue lambda)
    {
        if (lambda.Body.Terms.Count != 1 || lambda.Body.Terms[0] is not [{ Holds: true, Test: var test }]) return null;
        static bool IsNextMoveId(ProgramValue? value) =>
            value is QueryValue { Name: "Id", Of: QueryValue { Name: "NextMove", Of: QueryValue { Name: "Monster", Of: ParameterValue or NamedValue } } };
        static string? Text(ProgramValue value) => value is NamedValue { Name: var name } && name.Length > 1 && name[0] == '"' && name[^1] == '"' ? name[1..^1] : null;
        return test switch
        {
            QueryValue { Name: "Equals", Arguments: [var compared] } equals when IsNextMoveId(equals.Of) => Text(compared),
            CompareValue { Op: Comparison.Equal } same when IsNextMoveId(same.Left) => Text(same.Right),
            QueryValue { Name: "op_Equality", Arguments: [var left, var right] } when IsNextMoveId(left) => Text(right),
            _ => null,
        };
    }

    /// <summary>An enemy's fields with the numbers of its own its move rules ask (CounterKeys), each as the game's monster has it now (decision #411).</summary>
    internal static NamedCounts CounterFields(MonsterModel monster, NamedCounts fields)
    {
        foreach (var key in CounterKeys(monster.GetType()))
        {
            var name = key[(key.LastIndexOf('.') + 1)..];
            if (monster.GetType().GetProperty(name, Any)?.GetValue(monster) is int value) fields = fields.With(key, value);
        }
        return fields;
    }

    /// <summary>A count the simulator follows, on the monster's own state: its HP, its side's living creatures (with or without itself).</summary>
    private static CountCondition? Counted(ProgramValue value)
    {
        if (value is QueryValue { Name: "CurrentHp" } hp && IsItsCreature(hp.Of)) return new CountCondition(SimCounted.MonsterHp, SimCompare.Equal, Than: 0);
        // Its own amount of a power: this.Creature.GetPowerAmount<T>(), or the Amount of the power it fetched (Pheromone Spit's Personal Hive below 3).
        if (value is QueryValue { Name: "GetPowerAmount", GenericArguments: [var amountOf] } own && IsItsCreature(own.Of))
            return new CountCondition(SimCounted.MonsterPower, SimCompare.Equal, Than: 0) { Power = PowerId.Of(amountOf) };
        if (value is QueryValue { Name: "Amount", Of: { } fetched } && ItsPower(fetched) is { } held)
            return new CountCondition(SimCounted.MonsterPower, SimCompare.Equal, Than: 0) { Power = PowerId.Of(held) };
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

    /// <summary>
    /// The power class of one of its own powers the code fetches (this.Creature.GetPower&lt;T&gt;(), or this.Creature.Powers.OfType&lt;T&gt;().FirstOrDefault()), or null.
    /// </summary>
    private static string? ItsPower(ProgramValue value) => value switch
    {
        // Kept in a local first (Pheromone Spit's personalHivePower): what was stored.
        StoredValue stored => ItsPower(stored.Value),
        QueryValue { Name: "GetPower", GenericArguments: [var power] } get when IsItsCreature(get.Of) => power,
        QueryValue { Name: "FirstOrDefault", Arguments: [QueryValue { Name: "OfType", GenericArguments: [var power], Arguments: [QueryValue { Name: "Powers" } powers] }] }
            when IsItsCreature(powers.Of) => power,
        _ => null,
    };

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
