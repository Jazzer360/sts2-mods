using System.Reflection;
using System.Reflection.Emit;

namespace SpireQoL.Procs.Programs;

/// <summary>
/// One step of a game method (a card's play, a monster's move, a model's hook), in the order its code runs it (decisions #182, #195,
/// docs/CARD_PROGRAMS.md).
/// </summary>
public abstract record ProgramStep(int Offset)
{
    /// <summary>
    /// Runs only when this holds (the tests on the way to it: an if around it, an early return before it), relative to the loop it's
    /// in; null on every way.
    /// </summary>
    public ProgramCondition? Condition { get; init; }

    /// <summary>" if ..." when it has a condition.</summary>
    protected string When => Condition != null ? " if " + Condition : "";
}

/// <summary>
/// A game command the code calls (CreatureCmd.GainBlock, PowerCmd.Apply&lt;T&gt;, the attack DamageCmd.Attack starts and Execute
/// runs), with where each of its arguments comes from.
/// </summary>
public sealed record CommandStep(int Offset, CodeMember Command, IReadOnlyList<string> Arguments) : ProgramStep(Offset)
{
    /// <summary>Its arguments as values (what the code works out for each: a card value, a count, a constant), in order.</summary>
    public IReadOnlyList<ProgramValue> Values { get; init; } = Array.Empty<ProgramValue>();

    /// <summary>An attack's builder calls (WithHitCount, Targeting, TargetingAllOpponents, BeforeDamage...), in order.</summary>
    public IReadOnlyList<string> Builder { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Where an attack's WithHitCount gets its count ("HitCount", "Repeat", a constant, "ResolveEnergyXValue" for an X-cost card's X);
    /// null when its builder sets none (one hit).
    /// </summary>
    public string? HitCount { get; init; }

    /// <summary>WithHitCount's argument as a value (Dismantle's {target Vulnerable: HitCount; else 1}, a card's X); null with no hit count (one hit).</summary>
    public ProgramValue? HitCountValue { get; init; }

    /// <summary>An attack's Targeting argument as a value (the creature it's aimed at: the play's target, Memento Mori's least-HP enemy); null with none.</summary>
    public ProgramValue? TargetValue { get; init; }

    /// <summary>
    /// The steps of a delegate the command is given and runs (ArrowCmd.ShootArrowAtTarget runs Blossom Shot's attack after the
    /// shot): they follow the command.
    /// </summary>
    public IReadOnlyList<ProgramStep> Inner { get; init; } = Array.Empty<ProgramStep>();

    /// <summary>"CreatureCmd.GainBlock", "PowerCmd.Apply&lt;VulnerablePower&gt;".</summary>
    public string Name => Command.ToString();

    public override string ToString() => Name + (Inner.Count > 0 ? " [" + string.Join(", ", Inner) + "]" : "") + When;
}

/// <summary>A player's pick in the middle of the play (CardSelectCmd): which card is theirs to choose, not whether the play goes on.</summary>
public sealed record ChoiceStep(int Offset, CodeMember Command) : ProgramStep(Offset)
{
    /// <summary>Where each of its arguments comes from (whose pile, whose pick: "Owner" is you), as a command's.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    /// <summary>Its arguments as values (the selector's prefs: how many at most; whose pile).</summary>
    public IReadOnlyList<ProgramValue> Values { get; init; } = Array.Empty<ProgramValue>();

    public override string ToString() => "choice " + Command.Name + When;
}

/// <summary>
/// A card the code makes (CombatState.CreateCard&lt;T&gt;: Jest's Sadness, Mourning Blow's): not a change by itself (a command adds it to
/// a pile, or the card turns into it after the play), but what it makes, by type, for the rules that ask (CardEffects.MakesSadness).
/// </summary>
public sealed record CreateStep(int Offset, string Card) : ProgramStep(Offset)
{
    /// <summary>Its arguments as values (CreateCard&lt;T&gt;(owner): whose card it is).</summary>
    public IReadOnlyList<ProgramValue> Values { get; init; } = Array.Empty<ProgramValue>();

    public override string ToString() => "create " + Card + When;
}

/// <summary>A loop: its body runs once per round (a foreach over cards, a for up to a value, a do-while).</summary>
public sealed record LoopStep(int Offset, string Over, IReadOnlyList<ProgramStep> Body) : ProgramStep(Offset)
{
    /// <summary>
    /// What its code tests to go round again (i &lt; Cards, MoveNext, Pillage's drawn card being an Attack while your hand has room),
    /// besides the loop's own condition; null when it can't be read (a jump back with no test).
    /// </summary>
    public ProgramCondition? While { get; init; }

    /// <summary>What a foreach goes over, as a value (the hand's cards, a filter of them, a pick's result); null for other loops.</summary>
    public ProgramValue? Items { get; init; }

    public override string ToString() => (Over == "?" && While != null ? $"loop while {While}" : $"loop over {Over}") + $" [{string.Join(", ", Body)}]" + When;
}

/// <summary>
/// A store to the model's own state (decision #195): a field or a property of its class or a base class of the game's, set to a value
/// (Queen's HasAmalgamDied, a counter a power keeps). Not a command, but what a later read of that state reads (a monster's conditional
/// move branch, the model's next hook).
/// </summary>
public sealed record StoreStep(int Offset, string Field, ProgramValue Value) : ProgramStep(Offset)
{
    /// <summary>
    /// What a command of the play gave back that the store sets a property of (a ResultPrefix store: the power Imitation Learning's PowerCmd.Apply gave);
    /// null for any other store.
    /// </summary>
    public ResultValue? Of { get; init; }

    public override string ToString() => "store " + Field + " = " + Value + When;
}

/// <summary>
/// A change to the model's own data (its GetInternalData object, decision #195): a call on one of its collections whose result the code
/// doesn't use (amountsForPlayedCards.Add(cardPlay.Card, base.Amount), a Remove made only for its out value), with its arguments. A store
/// to one of its plain fields is a StoreStep ("Data.etherealCount").
/// </summary>
public sealed record DataStep(int Offset, string Data, string Operation, IReadOnlyList<ProgramValue> Values) : ProgramStep(Offset)
{
    public override string ToString() => $"data {Data}.{Operation}({string.Join(", ", Values)})" + When;
}

/// <summary>
/// A change to a list the hook is given, by the code that runs it (ModifyShuffleOrder's cards: PerfectFit's Remove(base.Card), Insert(0,
/// base.Card)), whose result isn't used: the parameter, the list's method and its arguments.
/// </summary>
public sealed record ParameterListStep(int Offset, string Parameter, string Operation, IReadOnlyList<ProgramValue> Values) : ProgramStep(Offset)
{
    public override string ToString() => $"{Parameter}.{Operation}({string.Join(", ", Values)})" + When;
}

/// <summary>
/// A power's or relic's code changing a card through the card's own method, not a command (MummifiedHand's cardModel?.SetToFreeThisTurn()): the
/// card it's called on and its arguments. Read for powers and relics only (a card's own play reads its changes apart).
/// </summary>
public sealed record CardActionStep(int Offset, CodeMember Action, ProgramValue Card) : ProgramStep(Offset)
{
    public IReadOnlyList<ProgramValue> Values { get; init; } = Array.Empty<ProgramValue>();
    public override string ToString() => $"card {Card}.{Action.Name}({string.Join(", ", Values)})" + When;
}

/// <summary>
/// A power's or relic's own Flash() (this.Flash()): what the game shows as it acts, at its place in the code. Not a change, but where the
/// play check holds a prediction to a flash (decision #199).
/// </summary>
public sealed record FlashCall(int Offset) : ProgramStep(Offset)
{
    public override string ToString() => "flash" + When;
}

/// <summary>Something the reader couldn't follow: the steps it holds or stands for aren't known.</summary>
public sealed record UnknownStep(int Offset, string Reason) : ProgramStep(Offset)
{
    public override string ToString() => "unknown: " + Reason + When;
}

/// <summary>
/// A game method as its code runs it (a card's play, a monster's move, a model's hook: decision #195): its steps in order, and what the
/// reader couldn't follow. <paramref name="Owner"/>: the class it was read from (the card, the monster, the power...).
/// </summary>
public sealed record ModelProgram(string Owner, IReadOnlyList<ProgramStep> Steps)
{
    public static readonly ModelProgram Empty = new("", Array.Empty<ProgramStep>());

    /// <summary>Every step, loops' bodies included, in order.</summary>
    public IEnumerable<ProgramStep> All() => Flatten(Steps);

    private static IEnumerable<ProgramStep> Flatten(IEnumerable<ProgramStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            if (step is LoopStep loop)
                foreach (var inner in Flatten(loop.Body)) yield return inner;
            if (step is CommandStep { Inner.Count: > 0 } command)
                foreach (var inner in Flatten(command.Inner)) yield return inner;
        }
    }

    public IEnumerable<CommandStep> Commands => All().OfType<CommandStep>();
    public IEnumerable<string> Unknowns => All().OfType<UnknownStep>().Select(step => step.Reason);

    public override string ToString() => Owner + ": " + string.Join(", ", Steps);
}

/// <summary>
/// The loaded game's programs of what you play or use, read once per type (decision #335): a card's OnPlay, a potion's OnUse as a play (its
/// target the play's: PotionPrograms.AsPlay), so every reading of a card's program (CardEffects, MadeCards, the step translation) reads a
/// potion's use the same way. And what they say about order.
/// </summary>
public static class CardPrograms
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, ModelProgram> Cache = new();

    /// <summary>The card's program, or a potion's use as a play (empty when its code can't be read).</summary>
    public static ModelProgram Of(Type cardType) => Cache.GetOrAdd(cardType, type =>
    {
        try
        {
            return typeof(MegaCrit.Sts2.Core.Models.PotionModel).IsAssignableFrom(type)
                ? PotionPrograms.AsPlay(ProgramReader.Read(type, RuntimeCode.Read, "OnUse"))
                : ProgramReader.Read(type, RuntimeCode.Read);
        }
        catch { return ModelProgram.Empty; }
    });

    /// <summary>
    /// Reads every given card's program into the cache: the game does it on a worker as the mod loads, so a fight never reads one on the game's
    /// thread (a card not read yet when a fight asks is read then, as before). Returns how many it read.
    /// </summary>
    public static int ReadAll(IEnumerable<Type> cardTypes)
    {
        var count = 0;
        foreach (var cardType in cardTypes)
        {
            Of(cardType);
            count++;
        }
        return count;
    }

    /// <summary>The program's first command whose name starts with <paramref name="first"/> comes before its first <paramref name="second"/>.</summary>
    internal static bool Before(ModelProgram program, string first, string second)
    {
        var firstAt = -1;
        var secondAt = -1;
        var index = 0;
        foreach (var command in program.Commands)
        {
            var name = command.Command.Name;
            if (firstAt < 0 && name.StartsWith(first, StringComparison.Ordinal)) firstAt = index;
            if (secondAt < 0 && (name.StartsWith(second, StringComparison.Ordinal) || name.EndsWith("And" + second, StringComparison.Ordinal))) secondAt = index;
            index++;
        }
        return firstAt >= 0 && secondAt >= 0 && firstAt <= secondAt;
    }
}

/// <summary>
/// The loaded game's potion programs (decisions #313, #335): each potion's OnUse read once per potion type, as a card's OnPlay is, and read as
/// a play: the creature it's used on (OnUse's target) is the play's target (cardPlay.Target), so the card translation reads whom each step acts
/// on the same way (the play's target, its player, its owner).
/// </summary>
public static class PotionPrograms
{
    /// <summary>The potion's use (its OnUse) as a play, or an empty program when its code can't be read.</summary>
    public static ModelProgram Of(Type potionType) => CardPrograms.Of(potionType);

    /// <summary>The play's target as a card's code reads it: cardPlay.Target.</summary>
    private static readonly QueryValue PlayTarget = new("CardPlay", "Target", new NamedValue("cardPlay"), Array.Empty<ProgramValue>(), "Creature") { IsProperty = true };

    /// <summary>
    /// A potion's OnUse read as a play (decision #335): its target parameter (a field of its state machine, or the parameter itself) is the play's
    /// target, in every value and in each argument's source ("target.Player" is "cardPlay.Target.Player").
    /// </summary>
    public static ModelProgram AsPlay(ModelProgram use) => use with { Steps = use.Steps.Select(AsPlay).ToList() };

    private static ProgramValue? Target(ProgramValue value) => value is NamedValue { Name: "target" } or ParameterValue { Name: "target" } ? PlayTarget : null;

    private static string Argument(string argument) =>
        argument == "target" ? "cardPlay.Target" : argument.StartsWith("target.", StringComparison.Ordinal) ? "cardPlay.Target" + argument["target".Length..] : argument;

    private static ProgramStep AsPlay(ProgramStep step)
    {
        var mapped = step with { Condition = step.Condition?.Map(Target) };
        return mapped switch
        {
            LoopStep loop => loop with { While = loop.While?.Map(Target), Items = loop.Items?.Map(Target), Body = loop.Body.Select(AsPlay).ToList() },
            ChoiceStep choice => choice with { Values = choice.Values.Select(value => value.Map(Target)).ToList(), Arguments = choice.Arguments.Select(Argument).ToList() },
            CreateStep create => create with { Values = create.Values.Select(value => value.Map(Target)).ToList() },
            StoreStep store => store with { Value = store.Value.Map(Target) },
            CommandStep command => command with
            {
                Arguments = command.Arguments.Select(Argument).ToList(), Values = command.Values.Select(value => value.Map(Target)).ToList(),
                Inner = command.Inner.Select(AsPlay).ToList(), HitCountValue = command.HitCountValue?.Map(Target), TargetValue = command.TargetValue?.Map(Target),
            },
            CardActionStep action => action with { Card = action.Card.Map(Target), Values = action.Values.Select(value => value.Map(Target)).ToList() },
            DataStep data => data with { Values = data.Values.Select(value => value.Map(Target)).ToList() },
            _ => mapped,
        };
    }
}

/// <summary>
/// Reads a card's OnPlay (its async state machine, the local functions it calls) into a <see cref="ModelProgram"/>. Works on any
/// branch's code: types are matched by name, and the instructions come from the reader it's given (the loaded game's module, or
/// an assembly file's metadata in the tests). Reads code only, never the game's state or its RNG.
/// </summary>
public static class ProgramReader
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const int MaxDepth = 3;

    /// <summary>Commands that only show or sound something: they change nothing.</summary>
    private static readonly HashSet<string> ShowOnlyTypes = new() { "Cmd", "SfxCmd", "VfxCmd", "TalkCmd", "ThinkCmd", "MelodyCmd" };

    public static ModelProgram Read(Type cardType, Func<MethodBase, List<CodeOp>> read, string method = "OnPlay")
    {
        var body = Body(cardType, method);
        if (body == null) return new ModelProgram(cardType.Name, Array.Empty<ProgramStep>());
        var steps = new Reader(cardType, read).Steps(body, depth: 0);
        return new ModelProgram(cardType.Name, steps);
    }

    /// <summary>
    /// A model's constructor (the one taking nothing), as its code runs it: its field initializers' stores (GlassOrb's _passiveVal = 4m), the
    /// fields a new instance starts with.
    /// </summary>
    public static ModelProgram ReadConstructor(Type owner, Func<MethodBase, List<CodeOp>> read)
    {
        var constructor = owner.GetConstructors(All).FirstOrDefault(candidate => !candidate.IsStatic && candidate.GetParameters().Length == 0);
        return constructor == null ? new ModelProgram(owner.Name, Array.Empty<ProgramStep>()) : new ModelProgram(owner.Name, new Reader(owner, read).Steps(constructor, depth: 0));
    }

    /// <summary>
    /// What a property of a card's type (or a base type's) returns, as a condition, read from its getter's code (Ftl.CanDrawCard: the plays
    /// finished this turn, fewer than PlayMax); null when there's no such property.
    /// </summary>
    public static ProgramCondition? ReadProperty(Type cardType, string property, Func<MethodBase, List<CodeOp>> read)
    {
        for (var type = cardType; type != null; type = type.BaseType)
            if (type.GetProperty(property, All) is { GetMethod: { } getter })
                return new Reader(cardType, read).Returned(getter);
        return null;
    }

    /// <summary>
    /// What a method of a model returns, as a condition (a monster's conditional move branch's lambda: Frog Knight's
    /// HasBeetleCharged || CurrentHp &gt;= MaxHp / 2), read with <paramref name="owner"/> as the model whose code it is.
    /// </summary>
    public static ProgramCondition ReadReturned(Type owner, MethodBase method, Func<MethodBase, List<CodeOp>> read) =>
        new Reader(owner, read).Returned(BodyOf(method));

    /// <summary>What a method of a model returns, as a value (Living Shield's GetAllyCount: a count of its living side but itself); null when no one way returns it.</summary>
    public static ProgramValue? ReadReturnedValue(Type owner, MethodBase method, Func<MethodBase, List<CodeOp>> read) =>
        new Reader(owner, read).ReturnedValue(BodyOf(method));

    /// <summary>
    /// What a method of a model returns on each way to a return (a Modify hook's early returns: Strength's 0 for another dealer, 0 for an
    /// unpowered hit, else base.Amount), in the code's order: the value of the first way whose condition holds (decision #195).
    /// </summary>
    public static WaysValue ReadReturnedWays(Type owner, MethodBase method, Func<MethodBase, List<CodeOp>> read) =>
        new Reader(owner, read).ReturnedWays(BodyOf(method));

    /// <summary>
    /// The value a method's out parameter holds at each of its returns (a Try hook's modifiedAmount), each way on the condition it's reached on;
    /// null when it has no out parameter of that name.
    /// </summary>
    public static WaysValue? ReadOutWays(Type owner, MethodBase method, string parameter, Func<MethodBase, List<CodeOp>> read) =>
        new Reader(owner, read).OutWays(BodyOf(method), parameter);

    /// <summary>
    /// The questions to the game's log of the fight (CombatManager.Instance.History) a model type's code asks (decision #191), each the value
    /// its code works out: read from every method of the type and of the types nested in it (its state machines, its lambdas), each the
    /// outermost value a call there works out that reads the log. One that uses a parameter of a helper of the type's own (Spite's
    /// LostHpThisTurn(creature)) is read where the helper is called, with what its caller passes.
    /// </summary>
    public static IReadOnlyList<(string Method, ProgramValue Query)> HistoryQueries(Type owner, Func<MethodBase, List<CodeOp>> read)
    {
        var found = new List<(string Method, ProgramValue Query)>();
        var reader = new Reader(owner, read);
        var methods = new List<(Type Type, MethodBase Method, List<CodeOp> Ops)>();
        foreach (var type in new[] { owner }.Concat(CardCode.NestedTypes(owner)))
            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                try { methods.Add((type, method, read(method))); }
                catch { /* no code to read */ }
            }
        static bool AsksTheLog(List<CodeOp> ops) => ops.Any(instruction => instruction.IsCall && instruction.Member is { Name: "get_History", DeclaringType: "CombatManager" });
        // A helper of the type's own that asks the log: read where it's called too, with what its caller passes.
        var helpers = methods.Where(entry => entry.Type == owner && !entry.Method.Name.StartsWith('<') && AsksTheLog(entry.Ops)
                && methods.Any(caller => caller.Method != entry.Method && caller.Ops.Any(instruction => instruction.IsCall && instruction.Member is { } called && called.DeclaringType == owner.Name && called.Name == entry.Method.Name)))
            .Select(entry => entry.Method.Name).ToHashSet();
        foreach (var (type, method, ops) in methods)
        {
            if (!AsksTheLog(ops) && !ops.Any(instruction => instruction.IsCall && instruction.Member is { } member
                    && (member.DeclaringType == owner.Name && helpers.Contains(member.Name) || reader.IsGameHelperAskingTheLog(member)))) continue;
            var own = type == owner && helpers.Contains(method.Name) ? method.GetParameters().Select(parameter => parameter.Name).ToHashSet() : new HashSet<string?>();
            var queries = new Dictionary<string, QueryValue>();
            foreach (var value in reader.HistoryValues(method, ops))
                foreach (var part in value.Parts())
                    if (part is QueryValue { Type: "Enumerable" } query && ReadsHistory(query)) queries.TryAdd(query.ToString(), query);
            // The last query of a chain (Where then Sum: the Sum), not the ones it's asked of; one that uses the helper's own parameters is
            // read where it's called.
            var sources = queries.Values.Where(query => query.Arguments.Count > 0).Select(query => query.Arguments[0].ToString()).ToHashSet();
            foreach (var (text, query) in queries)
                if (!sources.Contains(text)
                    && !query.Parts().Any(inner => inner is ParameterValue { Name: var name } && own.Contains(name) || inner is NamedValue { Name: var captured } && own.Contains(captured)))
                    found.Add((type.Name + "." + method.Name, query));
        }
        return found;
    }

    /// <summary>
    /// What each calculated value of a card multiplies its extra value by (CalculatedVar.WithMultiplier's lambda in its CanonicalVars: Finisher's
    /// Attacks finished this turn, Murder's cards drawn), by the value's name ("CalculatedHits", "CalculatedDamage"), as the lambda returns it.
    /// </summary>
    public static IReadOnlyDictionary<string, ProgramValue> CalculatedMultipliers(Type cardType, Func<MethodBase, List<CodeOp>> read)
    {
        var found = new Dictionary<string, ProgramValue>();
        for (var type = cardType; type != null && found.Count == 0; type = type.BaseType)
        {
            if (type.GetProperty("CanonicalVars", All) is not { GetMethod: { } getter }) continue;
            List<CodeOp> ops;
            try { ops = read(getter); }
            catch { break; }
            foreach (var (createdValue, returned) in new Reader(cardType, read).MultiplierValues(getter, ops))
            {
                if (createdValue is not QueryValue { Name: ".ctor" } created) continue;
                var name = created.Arguments.Count > 0 && created.Arguments[0] is NamedValue { Name: var text } && text.StartsWith('"') ? text.Trim('"')
                    : created.Type == "CalculatedDamageVar" ? "CalculatedDamage" : created.Type == "CalculatedBlockVar" ? "CalculatedBlock" : null;
                if (name != null) found.TryAdd(name, returned);
            }
        }
        return found;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Assembly, string), string> DynamicVarKeys = new();

    /// <summary>
    /// The key a property of DynamicVarSet looks its value up by, read from its getter's code (DynamicVars.Doom is _vars["DoomPower"]); the
    /// name itself when there's no such getter (an indexed key, or a key the code wrote).
    /// </summary>
    public static string DynamicVarKey(Assembly game, string property, Func<MethodBase, List<CodeOp>> read) => DynamicVarKeys.GetOrAdd((game, property), key =>
    {
        var set = key.Item1.GetType("MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVarSet");
        if (set?.GetProperty(key.Item2, BindingFlags.Instance | BindingFlags.Public)?.GetMethod is not { } getter) return key.Item2;
        try { return read(getter).FirstOrDefault(instruction => instruction.Text != null)?.Text ?? key.Item2; }
        catch { return key.Item2; }
    });

    /// <summary>A value reads the game's log of the fight (CombatManager.Instance.History).</summary>
    public static bool ReadsHistory(ProgramValue value) => value.Parts().Any(part => part is QueryValue { Name: "History", Type: "CombatManager" });

    /// <summary>
    /// The method of <paramref name="methods"/> a call names: its name and number of parameters, and among overloads that share both (DoomPower's
    /// GetDoomedCreatures(CombatSide) beside its static GetDoomedCreatures(IReadOnlyList&lt;Creature&gt;)), the one that's static or not as the call
    /// is, with the parameter types the call names. Null: none.
    /// </summary>
    internal static MethodInfo? Called(IEnumerable<MethodInfo> methods, CodeMember member)
    {
        var named = methods.Where(method => method.Name == member.Name && method.GetParameters().Length == member.ParameterCount).ToList();
        if (named.Count <= 1) return named.FirstOrDefault();
        var sameKind = named.Where(method => method.IsStatic != member.HasThis).ToList();
        if (sameKind.Count == 0) sameKind = named;
        if (sameKind.Count > 1 && member.ParameterTypes.Count == member.ParameterCount)
            return sameKind.FirstOrDefault(method => method.GetParameters().Select(parameter => parameter.ParameterType.Name).SequenceEqual(member.ParameterTypes, StringComparer.Ordinal)) ?? sameKind[0];
        return sameKind[0];
    }

    /// <summary>The method whose code runs: an async method's state machine's MoveNext, else the method itself.</summary>
    internal static MethodBase? Body(Type owner, string method) =>
        owner.GetMethods(All).FirstOrDefault(candidate => candidate.Name == method) is { } found ? BodyOf(found) : null;

    /// <summary>
    /// The code that runs for <paramref name="method"/>: an async method's state machine's MoveNext (the machine its
    /// AsyncStateMachine attribute names: each overload has its own), else the method itself.
    /// </summary>
    internal static MethodBase BodyOf(MethodBase method)
    {
        var machine = method.CustomAttributes.FirstOrDefault(attribute => attribute.AttributeType.Name == "AsyncStateMachineAttribute")?
            .ConstructorArguments.FirstOrDefault().Value as Type;
        return machine?.GetMethod("MoveNext", All) ?? method;
    }

    internal static bool IsCommand(CodeMember member) =>
        member.Kind == CodeMemberKind.Method && member.Namespace == "MegaCrit.Sts2.Core.Commands" && !ShowOnlyTypes.Contains(member.DeclaringType)
        && !ShowsOnly(member.Name) && !Asks(member.Name);

    /// <summary>
    /// A hook the code tells its listeners itself (Hook.AfterDiedToDoom, awaited by DoomPower.DoomKill): a static method of the game's Hook class
    /// that returns a task.
    /// </summary>
    internal static bool IsHookTold(CodeMember member) =>
        member is { Kind: CodeMemberKind.Method, HasThis: false, DeclaringType: "Hook", Namespace: "MegaCrit.Sts2.Core.Hooks" } && member.ValueType.StartsWith("Task", StringComparison.Ordinal);

    /// <summary>
    /// A power's own action the card runs, awaited (Outbreak's PoisonPower.Trigger on each enemy's poison): a method of a power that
    /// returns a task, not a question (Get..., Is...). It's a step like a command, named by its power and method.
    /// </summary>
    internal static bool IsPowerAction(CodeMember member) =>
        member is { Kind: CodeMemberKind.Method, HasThis: true, Namespace: "MegaCrit.Sts2.Core.Models.Powers" } && member.ValueType.StartsWith("Task", StringComparison.Ordinal)
        && !Asks(member.Name) && !member.Name.StartsWith("get_", StringComparison.Ordinal);

    /// <summary>
    /// What a monster does to its own move rules or itself through its base class (MonsterModel.SetMoveImmediate: Queen's Enrage when her
    /// Amalgam dies): an instance method of MonsterModel that isn't a question, a property or a show. A step like a command, named by its
    /// class and method.
    /// </summary>
    internal static bool IsModelAction(CodeMember member) =>
        member is { Kind: CodeMemberKind.Method, HasThis: true, DeclaringType: nameof(MegaCrit.Sts2.Core.Models.MonsterModel), Namespace: "MegaCrit.Sts2.Core.Models" }
        && (member.ValueType == typeof(void).Name || member.ValueType == nameof(Task)) && !member.Name.StartsWith('<')
        && !member.Name.StartsWith("get_", StringComparison.Ordinal) && !member.Name.StartsWith("set_", StringComparison.Ordinal) && !Asks(member.Name) && !ShowsOnly(member.Name);

    /// <summary>
    /// A card's own action (CardModel.SetToFreeThisTurn): an instance method of CardModel that returns nothing or a task and isn't a question, a
    /// property or a show.
    /// </summary>
    internal static bool IsCardAction(CodeMember member) =>
        member is { Kind: CodeMemberKind.Method, HasThis: true, DeclaringType: nameof(MegaCrit.Sts2.Core.Models.CardModel), Namespace: "MegaCrit.Sts2.Core.Models" }
        && (member.ValueType == typeof(void).Name || member.ValueType == nameof(Task)) && !member.Name.StartsWith('<')
        && !member.Name.StartsWith("get_", StringComparison.Ordinal) && !member.Name.StartsWith("set_", StringComparison.Ordinal) && !Asks(member.Name) && !ShowsOnly(member.Name);

    /// <summary>The prefix of a model's own data's plain fields among its state ("Data.etherealCount").</summary>
    public const string OwnDataPrefix = "Data.";

    /// <summary>The prefix of a store to one of the model's own values ("DynamicVars.Damage", "DynamicVars.\"CardsLeft\"").</summary>
    public const string OwnVarPrefix = "DynamicVars.";

    /// <summary>The prefix of a property set on an object a hook is given ("Given.arrow.Status": a property set on an object a hook is given), never the model's own state.</summary>
    public const string GivenPrefix = "Given.";

    /// <summary>
    /// The prefix of a property a card's play sets on what one of its commands gave back ("Result.PlayerTarget": the power Imitation Learning's
    /// PowerCmd.Apply gave, aimed at the play's target's player: decision #322), never the card's own state.
    /// </summary>
    public const string ResultPrefix = "Result.";

    /// <summary>
    /// The prefix of a property set on its owner's monster as its class ("Monster.CorpseSlug.IsRavenous": ((CorpseSlug)base.Owner.Monster).IsRavenous =
    /// true), the monster's own state, never the model's.
    /// </summary>
    public const string OwnersMonsterPrefix = "Monster.";

    /// <summary>
    /// What a command gave back that the value is where the code is (<paramref name="reached"/>: the tests on the way there), kept in a local or not
    /// (a power PowerCmd.Apply gave); of a local stored on several ways (Imitation Learning's power: the one it found, else the one it applied), the
    /// one way the code there can be on. Null for anything else.
    /// </summary>
    private static ResultValue? Received(ProgramValue value, ProgramCondition reached) => value switch
    {
        StoredValue kept => Received(kept.Value, reached),
        ResultValue result => result,
        WaysValue ways when ways.Ways.Where(way => !ConditionReader.Exclusive(way.When, reached)).ToList() is [var only] => Received(only.Value, reached),
        _ => null,
    };

    /// <summary>
    /// Its owner's monster (base.Owner.Monster), cast to its class ((CorpseSlug)base.Owner.Monster) or tested as it and kept (base.Owner.Monster is
    /// BowlbugRock bowlbugRock: Imbalanced's), in a local or kept across awaits.
    /// </summary>
    private static bool IsOwnersMonster(ProgramValue value) => value switch
    {
        StoredValue kept => IsOwnersMonster(kept.Value),
        QueryValue { Name: "is", Of: { } tested } => IsOwnersMonster(tested),
        QueryValue { Name: "Monster", Of: QueryValue { Name: "Owner", Of: NamedValue { Name: "this" } } } => true,
        _ => false,
    };

    /// <summary>
    /// The monster of a creature the hook is given, where every way the code reaches the call tests that creature is its owner (Steam Eruption's
    /// ((WaterfallGiant)creature.Monster).TriggerAboutToBlowState() under creature == base.Owner; Burrowed's target.Monster kept and tested as a
    /// Tunneler, then tunneler.GetStunned() under target == base.Owner): its owner's monster there.
    /// </summary>
    private static bool IsGivenOwnersMonster(ProgramValue value, ProgramCondition reached)
    {
        static ProgramValue Monster(ProgramValue candidate) => candidate switch
        {
            StoredValue kept => Monster(kept.Value),
            QueryValue { Name: "is", Of: { } tested } => Monster(tested),
            _ => candidate,
        };
        var given = Monster(value) switch
        {
            QueryValue { Name: "Monster", Of: ParameterValue parameter } => parameter.Name,
            QueryValue { Name: "Monster", Of: NamedValue { Name: var named } } when named != "this" => named,
            _ => null,
        };
        if (given == null || reached.IsNever || reached.IsAlways) return false;
        static bool IsGiven(ProgramValue side, string name) => side is ParameterValue { Name: var parameter } && parameter == name || side is NamedValue { Name: var named } && named == name;
        static bool IsTheOwner(ProgramValue side) => side is QueryValue { Name: "Owner", Of: NamedValue { Name: "this" } };
        bool SaysOwner(ConditionLiteral literal) => literal.Test is CompareValue { Op: Comparison.Equal or Comparison.NotEqual } compare
            && (compare.Op == Comparison.Equal) == literal.Holds
            && (IsGiven(compare.Left, given) && IsTheOwner(compare.Right) || IsTheOwner(compare.Left) && IsGiven(compare.Right, given));
        return reached.Terms.All(term => term.Any(SaysOwner));
    }

    /// <summary>The prefix of a property set on a monster the hook makes before it joins ("Made.Wriggler.StartStunned"), set up for its CreatureCmd.Add.</summary>
    public const string MadeMonsterPrefix = "Made.";

    /// <summary>The prefix of a property its owner's monster's method sets on its own creature ("Creature.HpDisplay").</summary>
    public const string OwnCreaturePrefix = "Creature.";

    /// <summary>The prefix of a collection the code makes itself, in a local ("Local.wrigglers"), changed by a DataStep.</summary>
    public const string LocalPrefix = "Local.";

    /// <summary>
    /// The class of a monster the code makes to add to the fight ((Wriggler)ModelDb.Monster&lt;Wriggler&gt;().ToMutable(), in a local or not), or null.
    /// </summary>
    public static string? MadeMonster(ProgramValue? value) => value switch
    {
        StoredValue kept => MadeMonster(kept.Value),
        QueryValue { Name: "ToMutable", Of: QueryValue { Name: "Monster", Type: "ModelDb", GenericArguments: [var monster] } } => monster,
        _ => null,
    };

    /// <summary>
    /// One of the model's own values a value names (base.DynamicVars.Damage, base.DynamicVars["CardsLeft"], kept in a local or not): the
    /// property, or the key as the code writes it ("\"CardsLeft\""); null for anything else.
    /// </summary>
    public static string? OwnVarName(ProgramValue? value) => value switch
    {
        StoredValue stored => OwnVarName(stored.Value),
        QueryValue { Type: "DynamicVarSet", Name: "Item", Arguments: [NamedValue key], Of: QueryValue { Name: "DynamicVars", Of: NamedValue { Name: "this" } } } => key.Name,
        QueryValue { Type: "DynamicVarSet", Name: var property, Arguments.Count: 0, Of: QueryValue { Name: "DynamicVars", Of: NamedValue { Name: "this" } } } => property,
        _ => null,
    };

    /// <summary>The model's own data: what its GetInternalData gives (this.GetInternalData&lt;Data&gt;(), or a local it's kept in).</summary>
    public static bool IsOwnData(ProgramValue? value) => value switch
    {
        StoredValue stored => IsOwnData(stored.Value),
        QueryValue { Name: "GetInternalData", Of: NamedValue { Name: "this" } } => true,
        _ => false,
    };

    private static bool ShowsOnly(string name) =>
        name.StartsWith("Preview", StringComparison.Ordinal) || name.StartsWith("TriggerAnim", StringComparison.Ordinal) || name.Contains("Vfx")
        || name.Contains("Wait") || name.StartsWith("Shake", StringComparison.Ordinal) || name.StartsWith("Play", StringComparison.Ordinal) && name.EndsWith("Anim", StringComparison.Ordinal);

    /// <summary>A question to the game (ArrowCmd.PlayerHasAnyArrows), not a change.</summary>
    private static bool Asks(string name) =>
        name.StartsWith("Is", StringComparison.Ordinal) || name.StartsWith("Has", StringComparison.Ordinal) || name.StartsWith("PlayerHas", StringComparison.Ordinal)
        || name.StartsWith("CreatureIs", StringComparison.Ordinal) || name.StartsWith("Find", StringComparison.Ordinal) || name.StartsWith("Get", StringComparison.Ordinal);

    /// <summary>Whether a static game helper asks the fight's log or calls another helper of its type (ProgramReader.Reader.AsksTheLog).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Owner, string Name, int Parameters), bool> HelperAsks = new();

    /// <summary>A step with the values in its conditions, its values and its inner steps mapped (a delegate's this by its target: decision #307).</summary>
    public static ProgramStep BoundStep(ProgramStep step, Func<ProgramValue, ProgramValue?> replace) => Reader.Bound(step, replace);

    private sealed class Reader
    {
        private readonly Type _card;
        private readonly Func<MethodBase, List<CodeOp>> _read;

        /// <summary>How deep in game helpers this reader is (a static helper of the game's it follows for a question to the fight's log).</summary>
        private readonly int _helperDepth;

        public Reader(Type card, Func<MethodBase, List<CodeOp>> read, int helperDepth = 0)
        {
            _card = card;
            _read = read;
            _helperDepth = helperDepth;
        }

        /// <summary>The key a property of DynamicVarSet looks its value up by, read from its getter in this code's game (ProgramReader.DynamicVarKey).</summary>
        private string VarKeyOf(string property) => DynamicVarKey(_card.Assembly, property, _read);

        /// <summary>
        /// A method of a monster's class read where its owner's power calls it on that monster (decision #275): a property it sets on its own creature
        /// (base.Creature.HpDisplay) is a store, "Creature.HpDisplay", the power's owner's.
        /// </summary>
        public bool ForOwnersPower { get; init; }

        public List<ProgramStep> Steps(MethodBase method, int depth)
        {
            List<CodeOp> ops;
            try { ops = _read(method); }
            catch (Exception ex) { return new List<ProgramStep> { new UnknownStep(0, $"{method.Name} can't be read ({ex.GetType().Name})") }; }
            var flow = Flow.Of(ops, method, depth < MaxDepth ? LambdaOf : null, depth < MaxDepth ? HelperValue : null, VarKeyOf);
            var steps = new List<ProgramStep>();
            Parse(flow, 0, flow.Ops.Count, steps, outer: null, depth);
            return steps;
        }

        /// <summary>What a method returns, as a condition (a getter's value).</summary>
        public ProgramCondition Returned(MethodBase method) => Flow.Of(_read(method), method, LambdaOf, HelperValue, VarKeyOf).Returned();

        /// <summary>What a method returns, as a value (null when no one way returns it).</summary>
        public ProgramValue? ReturnedValue(MethodBase method) => Flow.Of(_read(method), method, LambdaOf, HelperValue, VarKeyOf).ReturnedValue();

        /// <summary>What a method returns on each way to a return, in order.</summary>
        public WaysValue ReturnedWays(MethodBase method) => Flow.Of(_read(method), method, LambdaOf, HelperValue, VarKeyOf).ReturnedWays();

        public WaysValue? OutWays(MethodBase method, string parameter) => Flow.Of(_read(method), method, LambdaOf, HelperValue, VarKeyOf).OutWays(parameter);

        /// <summary>
        /// Each WithMultiplier call in a method's code (a card's CanonicalVars): the calculated value it's asked of, and what its lambda
        /// returns, read from the lambda's own code (its own lambdas followed).
        /// </summary>
        public List<(ProgramValue Created, ProgramValue Returned)> MultiplierValues(MethodBase method, List<CodeOp> ops)
        {
            var flow = Flow.Of(ops, method, LambdaOf, HelperValue, VarKeyOf);
            var values = new List<(ProgramValue, ProgramValue)>();
            for (var i = 0; i < flow.Ops.Count; i++)
            {
                if (!flow.Ops[i].IsCall || flow.Ops[i].Member is not { Name: "WithMultiplier" }) continue;
                try
                {
                    if (flow.ValueBefore(i + 1) is not QueryValue { Of: { } created }) continue;
                    CodeMember? pointer = null;
                    for (var back = i - 1; back >= 0 && back >= i - 8 && pointer == null; back--)
                        if (flow.Ops[back].OpCode == OpCodes.Ldftn) pointer = flow.Ops[back].Member;
                    if (pointer == null || Lambda(pointer) is not { } body) continue;
                    if (Flow.Of(_read(body), body, LambdaOf, HelperValue, VarKeyOf).ReturnedValue() is { } returned) values.Add((created, returned));
                }
                catch
                {
                    // not read
                }
            }
            return values;
        }

        /// <summary>The values the calls in a method's code work out that read the game's log of the fight (decision #191).</summary>
        public List<ProgramValue> HistoryValues(MethodBase method, List<CodeOp> ops)
        {
            var flow = Flow.Of(ops, method, LambdaOf, HelperValue, VarKeyOf);
            var values = new List<ProgramValue>();
            for (var i = 0; i < flow.Ops.Count; i++)
            {
                if (!flow.Ops[i].IsCall || flow.Ops[i].Member is not { ReturnsValue: true }) continue;
                ProgramValue value;
                try { value = flow.ValueBefore(i + 1); }
                catch { continue; }
                if (ReadsHistory(value)) values.Add(value);
            }
            return values;
        }

        /// <summary>
        /// What a helper of the card's own (not async: SecondWind.GetCards, FlakCannon.GetStatuses) returns, from its one way to a return,
        /// with its parameters as the call passes them; null when it isn't one or can't be read.
        /// </summary>
        private ProgramValue? HelperValue(CodeMember member, IReadOnlyList<ProgramValue> passed)
        {
            // A static helper of the game's that asks the fight's log (MelodyCmd.PlayedChoThisTurn(Owner): PlayedThisTurn(player, Cho)): what it
            // returns, with its parameters bound to what the call passes (decision #191).
            if (member is { Kind: CodeMemberKind.Method, ReturnsValue: true, HasThis: false } && member.DeclaringType != _card.Name && _helperDepth < 2
                && member.Namespace.StartsWith("MegaCrit.Sts2.Core", StringComparison.Ordinal) && !member.ValueType.StartsWith("Task", StringComparison.Ordinal)
                && _card.Assembly.GetType(member.Namespace + "." + member.DeclaringType) is { } helperOwner && IsGameHelperAskingTheLog(member))
            {
                var helped = new Reader(helperOwner, _read, _helperDepth + 1).HelperValue(member, passed);
                return helped != null && ReadsHistory(helped) ? helped : null;
            }
            if (member.Kind != CodeMemberKind.Method || !member.ReturnsValue || member.Name.StartsWith("get_", StringComparison.Ordinal) || member.DeclaringType != _card.Name) return null;
            if (Called(_card.GetMethods(All), member) is not MethodInfo found
                || BodyOf(found) != found) return null;
            try
            {
                var flow = Flow.Of(_read(found), found, LambdaOf, helperOf: _helperDepth is > 0 and < 3 ? new Reader(_card, _read, _helperDepth + 1).HelperValue : null, varKeyOf: VarKeyOf);
                // Two ways to a return that are a ?. chain (Frantic Escape's CombatState?.Enemies.FirstOrDefault(...): null where its CombatState isn't
                // there): both, each on its condition (decision #322), where its value is read. Only that shape: any other helper's ways stay unread.
                var returned = flow.ReturnedValue() ?? (flow.ReturnedWays() is { Ways.Count: 2 } ways && ConditionReader.Chained(ways) is { } chained && !chained.Parts().Any(part => part is UnknownValue) ? ways : null);
                if (returned == null) return null;
                var names = found.GetParameters();
                // A parameter, or a lambda's capture of it (a closure's field named after it: Spite's creature in its Any).
                return returned.Map(value => value is ParameterValue or NamedValue && Array.FindIndex(names, name => name.Name == (value as ParameterValue)?.Name || name.Name == (value as NamedValue)?.Name) is var index and >= 0 && index < passed.Count
                    ? passed[index] : null);
            }
            catch { return null; }
        }

        /// <summary>
        /// The game helper's code asks the fight's log, or calls another helper of its type (that may): only those are followed, once each.
        /// </summary>
        public bool IsGameHelperAskingTheLog(CodeMember member) =>
            member is { Kind: CodeMemberKind.Method, ReturnsValue: true, HasThis: false } && member.DeclaringType != _card.Name
            && member.Namespace.StartsWith("MegaCrit.Sts2.Core", StringComparison.Ordinal) && !member.ValueType.StartsWith("Task", StringComparison.Ordinal)
            && _card.Assembly.GetType(member.Namespace + "." + member.DeclaringType) is { } owner && AsksTheLog(owner, member);

        private bool AsksTheLog(Type owner, CodeMember member) => HelperAsks.GetOrAdd((owner, member.Name, member.ParameterCount), key =>
        {
            if (key.Owner.GetMethods(All).FirstOrDefault(method => method.Name == key.Name && method.GetParameters().Length == key.Parameters) is not { } found) return false;
            try
            {
                return _read(found).Any(instruction => instruction.IsCall && instruction.Member is { } called
                    && (called is { Name: "get_History", DeclaringType: "CombatManager" } || called.DeclaringType == key.Owner.Name && !called.HasThis));
            }
            catch { return false; }
        });

        /// <summary>How deep in lambdas the one being read is (a lambda's own lambdas are read up to a few deep).</summary>
        private int _lambdaDepth;

        /// <summary>A lambda inside a lambda being read: read as one is, a level deeper.</summary>
        private LambdaValue? NestedLambdaOf(CodeMember member)
        {
            _lambdaDepth++;
            try { return LambdaOf(member); }
            finally { _lambdaDepth--; }
        }

        /// <summary>A lambda the code makes (a predicate for Any, Where...): its parameter and what it returns.</summary>
        private LambdaValue? LambdaOf(CodeMember member)
        {
            if (Lambda(member) is not { } body) return null;
            try
            {
                // A lambda inside it is read too (Book Repair Knife's c => c.Powers.All(p => p.ShouldOwnerDeathTriggerFatal())).
                var flow = Flow.Of(_read(body), body, lambdaOf: _lambdaDepth < 3 ? NestedLambdaOf : null, helperOf: null, varKeyOf: VarKeyOf);
                var parameter = body.GetParameters() is { Length: > 0 } parameters ? parameters[0].Name ?? "x" : "x";
                return new LambdaValue(parameter, flow.Returned());
            }
            catch { return null; }
        }

        /// <summary>
        /// The steps of ops[start, end): commands in order, loops as bodies, each step with the condition it runs on (the tests on the way
        /// to it), inside <paramref name="outer"/> (the loop it's in: its own condition isn't repeated on each step).
        /// </summary>
        private void Parse(Flow flow, int start, int end, List<ProgramStep> into, ProgramCondition? outer, int depth)
        {
            var ops = flow.Ops;
            List<ProgramStep>? callback = null;
            for (var i = start; i < end; i++)
            {
                var instruction = ops[i];
                // A loop that starts here (a backward jump to this instruction later on): its body up to that jump.
                if (flow.LoopEndAt(i) is { } loopEnd && loopEnd < end)
                {
                    var entry = flow.LoopEntry(i);
                    if (entry is { IsNever: false })
                    {
                        var body = new List<ProgramStep>();
                        Parse(flow, i, loopEnd, body, entry, depth);
                        if (body.Count > 0)
                            into.Add(new LoopStep(instruction.Offset, flow.LoopOver(i, loopEnd), body)
                                { Condition = Relative(entry, outer), While = flow.While(i, loopEnd), Items = flow.LoopItems(i) });
                    }
                    i = loopEnd;
                    continue;
                }
                if (flow.IsNoise(i) || instruction.IsBranch) continue;
                // Code no way reaches (a handler, a resume point): nothing of it runs in a play.
                if (flow.ReachedAt(i) is not { IsNever: false } reached) continue;
                var condition = Relative(reached, outer);
                // A store to the model's own state (this.HasAmalgamDied = true): a step of its own, for what reads that state later.
                if (instruction.OpCode == OpCodes.Stfld && instruction.Member is { Kind: CodeMemberKind.Field } field && OwnState(field) is { } stored
                    && flow.StoreReceiver(i) is NamedValue { Name: "this" })
                {
                    into.Add(new StoreStep(instruction.Offset, stored, flow.ValueBefore(i)) { Condition = condition });
                    continue;
                }
                // A store to a plain field of the model's own data (data.etherealCount++): its state, as "Data.etherealCount".
                if (instruction.OpCode == OpCodes.Stfld && instruction.Member is { Kind: CodeMemberKind.Field } dataField && IsOwnDataType(dataField.DeclaringType)
                    && IsOwnData(flow.StoreReceiver(i)))
                {
                    into.Add(new StoreStep(instruction.Offset, OwnDataPrefix + dataField.Name, flow.ValueBefore(i)) { Condition = condition });
                    continue;
                }
                // A plain field of the model's own data set to its default through its address (GetInternalData<Data>().damageReceivedThisTurn = default:
                // ldflda, then initobj: Hardened Shell's count as each side's turn starts): a store of 0, as "Data.damageReceivedThisTurn".
                if (instruction.OpCode == OpCodes.Ldflda && i + 1 < end && ops[i + 1].OpCode == OpCodes.Initobj && instruction.Member is { Kind: CodeMemberKind.Field } clearedField
                    && IsOwnDataType(clearedField.DeclaringType) && IsOwnData(flow.ReceiverValue(i, arguments: 0)))
                {
                    into.Add(new StoreStep(instruction.Offset, OwnDataPrefix + clearedField.Name, new ConstantValue(0)) { Condition = condition });
                    continue;
                }
                if (!instruction.IsCall || instruction.Member is not { } member) continue;
                // A change to one of the model's own data's collections whose result isn't used (an Add, a Remove for its out value).
                if (member is { Kind: CodeMemberKind.Method, HasThis: true } && member.Namespace.StartsWith("System.Collections", StringComparison.Ordinal)
                    && (!member.ReturnsValue || i + 1 < ops.Count && ops[i + 1].OpCode == OpCodes.Pop)
                    && (flow.ReceiverValue(i, member.ParameterCount) is StoredValue { Value: var kept } ? kept : flow.ReceiverValue(i, member.ParameterCount)) is QueryValue { Of: { } dataOf, Name: var collection } && IsOwnData(dataOf))
                {
                    into.Add(new DataStep(instruction.Offset, collection, member.Name, flow.ArgumentValues(i, member)) { Condition = condition });
                    continue;
                }
                // A change to a list the hook is given (ModifyShuffleOrder's cards: Perfect Fit's Remove and Insert), whose result isn't used.
                if (member is { Kind: CodeMemberKind.Method, HasThis: true } && member.Namespace.StartsWith("System.Collections", StringComparison.Ordinal)
                    && (!member.ReturnsValue || i + 1 < ops.Count && ops[i + 1].OpCode == OpCodes.Pop)
                    && flow.ReceiverValue(i, member.ParameterCount) is ParameterValue { Name: var parameter })
                {
                    into.Add(new ParameterListStep(instruction.Offset, parameter, member.Name, flow.ArgumentValues(i, member)) { Condition = condition });
                    continue;
                }
                if (member is { Kind: CodeMemberKind.Method, HasThis: true, ParameterCount: 1 } && member.Name.StartsWith("set_", StringComparison.Ordinal) && OwnState(member) is { } property
                    && flow.ReceiverValue(i, arguments: 1) is NamedValue { Name: "this" })
                {
                    into.Add(new StoreStep(instruction.Offset, property, flow.ArgumentValues(i, member)[0]) { Condition = condition });
                    continue;
                }
                // A property a card's play sets on what one of its commands gave back (Imitation Learning's imitationLearningPower.PlayerTarget =
                // cardPlay.Target.Player, the power its PowerCmd.Apply gave): a store, "Result.PlayerTarget", with that result, which the translation of
                // that command reads (decision #322).
                if (!IsPowerOrRelic && member is { Kind: CodeMemberKind.Method, HasThis: true, ParameterCount: 1 } && member.Name.StartsWith("set_", StringComparison.Ordinal)
                    && Received(flow.ReceiverValue(i, arguments: 1), reached) is { } result)
                {
                    into.Add(new StoreStep(instruction.Offset, ResultPrefix + member.Name[4..], flow.ArgumentValues(i, member)[0]) { Condition = condition, Of = result });
                    continue;
                }
                // A property set on an object the hook is given (an arrow, a card: arrow.Status = ...): a store, "Given.arrow.Status",
                // which the hook reader translates or names as a gap.
                if (member is { Kind: CodeMemberKind.Method, HasThis: true, ParameterCount: 1 } && member.Name.StartsWith("set_", StringComparison.Ordinal) && IsPowerOrRelic
                    && flow.ReceiverValue(i, arguments: 1) switch { ParameterValue givenParameter => givenParameter.Name, NamedValue givenNamed => givenNamed.Name, _ => null } is { } given && given != "this")
                {
                    into.Add(new StoreStep(instruction.Offset, GivenPrefix + given + "." + member.Name[4..], flow.ArgumentValues(i, member)[0]) { Condition = condition });
                    continue;
                }
                // A property set on its owner's monster ((CorpseSlug)base.Owner.Monster).IsRavenous = true): a store, "Monster.CorpseSlug.IsRavenous", which the
                // hook reader translates or names as a gap.
                if (member is { Kind: CodeMemberKind.Method, HasThis: true, ParameterCount: 1 } && member.Name.StartsWith("set_", StringComparison.Ordinal) && IsPowerOrRelic
                    && IsOwnersMonster(flow.ReceiverValue(i, arguments: 1)))
                {
                    into.Add(new StoreStep(instruction.Offset, OwnersMonsterPrefix + member.DeclaringType + "." + member.Name[4..], flow.ArgumentValues(i, member)[0]) { Condition = condition });
                    continue;
                }
                // A property set on a monster the hook makes before it joins the fight ((Wriggler)ModelDb.Monster<Wriggler>().ToMutable(), then
                // wriggler.StartStunned = true: Infested's): a store, "Made.Wriggler.StartStunned", which the hook reader reads with the CreatureCmd.Add that
                // adds that monster or names as a gap.
                if (member is { Kind: CodeMemberKind.Method, HasThis: true, ParameterCount: 1 } && member.Name.StartsWith("set_", StringComparison.Ordinal) && IsPowerOrRelic
                    && MadeMonster(flow.ReceiverValue(i, arguments: 1)) is { } madeMonster)
                {
                    into.Add(new StoreStep(instruction.Offset, MadeMonsterPrefix + madeMonster + "." + member.Name[4..], flow.ArgumentValues(i, member)[0]) { Condition = condition });
                    continue;
                }
                // A property a monster's method sets on its own creature, read where its owner's power calls it (Steam Eruption's TriggerAboutToBlowState:
                // base.Creature.HpDisplay = ...): a store, "Creature.HpDisplay", which the hook reader translates or names as a gap.
                if (ForOwnersPower && member is { Kind: CodeMemberKind.Method, HasThis: true, ParameterCount: 1 } && member.Name.StartsWith("set_", StringComparison.Ordinal)
                    && flow.ReceiverValue(i, arguments: 1) is QueryValue { Name: "Creature", Of: NamedValue { Name: "this" } })
                {
                    into.Add(new StoreStep(instruction.Offset, OwnCreaturePrefix + member.Name[4..], flow.ArgumentValues(i, member)[0]) { Condition = condition });
                    continue;
                }
                // A property a power sets on its owner (Hardened Shell's base.Owner.HpDisplay = HpDisplay.InfiniteWithNumbers): the same store, "Creature.HpDisplay",
                // which the hook reader translates or names as a gap.
                if (IsPowerOrRelic && member is { Kind: CodeMemberKind.Method, HasThis: true, ParameterCount: 1, DeclaringType: "Creature" } && member.Name.StartsWith("set_", StringComparison.Ordinal)
                    && flow.ReceiverValue(i, arguments: 1) is QueryValue { Name: "Owner", Of: NamedValue { Name: "this" } })
                {
                    into.Add(new StoreStep(instruction.Offset, OwnCreaturePrefix + member.Name[4..], flow.ArgumentValues(i, member)[0]) { Condition = condition });
                    continue;
                }
                // A change to a collection the code makes itself (List<Creature> wrigglers = new(): Infested's, its Wrigglers kept to show them once its death's
                // animation ends), whose result isn't used: a step on it, "Local.wrigglers", which the hook reader translates or names as a gap.
                if (IsPowerOrRelic && member is { Kind: CodeMemberKind.Method, HasThis: true } && member.Namespace.StartsWith("System.Collections", StringComparison.Ordinal)
                    && (!member.ReturnsValue || i + 1 < ops.Count && ops[i + 1].OpCode == OpCodes.Pop)
                    && flow.ReceiverValue(i, member.ParameterCount) is StoredValue { Name: { Length: > 0 } madeList, Value: QueryValue { Name: ".ctor" } made } && made.Type.StartsWith("List", StringComparison.Ordinal))
                {
                    into.Add(new DataStep(instruction.Offset, LocalPrefix + madeList, member.Name, flow.ArgumentValues(i, member)) { Condition = condition });
                    continue;
                }
                // One of the model's own values set (base.DynamicVars["CardsLeft"].BaseValue = 5m, base.DynamicVars.Damage.BaseValue--): a store to
                // its state, "DynamicVars." and the value's property or its key as the code writes it.
                if (member is { Kind: CodeMemberKind.Method, HasThis: true, ParameterCount: 1, Name: "set_BaseValue" } && KeepsOwnVars && OwnVarName(flow.StoreReceiver(i)) is { } ownVar)
                {
                    into.Add(new StoreStep(instruction.Offset, OwnVarPrefix + ownVar, flow.ArgumentValues(i, member)[0]) { Condition = condition });
                    continue;
                }
                // base.AfterAddedToRoom(): the base class's own code of the hook (a non-virtual call to an instance method of a base model
                // class), its steps run here.
                if (instruction.OpCode == OpCodes.Call && member.HasThis && BaseImplementation(member) is { } implementation)
                {
                    if (depth < MaxDepth)
                    {
                        var inner = new Reader(_card, _read).Steps(implementation, depth + 1);
                        into.AddRange(condition == null ? inner : inner.Select(step => step with { Condition = And(condition, step.Condition) }));
                    }
                    continue;
                }
                // Its own Flash() (a power's or a relic's, on itself): where the game shows it acting.
                if (member is { Kind: CodeMemberKind.Method, HasThis: true, Name: "Flash" } && IsPowerOrRelic && flow.ReceiverValue(i, member.ParameterCount) is NamedValue { Name: "this" })
                {
                    into.Add(new FlashCall(instruction.Offset) { Condition = condition });
                    continue;
                }
                // A card's own action a power's or relic's code calls (SetToFreeThisTurn on a card in the hand): a step, with the card it's on.
                if (IsPowerOrRelic && IsCardAction(member))
                {
                    into.Add(new CardActionStep(instruction.Offset, member, flow.ReceiverValue(i, member.ParameterCount)) { Condition = condition, Values = flow.ArgumentValues(i, member) });
                    continue;
                }
                if (IsModelAction(member))
                {
                    into.Add(new CommandStep(instruction.Offset, member, Arguments(flow, i, member)) { Condition = condition, Values = flow.ArgumentValues(i, member) });
                    continue;
                }
                if (member.Namespace == "MegaCrit.Sts2.Core.Commands" && member.DeclaringType == "CardSelectCmd")
                {
                    into.Add(new ChoiceStep(instruction.Offset, member) { Condition = condition, Arguments = Arguments(flow, i, member), Values = flow.ArgumentValues(i, member) });
                    continue;
                }
                if (member.Name == "CreateCard" && member.GenericArguments.Count == 1)
                {
                    into.Add(new CreateStep(instruction.Offset, member.GenericArguments[0]) { Condition = condition, Values = flow.ArgumentValues(i, member) });
                    continue;
                }
                if (member.DeclaringType == "DamageCmd" && member.Name == "Attack")
                {
                    into.Add(Attack(flow, i, member) with { Condition = condition });
                    continue;
                }
                // A hook the code tells the listeners itself (DoomPower.DoomKill's Hook.AfterDiedToDoom): what every listener does there runs at this
                // step, so it's a step like a command, never left out.
                if (IsHookTold(member))
                {
                    into.Add(new CommandStep(instruction.Offset, member, Arguments(flow, i, member)) { Condition = condition, Values = flow.ArgumentValues(i, member) });
                    continue;
                }
                // A power's action on itself (PoisonPower.AfterSideTurnStart's Trigger(), Black Hole's DealDamageToAllEnemies) is its own method, read
                // where it's called below; another's (Outbreak's PoisonPower.Trigger on each enemy's poison) is a step named by its power and method.
                if (IsCommand(member) || IsPowerAction(member) && (member.DeclaringType != _card.Name || OwnMethod(member) == null))
                {
                    into.Add(new CommandStep(instruction.Offset, member, Arguments(flow, i, member))
                        { Condition = condition, Values = flow.ArgumentValues(i, member), Inner = callback ?? (IReadOnlyList<ProgramStep>)Array.Empty<ProgramStep>() });
                    callback = null;
                    continue;
                }
                // A method of its owner's monster's own class, called on that monster (((CeremonialBeast)base.Owner.Monster).SetStunned(): Plow's): its
                // steps run here as that monster's, its this the owner's monster, so a store to its own state is that monster's state
                // ("Monster.CeremonialBeast.IsInSecondPhase") and its base.Creature the owner (decision #275). Each step is at the call, where the power's
                // code runs it.
                if (IsPowerOrRelic && OwnersMonsterMethod(member) is { } monsterMethod
                    && (IsOwnersMonster(flow.ReceiverValue(i, member.ParameterCount)) || IsGivenOwnersMonster(flow.ReceiverValue(i, member.ParameterCount), reached)))
                {
                    if (depth >= MaxDepth) into.Add(new UnknownStep(instruction.Offset, $"{member.Name}: calls nested too deep") { Condition = condition });
                    else
                    {
                        var passed = PassedTo(monsterMethod.Called, flow.ArgumentValues(i, member));
                        var inner = new Reader(monsterMethod.Owner, _read) { ForOwnersPower = true }.Steps(monsterMethod.Body, depth + 1)
                            .Select(step => AsOwnersMonsters(Bound(step, value => value is NamedValue { Name: "this" } ? OwnersMonster : passed(value)), monsterMethod.Owner.Name) with { Offset = instruction.Offset });
                        into.AddRange(condition == null ? inner : inner.Select(step => step with { Condition = And(condition, step.Condition) }));
                    }
                    continue;
                }
                // The card's own methods and the local functions its OnPlay declares: their steps run here.
                if (OwnMethod(member) is { } own)
                {
                    // Past the depth the reader follows, a method whose code calls nothing but getters (a question: DoomPower.IsOwnerDoomed in the
                    // filter GetDoomedCreatures gives Where) has no steps to miss.
                    if (depth >= MaxDepth) { if (MayAct(own.Body)) into.Add(new UnknownStep(instruction.Offset, $"{member.Name}: calls nested too deep") { Condition = condition }); }
                    else
                    {
                        // Its parameters are what this call passes (MadScience.ExecuteRider(TinkerTimeRider, ...): its tests of rider are tests
                        // of the card's rider).
                        var passed = PassedTo(own.Called, flow.ArgumentValues(i, member));
                        var inner = new Reader(_card, _read).Steps(own.Body, depth + 1).Select(step => Bound(step, passed));
                        into.AddRange(condition == null ? inner : inner.Select(step => step with { Condition = And(condition, step.Condition) }));
                    }
                    continue;
                }
                // A static helper of the game's models that runs commands (Shiv.CreateInHand: Blade Symphony's Shivs): its steps run here, its
                // parameters what this call passes (Blade Dance's one Shiv, Storm of Steel's as many as it discarded).
                if (depth < MaxDepth && GameHelper(member) is { } helper)
                {
                    var passed = PassedTo(helper.Called, flow.ArgumentValues(i, member));
                    var inner = new Reader(helper.Owner, _read).Steps(helper.Body, depth + 1).Select(step => Bound(step, passed)).ToList();
                    if (Acts(inner)) into.AddRange(condition == null ? inner : inner.Select(step => step with { Condition = And(condition, step.Condition) }));
                    continue;
                }
                // A delegate whose code calls commands: the next command it's given to runs it (its Inner steps). One left over, or
                // given to something else (an attack builder's BeforeDamage), runs when something else decides: not followed.
                if (instruction.OpCode == OpCodes.Newobj && i > 0 && ops[i - 1].OpCode == OpCodes.Ldftn && ops[i - 1].Member is { } lambda && Lambda(lambda) is { } lambdaBody
                    && depth < MaxDepth)
                {
                    var inner = new Reader(_card, _read).Steps(lambdaBody, depth + 1);
                    if (Acts(inner))
                    {
                        if (callback != null) into.Add(new UnknownStep(instruction.Offset, "commands in a callback") { Condition = condition });
                        callback = inner;
                    }
                }
            }
            if (callback != null) into.Add(new UnknownStep(ops.Count > 0 ? ops[^1].Offset : 0, "commands in a callback"));
        }

        /// <summary>
        /// A method's parameters as a call passes them (each the value of its argument), for its steps' values and conditions: a parameter, or
        /// its state machine's field named after it.
        /// </summary>
        private static Func<ProgramValue, ProgramValue?> PassedTo(MethodBase called, IReadOnlyList<ProgramValue> values)
        {
            var passed = new Dictionary<string, ProgramValue>();
            var names = called.GetParameters();
            for (var parameter = 0; parameter < names.Length && parameter < values.Count; parameter++)
                if (names[parameter].Name is { } parameterName && values[parameter] is not UnknownValue) passed[parameterName] = values[parameter];
            return value => value switch
            {
                ParameterValue parameterValue when passed.TryGetValue(parameterValue.Name, out var given) => given,
                NamedValue namedValue when passed.TryGetValue(namedValue.Name, out var given) => given,
                _ => null,
            };
        }

        /// <summary>A step with the values in its conditions mapped (a helper's parameters by what its caller passed), its inner steps too.</summary>
        internal static ProgramStep Bound(ProgramStep step, Func<ProgramValue, ProgramValue?> replace)
        {
            var bound = step with { Condition = step.Condition?.Map(replace) };
            return bound switch
            {
                LoopStep loop => loop with { While = loop.While?.Map(replace), Items = loop.Items?.Map(replace), Body = loop.Body.Select(inner => Bound(inner, replace)).ToList() },
                ChoiceStep choice => choice with { Values = choice.Values.Select(value => value.Map(replace)).ToList() },
                CreateStep create => create with { Values = create.Values.Select(value => value.Map(replace)).ToList() },
                StoreStep store => store with { Value = store.Value.Map(replace) },
                CommandStep command => command with
                {
                    Values = command.Values.Select(value => value.Map(replace)).ToList(), Inner = command.Inner.Select(inner => Bound(inner, replace)).ToList(),
                    HitCountValue = command.HitCountValue?.Map(replace), TargetValue = command.TargetValue?.Map(replace),
                },
                _ => bound,
            };
        }

        private static ProgramCondition? And(ProgramCondition? outer, ProgramCondition? inner) => outer == null ? inner : inner == null ? outer : outer.And(inner);

        /// <summary>A step's condition inside the loop it's in (null when it holds on every way).</summary>
        private static ProgramCondition? Relative(ProgramCondition reached, ProgramCondition? outer) => reached.Without(outer) is { IsAlways: false } relative ? relative : null;

        /// <summary>Steps that do something: a command or a choice (a helper or delegate with neither only asks or shows).</summary>
        private static bool Acts(List<ProgramStep> steps) => new ModelProgram("", steps).All().Any(step => step is CommandStep or ChoiceStep or UnknownStep);

        /// <summary>The attack DamageCmd.Attack starts: its builder calls up to Execute.</summary>
        private static CommandStep Attack(Flow flow, int at, CodeMember member)
        {
            var builder = new List<string>();
            string? hitCount = null;
            ProgramValue? hitCountValue = null, targetValue = null;
            for (var j = at + 1; j < flow.Ops.Count; j++)
            {
                if (flow.Ops[j].Member is not { DeclaringType: "AttackCommand" } call || !flow.Ops[j].IsCall) continue;
                if (call.Name == "Execute") break;
                builder.Add(call.Name);
                if (call.Name == "WithHitCount" && call.ParameterCount >= 1)
                {
                    hitCount = flow.Source(j, call, parameter: 0);
                    hitCountValue = flow.ArgumentValues(j, call)[0];
                }
                if (call.Name == "Targeting" && call.ParameterCount >= 1) targetValue = flow.ArgumentValues(j, call)[0];
            }
            return new CommandStep(flow.Ops[at].Offset, member, Arguments(flow, at, member))
                { Builder = builder, HitCount = hitCount, HitCountValue = hitCountValue, TargetValue = targetValue, Values = flow.ArgumentValues(at, member) };
        }

        /// <summary>
        /// The code may do something: it calls a method that isn't a property's getter (a command, a hook, another method that might call one), or
        /// it can't be read. False only for code that reads properties and works with what they give.
        /// </summary>
        private bool MayAct(MethodBase body)
        {
            try
            {
                return _read(body).Any(instruction => instruction.IsCall && instruction.Member is { Kind: CodeMemberKind.Method } called && !called.Name.StartsWith("get_", StringComparison.Ordinal)
                    && !(called.DeclaringType == "Decimal" && called.Name.StartsWith("op_", StringComparison.Ordinal)));
            }
            catch { return true; }
        }

        /// <summary>The card's own method (its type or a base type's private helper) or a local function OnPlay declares.</summary>
        private (MethodBase Called, MethodBase Body)? OwnMethod(CodeMember member)
        {
            if (member.Kind != CodeMemberKind.Method || member.HasThis == false && !member.Name.StartsWith('<')) return null;
            if (member.DeclaringType != _card.Name && !member.DeclaringType.StartsWith("<>c", StringComparison.Ordinal)) return null;
            if (!member.Name.StartsWith('<') && member.Name is "OnPlay" or "get_DynamicVars" or "get_Owner" or "get_CombatState") return null;
            if (member.Name.StartsWith("get_", StringComparison.Ordinal) || member.Name.StartsWith("set_", StringComparison.Ordinal)) return null;
            foreach (var type in OwnTypes())
            {
                if (type.Name != member.DeclaringType) continue;
                var found = Called(type.GetMethods(All), member);
                if (found == null) continue;
                // An async helper's code is its state machine's (nested in its type, or in the card for a local function).
                return (found, BodyOf(found));
            }
            return null;
        }

        /// <summary>A static method of another of the game's models (not a command, not a property): the type it's on and its code.</summary>
        private (Type Owner, MethodBase Called, MethodBase Body)? GameHelper(CodeMember member)
        {
            if (member.Kind != CodeMemberKind.Method || member.HasThis || IsCommand(member) || member.Name.StartsWith('<') || member.Name.StartsWith("op_", StringComparison.Ordinal)) return null;
            if (!member.Namespace.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal) || member.Name.StartsWith("get_", StringComparison.Ordinal)) return null;
            if (_card.Assembly.GetType(member.Namespace + "." + member.DeclaringType) is not { } owner) return null;
            if (Called(owner.GetMethods(All), member) is not { } found) return null;
            return (owner, found, BodyOf(found));
        }

        private MethodBase? Lambda(CodeMember member)
        {
            foreach (var type in OwnTypes())
            {
                if (type.Name != member.DeclaringType) continue;
                if (type.GetMethods(All).FirstOrDefault(method => method.Name == member.Name) is not { } found) continue;
                return BodyOf(found);
            }
            // A method of its owner's monster's class made into a delegate (Plow's ceremonialBeast.StunnedMove, the stunned move it gives CreatureCmd.Stun):
            // its code, read as a lambda's is.
            return IsPowerOrRelic && OwnersMonsterMethod(member) is { } monsterMethod ? monsterMethod.Body : null;
        }

        /// <summary>
        /// An instance method a monster class of the game's declares, that returns nothing or a task (so no question, whatever its name: Tunneler's
        /// GetStunned) and isn't a property or a show (CeremonialBeast.SetStunned, its StunnedMove): its class and code, for a power whose code calls it
        /// on its owner's monster; else null.
        /// </summary>
        private (Type Owner, MethodBase Called, MethodBase Body)? OwnersMonsterMethod(CodeMember member)
        {
            if (member is not { Kind: CodeMemberKind.Method, HasThis: true, Namespace: "MegaCrit.Sts2.Core.Models.Monsters" } || member.Name.StartsWith('<')
                || member.Name.StartsWith("get_", StringComparison.Ordinal) || member.Name.StartsWith("set_", StringComparison.Ordinal) || ShowsOnly(member.Name)
                || member.ValueType != typeof(void).Name && member.ValueType != nameof(Task)) return null;
            if (_card.Assembly.GetType(member.Namespace + "." + member.DeclaringType) is not { } monster || !IsMonsterClass(monster)) return null;
            if (monster.GetMethods(All).FirstOrDefault(method => method.Name == member.Name && method.GetParameters().Length == member.ParameterCount) is not { } found) return null;
            return (monster, found, BodyOf(found));

            // By name: the game's types may be read apart from the ones the mod runs with.
            static bool IsMonsterClass(Type type)
            {
                for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
                    if (baseType.Name == nameof(MegaCrit.Sts2.Core.Models.MonsterModel)) return true;
                return false;
            }
        }

        /// <summary>Its owner's monster (base.Owner.Monster): what a method of that monster's class names as this, read for its owner's power.</summary>
        private static readonly ProgramValue OwnersMonster = new QueryValue(nameof(MegaCrit.Sts2.Core.Entities.Creatures.Creature), "Monster",
            new QueryValue(nameof(MegaCrit.Sts2.Core.Models.PowerModel), "Owner", new NamedValue("this"), Array.Empty<ProgramValue>(), nameof(MegaCrit.Sts2.Core.Entities.Creatures.Creature)) { IsProperty = true },
            Array.Empty<ProgramValue>(), nameof(MegaCrit.Sts2.Core.Models.MonsterModel)) { IsProperty = true };

        /// <summary>
        /// A step of a method of the owner's monster's class, as its owner's power runs it: a store to the monster's own state is that monster's state
        /// ("Monster." + its class + "." + the member: OwnersMonsterPrefix), as a property the power sets on it is.
        /// </summary>
        private static ProgramStep AsOwnersMonsters(ProgramStep step, string monsterClass) => step switch
        {
            StoreStep store when !store.Field.StartsWith(OwnDataPrefix, StringComparison.Ordinal) && !store.Field.StartsWith(OwnVarPrefix, StringComparison.Ordinal)
                && !store.Field.StartsWith(GivenPrefix, StringComparison.Ordinal) && !store.Field.StartsWith(OwnersMonsterPrefix, StringComparison.Ordinal) && !store.Field.StartsWith(OwnCreaturePrefix, StringComparison.Ordinal)
                => store with { Field = OwnersMonsterPrefix + monsterClass + "." + store.Field },
            LoopStep loop => loop with { Body = loop.Body.Select(inner => AsOwnersMonsters(inner, monsterClass)).ToList() },
            _ => step,
        };

        /// <summary>The model read is a power or a relic (its own values are state its hooks keep; a card's are its play's numbers, read apart).</summary>
        private bool IsPowerOrRelic
        {
            get
            {
                for (var type = _card.BaseType; type != null; type = type.BaseType)
                    if (type.Name is "PowerModel" or "RelicModel") return true;
                return false;
            }
        }

        /// <summary>
        /// The model read keeps its own values as state its code sets (a power, a relic; an enchantment, whose RecalculateValues sets them from its
        /// Amount: Adroit's Block): a store to one is a step.
        /// </summary>
        private bool KeepsOwnVars
        {
            get
            {
                for (var type = _card.BaseType; type != null; type = type.BaseType)
                    if (type.Name is "PowerModel" or "RelicModel" or "EnchantmentModel") return true;
                return false;
            }
        }

        /// <summary>
        /// The name of the model's own state a store sets (a field, or a property by its setter, of its class or of a base class of the
        /// game's: an auto-property's backing field by the property's name); null for anything else (a state machine's locals, another
        /// object's).
        /// </summary>

        private string? OwnState(CodeMember member)
        {
            if (member.Name.StartsWith("<>", StringComparison.Ordinal)) return null;
            for (var type = _card; type != null && type.Namespace?.StartsWith("MegaCrit", StringComparison.Ordinal) == true; type = type.BaseType)
            {
                if (type.Name != member.DeclaringType) continue;
                var name = member.Name.StartsWith("set_", StringComparison.Ordinal) ? member.Name[4..] : member.Name;
                return name.StartsWith('<') && name.IndexOf('>') is var close and > 1 ? name[1..close] : name;
            }
            return null;
        }

        /// <summary>
        /// The code of a base model class's own virtual method the model calls past its override (base.AfterAddedToRoom(): MonsterModel's,
        /// which does nothing): a virtual method of a base class of the game's, other than the model's own class, called without dispatch.
        /// </summary>
        private MethodBase? BaseImplementation(CodeMember member)
        {
            if (member.Name.StartsWith('<') || member.Name.StartsWith("get_", StringComparison.Ordinal) || member.DeclaringType == _card.Name) return null;
            for (var type = _card.BaseType; type != null && type.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal) == true; type = type.BaseType)
            {
                if (type.Name != member.DeclaringType) continue;
                return type.GetMethods(All).FirstOrDefault(method => method.Name == member.Name && method.GetParameters().Length == member.ParameterCount) is { IsAbstract: false, IsVirtual: true } found
                    ? BodyOf(found) : null;
            }
            return null;
        }

        /// <summary>A type nested in the model's class that isn't the compiler's (its Data class).</summary>
        private bool IsOwnDataType(string name) => !name.StartsWith('<') && CardCode.NestedTypes(_card).Any(nested => nested.Name == name);

        private IEnumerable<Type> OwnTypes()
        {
            yield return _card;
            foreach (var nested in CardCode.NestedTypes(_card)) yield return nested;
        }

        /// <summary>Where each argument of the call at <paramref name="at"/> comes from, described.</summary>
        private static IReadOnlyList<string> Arguments(Flow flow, int at, CodeMember member)
        {
            var arguments = new string[member.ParameterCount];
            for (var parameter = 0; parameter < member.ParameterCount; parameter++)
                arguments[parameter] = flow.Source(at, member, parameter);
            return arguments;
        }
    }

    /// <summary>
    /// A method's instructions with the async machinery taken out: the state dispatch, each await's suspend and resume, the leaves
    /// and the finally blocks' state checks. What's left is the card's own flow: its calls, its ifs and its loops.
    /// </summary>
    private sealed class Flow
    {
        public readonly List<CodeOp> Ops;
        private readonly Dictionary<int, int> _indexOf = new();
        private readonly bool[] _noise;
        private readonly Dictionary<int, int> _loopEnds = new();
        /// <summary>Each await's jump past its suspend (index of the jump, index it resumes at).</summary>
        private readonly Dictionary<int, int> _awaitJumps = new();
        /// <summary>The state dispatch's jumps (where to resume): not the card's flow.</summary>
        private readonly HashSet<int> _dispatch = new();
        /// <summary>The condition each instruction is reached on (null: never reached).</summary>
        private ProgramCondition?[] _reached = Array.Empty<ProgramCondition?>();
        private readonly Dictionary<int, ProgramCondition?> _loopEntries = new();
        /// <summary>The method read (names its literals' places and its parameters).</summary>
        private readonly System.Reflection.MethodBase? _method;
        private readonly string _site;
        private readonly Func<CodeMember, LambdaValue?>? _lambdaOf;
        private readonly Func<CodeMember, IReadOnlyList<ProgramValue>, ProgramValue?>? _helperOf;
        /// <summary>The key a property of DynamicVarSet looks its value up by (ProgramReader.DynamicVarKey); its own name when there is none.</summary>
        private readonly Func<string, string>? _varKeyOf;

        private Flow(List<CodeOp> ops, System.Reflection.MethodBase? method, Func<CodeMember, LambdaValue?>? lambdaOf,
            Func<CodeMember, IReadOnlyList<ProgramValue>, ProgramValue?>? helperOf, Func<string, string>? varKeyOf)
        {
            Ops = ops;
            for (var i = 0; i < ops.Count; i++) _indexOf[ops[i].Offset] = i;
            _noise = new bool[ops.Count];
            _method = method;
            _site = method == null ? "" : method.DeclaringType?.Name + "." + method.Name;
            _lambdaOf = lambdaOf;
            _helperOf = helperOf;
            _varKeyOf = varKeyOf;
        }

        public int IndexOf(int offset) => _indexOf.TryGetValue(offset, out var index) ? index : Ops.Count;
        public bool IsNoise(int index) => _noise[index];
        public int? LoopEndAt(int index) => _loopEnds.TryGetValue(index, out var end) ? end : null;
        public ProgramCondition? ReachedAt(int index) => index < _reached.Length ? _reached[index] : null;
        public ProgramCondition? LoopEntry(int start) => _loopEntries.TryGetValue(start, out var entry) ? entry : null;

        public static Flow Of(List<CodeOp> ops, System.Reflection.MethodBase? method = null, Func<CodeMember, LambdaValue?>? lambdaOf = null,
            Func<CodeMember, IReadOnlyList<ProgramValue>, ProgramValue?>? helperOf = null, Func<string, string>? varKeyOf = null)
        {
            var flow = new Flow(ops, method, lambdaOf, helperOf, varKeyOf);
            flow.MarkMachinery();
            flow.FindLoops();
            flow.FindStackDepths();
            flow.FindConditions();
            return flow;
        }

        /// <summary>How many values are on the stack before each instruction (null: not known, or not reached by straight flow).</summary>
        private int?[] _stackDepth = Array.Empty<int?>();

        /// <summary>The jumps landing on each instruction (their indexes).</summary>
        private readonly Dictionary<int, List<int>> _jumpsTo = new();

        /// <summary>
        /// The stack's depth before each instruction, followed from the start along every jump and fall-through: a place two ways meet with a
        /// value on the stack (bool flag = a &amp;&amp; b: the jump past b leaves false) is a merge (ReadBack reads each way's value).
        /// </summary>
        private void FindStackDepths()
        {
            _stackDepth = new int?[Ops.Count];
            for (var i = 0; i < Ops.Count; i++)
                foreach (var target in Ops[i].Targets)
                {
                    var to = IndexOf(target);
                    if (!_jumpsTo.TryGetValue(to, out var from)) _jumpsTo[to] = from = new List<int>();
                    from.Add(i);
                }
            if (Ops.Count == 0) return;
            _stackDepth[0] = 0;
            var work = new Stack<int>();
            work.Push(0);
            while (work.Count > 0)
            {
                var index = work.Pop();
                var instruction = Ops[index];
                if (_stackDepth[index] is not { } depth || CodeDecoder.StackEffect(instruction) is not var (pop, push)) continue;
                var after = depth - pop + push;
                if (after < 0) continue;
                void Flow(int to, int value)
                {
                    if (to >= Ops.Count || _stackDepth[to] != null) return;
                    _stackDepth[to] = value;
                    work.Push(to);
                }
                // A leave empties the stack; ret, throw and the like end the way.
                if (instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S)
                {
                    Flow(IndexOf(instruction.Targets[0]), 0);
                    continue;
                }
                if (instruction.OpCode.FlowControl is FlowControl.Return or FlowControl.Throw) continue;
                foreach (var target in instruction.Targets) Flow(IndexOf(target), after);
                if (instruction.OpCode.FlowControl != FlowControl.Branch) Flow(index + 1, after);
            }
        }

        /// <summary>
        /// The value on the stack where two or more ways meet with one (<paramref name="at"/>: the instruction they meet at), as a true-or-false:
        /// each way's condition and the value it leaves (bool flag = a &amp;&amp; b: a and b). Null when it isn't such a place; Unknown when a way
        /// leaves something that isn't a true-or-false, or comes from later in the code.
        /// </summary>
        private (ProgramValue Value, int Start)? Merged(int at, int depth)
        {
            if (at >= Ops.Count || !_jumpsTo.TryGetValue(at, out var jumps) || _stackDepth.ElementAtOrDefault(at) is not > 0) return null;
            var ways = new List<(int Source, bool Jump)>();
            // Reached only by tests of a copy (x?.y: dup; brtrue to here, the way on ends before it): the value copied.
            if (jumps.All(jump => Ops[jump].OpCode.FlowControl == FlowControl.Cond_Branch && jump > 0 && Ops[jump - 1].OpCode == OpCodes.Dup)
                && at > 0 && Ops[at - 1].OpCode.FlowControl is FlowControl.Branch or FlowControl.Return or FlowControl.Throw)
                return ReadBack(jumps[0] - 1, depth + 1);
            // A conditional jump carrying the value it tested a copy of (x ?? y: dup; brtrue) is a way with that value, on the test that made it
            // jump. A cached lambda (ldsfld <>9__x; dup; brtrue) is the same lambda either way: read as before, and so is any other.
            var carried = jumps.Where(jump => Ops[jump].OpCode.FlowControl != FlowControl.Branch).ToList();
            if (carried.Any(jump => jump < 2 || Ops[jump - 1].OpCode != OpCodes.Dup || Ops[jump - 2].OpCode == OpCodes.Ldsfld && Ops[jump - 2].Member?.Name.StartsWith("<>9__", StringComparison.Ordinal) == true))
                return null;
            foreach (var jump in jumps)
            {
                if (jump > at) return (new UnknownValue("merge"), -1);
                ways.Add((jump, Jump: true));
            }
            if (at > 0 && Ops[at - 1].OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw))
                ways.Add((at - 1, Jump: false));
            var merged = ProgramCondition.Never;
            var each = new List<(ProgramCondition When, ProgramValue Value)>();
            var start = at;
            var truths = true;
            foreach (var (source, jump) in ways)
            {
                if (source >= _reached.Length || _reached[source] is not { IsNever: false } reached) continue;
                // A conditional jump's value is the copy it tested, on the test that made it jump (x ?? y: x, when it isn't null).
                var conditional = jump && Ops[source].OpCode.FlowControl == FlowControl.Cond_Branch;
                if (conditional) reached = reached.And(Taken(source));
                // A jump's value is the one before it; a fall-through's is the instruction's own.
                var value = conditional ? ReadBack(source - 1, depth + 1) : jump ? ReadBack(source, depth + 1) : ReadBack(at, depth + 1, merge: false);
                if (value.Start < 0) return (new UnknownValue("merge"), -1);
                start = Math.Min(start, value.Start);
                each.Add((reached, value.Value));
                if (AsCondition(value.Value, $"{_site}:{Ops[source].Offset:X4}:value") is { } holds) merged = merged.Or(reached.And(holds));
                else truths = false;
            }
            // The expression starts where its first test's operand does (a && b: a's code, before the jump past b).
            for (var changed = true; changed;)
            {
                changed = false;
                for (var i = start; i < at; i++)
                {
                    if (Ops[i].OpCode.FlowControl != FlowControl.Cond_Branch || CodeDecoder.StackEffect(Ops[i]) is not var (pops, _)) continue;
                    var cursor = i;
                    for (var operand = 0; operand < pops && cursor >= 0; operand++) cursor = ReadBack(cursor, depth + 1).Start;
                    if (cursor < 0 || cursor >= start) continue;
                    start = cursor;
                    changed = true;
                }
            }
            return (truths ? new ConditionValue(merged) : new WaysValue(each), start);
        }

        /// <summary>A true-or-false value as a condition (a constant, a comparison, a test the code made, a bool property); null otherwise.</summary>
        private static ProgramCondition? AsCondition(ProgramValue value, string at) => value switch
        {
            ConstantValue { Value: 0 } => ProgramCondition.Never,
            ConstantValue { Value: 1 } => ProgramCondition.Always,
            ConditionValue flag => flag.Condition,
            StoredValue { Value: ConditionValue stored } => stored.Condition,
            CompareValue or QueryValue { ValueType: "Boolean" } or StoredValue { Value: CompareValue or QueryValue { ValueType: "Boolean" } }
                => ProgramCondition.Of(new ConditionLiteral(at, value, Holds: true)),
            _ => null,
        };

        /// <summary>
        /// The condition each instruction runs on: every way to it from the start, each the tests it passes (a branch taken or not),
        /// with the machinery's jumps followed as they always go (an await resumes past its suspend; the dispatch starts at the top).
        /// A loop's body runs on the loop's own condition (its round's test is the loop's While); a way out of a loop goes on with the
        /// condition the loop had; a return (a leave to the end) ends its way.
        /// </summary>
        private void FindConditions()
        {
            var count = Ops.Count;
            var incoming = new ProgramCondition?[count + 1];
            _reached = new ProgramCondition?[count];
            incoming[0] = ProgramCondition.Always;
            var loops = _loopEnds.OrderBy(loop => loop.Key).Select(loop => (Start: loop.Key, End: loop.Value)).ToList();
            for (var i = 0; i < count; i++)
            {
                if (_loopEnds.TryGetValue(i, out var loopEnd))
                {
                    // Entered at its top, or by the jump to its test (a for or while): from before it, so all known by now.
                    var entry = incoming[i];
                    for (var inside = i + 1; inside <= loopEnd; inside++)
                        if (incoming[inside] is { } from) entry = entry == null ? from : entry.Or(from);
                    _loopEntries[i] = entry;
                    incoming[i] = entry;
                }
                var here = incoming[i];
                _reached[i] = here;
                if (here == null || here.IsNever) continue;
                var instruction = Ops[i];
                var at = i;
                void Edge(int target, ProgramCondition condition)
                {
                    if (target <= at) return; // a loop's round: its condition is the loop's
                    foreach (var (start, end) in loops)
                        if (start <= at && at <= end && target > end && _loopEntries.GetValueOrDefault(start) is { } loopEntry)
                        {
                            condition = loopEntry;
                            break;
                        }
                    incoming[target] = incoming[target] is { } already ? already.Or(condition) : condition;
                }
                if (_awaitJumps.TryGetValue(i, out var resume)) { Edge(resume, here); continue; }
                if (_dispatch.Contains(i)) { Edge(i + 1, here); continue; }
                if (instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S)
                {
                    var target = IndexOf(instruction.Targets[0]);
                    if (!Returns(target)) Edge(target, here);
                    continue;
                }
                if (instruction.OpCode == OpCodes.Ret || instruction.OpCode == OpCodes.Throw || instruction.OpCode == OpCodes.Rethrow
                    || instruction.OpCode == OpCodes.Endfinally || instruction.OpCode == OpCodes.Endfilter) continue;
                if (instruction.OpCode == OpCodes.Switch)
                {
                    // A switch on a value: each case runs when the value is its number, the rest when it's none of them.
                    var tested = ValueBefore(i);
                    var none = here;
                    for (var number = 0; number < instruction.Targets.Count; number++)
                    {
                        var literal = new ConditionLiteral($"{_site}:{instruction.Offset:X4}:{number}", new CompareValue(Comparison.Equal, tested, new ConstantValue(number)), Holds: true);
                        Edge(IndexOf(instruction.Targets[number]), here.And(literal));
                        none = none.And(literal.Negate());
                    }
                    Edge(i + 1, none);
                    continue;
                }
                if (instruction.OpCode.FlowControl == FlowControl.Branch) { Edge(IndexOf(instruction.Targets[0]), here); continue; }
                if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
                {
                    var taken = Taken(i);
                    Edge(IndexOf(instruction.Targets[0]), here.And(taken));
                    Edge(i + 1, here.And(taken.Negate()));
                    continue;
                }
                Edge(i + 1, here);
            }
        }

        /// <summary>A jump to <paramref name="index"/> ends the method: it reaches the return (or the builder's SetResult) with no call or jump on the way.</summary>
        private bool Returns(int index)
        {
            for (var i = index; i < Ops.Count && i < index + 12; i++)
            {
                var instruction = Ops[i];
                if (instruction.OpCode == OpCodes.Ret || instruction.Member is { Name: "SetResult" }) return true;
                if (instruction.IsCall || instruction.IsBranch) return false;
            }
            return index >= Ops.Count;
        }

        /// <summary>What the conditional jump at <paramref name="branch"/> tests, the way that makes it jump.</summary>
        private ConditionLiteral Taken(int branch)
        {
            var instruction = Ops[branch];
            var at = $"{_site}:{instruction.Offset:X4}";
            var unsigned = instruction.OpCode.Name!.Contains(".un", StringComparison.Ordinal);
            var name = instruction.OpCode.Name!.Replace(".s", "", StringComparison.Ordinal).Replace(".un", "", StringComparison.Ordinal);
            if (name is "brtrue" or "brfalse") return new ConditionLiteral(at, ValueBefore(branch), Holds: name == "brtrue");
            var right = ReadBack(branch, depth: 0);
            var left = right.Start >= 0 ? ReadBack(right.Start, depth: 0).Value : new UnknownValue("operand");
            var comparison = name switch
            {
                "beq" => Comparison.Equal,
                "bne" => Comparison.NotEqual,
                "blt" => Comparison.Less,
                "ble" => Comparison.LessOrEqual,
                "bgt" => Comparison.Greater,
                _ => Comparison.GreaterOrEqual,
            };
            return new ConditionLiteral(at, new CompareValue(comparison, left, right.Value) { Unsigned = unsigned && name != "bne" }, Holds: true);
        }

        /// <summary>When a loop goes round again (its jump back's test, and the tests on the way to it inside the loop); null when it can't be read.</summary>
        public ProgramCondition? While(int start, int end)
        {
            if (Ops[end].OpCode.FlowControl != FlowControl.Cond_Branch || ReachedAt(end) is not { } path) return null;
            var round = path.And(Taken(end)).Without(LoopEntry(start));
            return round.IsAlways ? null : round;
        }

        /// <summary>
        /// What a lambda (a method returning a value) returns, as a condition: each way to a return, with the value it returns (a constant
        /// true or false, or a test of its own).
        /// </summary>
        public ProgramCondition Returned()
        {
            var returned = ProgramCondition.Never;
            for (var i = 0; i < Ops.Count; i++)
            {
                if (Ops[i].OpCode != OpCodes.Ret || ReachedAt(i) is not { IsNever: false } reached) continue;
                var value = ValueBefore(i);
                returned = value switch
                {
                    ConstantValue { Value: 0 } => returned,
                    ConstantValue => returned.Or(reached),
                    _ => returned.Or(reached.And(new ConditionLiteral($"{_site}:{Ops[i].Offset:X4}", value, Holds: true))),
                };
            }
            return returned;
        }

        /// <summary>
        /// What a field store at <paramref name="at"/> is made on, an increment's included (x.f++, x.f += n: x; dup; ldfld f; ...; stfld f,
        /// whose stored value reads the copy the dup made, so the object is the one under it).
        /// </summary>
        public ProgramValue StoreReceiver(int at)
        {
            // A field's increment reads it with ldfld f; a property's (x.P--: a setter) with its getter.
            if (Ops[at].Member is { } stored)
            {
                var (load, name) = Ops[at].OpCode == OpCodes.Stfld ? (OpCodes.Ldfld, stored.Name) : (OpCodes.Callvirt, stored.Name.StartsWith("set_", StringComparison.Ordinal) ? "get_" + stored.Name[4..] : "");
                for (var back = at - 1; back >= 1 && back >= at - 12 && Ops[back].OpCode != OpCodes.Stfld && !(Ops[back].IsCall && Ops[back].Member?.Name == stored.Name); back--)
                    if ((Ops[back].OpCode == load || load == OpCodes.Callvirt && Ops[back].OpCode == OpCodes.Call) && Ops[back].Member?.Name == name && Ops[back].Member?.DeclaringType == stored.DeclaringType && Ops[back - 1].OpCode == OpCodes.Dup)
                        return ReadBack(back - 1, depth: 0).Value;
            }
            return ReceiverValue(at, arguments: 1);
        }

        /// <summary>
        /// What an instance call or a field store at <paramref name="at"/> is made on (the object under its <paramref name="arguments"/>
        /// values on the stack: this, a local), or unknown.
        /// </summary>
        public ProgramValue ReceiverValue(int at, int arguments)
        {
            var cursor = at;
            for (var argument = 0; argument < arguments && cursor >= 0; argument++) cursor = ReadBack(cursor, depth: 0).Start;
            return cursor >= 0 ? ReadBack(cursor, depth: 0).Value : new UnknownValue("receiver");
        }

        /// <summary>The values the call at <paramref name="callAt"/> passes, in order (unknown ones as UnknownValue).</summary>
        public IReadOnlyList<ProgramValue> ArgumentValues(int callAt, CodeMember member)
        {
            var values = new ProgramValue[member.ParameterCount];
            var cursor = callAt;
            for (var parameter = member.ParameterCount - 1; parameter >= 0; parameter--)
            {
                var argument = cursor >= 0 ? ReadBack(cursor, depth: 0) : (Value: new UnknownValue("argument"), Start: -1);
                values[parameter] = argument.Value;
                cursor = argument.Start;
            }
            return values;
        }

        /// <summary>Each way to a return, with the condition it's reached on and the value it returns, in the code's order.</summary>
        public WaysValue ReturnedWays()
        {
            var ways = new List<(ProgramCondition When, ProgramValue Value)>();
            for (var i = 0; i < Ops.Count; i++)
                if (Ops[i].OpCode == OpCodes.Ret && ReachedAt(i) is { IsNever: false } reached) ways.Add((reached, WithFieldsSet(i, ValueBefore(i))));
            return new WaysValue(ways);
        }

        /// <summary>
        /// Each way to a return, with the condition it's reached on and the value its out parameter <paramref name="parameter"/> holds there (a Try
        /// hook's modifiedAmount: the stores to it, stobj or stind through its address, on the way): null when the method has no such parameter.
        /// </summary>
        public WaysValue? OutWays(string parameter)
        {
            if (OutSlot(parameter) is not { } slot) return null;
            var ways = new List<(ProgramCondition When, ProgramValue Value)>();
            for (var i = 0; i < Ops.Count; i++)
            {
                if (Ops[i].OpCode != OpCodes.Ret || ReachedAt(i) is not { IsNever: false } reached) continue;
                var value = OutValueAt(slot, i, depth: 0) ?? new UnknownValue($"{parameter} never set");
                // Set differently on the ways to this return: each of those ways on its own.
                if (value is WaysValue set)
                {
                    foreach (var (when, setValue) in set.Ways)
                        if (reached.And(when) is { IsNever: false } both) ways.Add((both, setValue));
                }
                else ways.Add((reached, value));
            }
            return new WaysValue(ways);
        }

        /// <summary>The argument slot of the out (by reference) parameter named <paramref name="parameter"/>, or null.</summary>
        private int? OutSlot(string parameter)
        {
            if (_method == null) return null;
            var parameters = _method.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
                if (parameters[i].Name == parameter && parameters[i].ParameterType.IsByRef) return _method.IsStatic ? i : i + 1;
            return null;
        }

        /// <summary>
        /// A store through an out parameter's address (ldarg slot; a value; stobj or stind.*), or its default set in place (ldarg slot; initobj:
        /// Artifact's modifiedAmount = default(decimal), read as 0 by <see cref="ParameterSet"/>).
        /// </summary>
        private bool StoresThrough(int index, int slot, int depth)
        {
            var name = Ops[index].OpCode.Name!;
            if (DefaultsThrough(index, slot)) return true;
            if (Ops[index].OpCode != OpCodes.Stobj && !name.StartsWith("stind", StringComparison.Ordinal)) return false;
            var value = ReadBack(index, depth + 1);
            return value.Start > 0 && Ops[value.Start - 1].OpCode.Name!.StartsWith("ldarg", StringComparison.Ordinal) && Ops[value.Start - 1].Slot == slot;
        }

        /// <summary>The out parameter's default set in place through its address (ldarg slot, then initobj of a number: a decimal's or an int's 0).</summary>
        private bool DefaultsThrough(int index, int slot) =>
            Ops[index].OpCode == OpCodes.Initobj && index > 0 && Ops[index - 1].OpCode.Name!.StartsWith("ldarg", StringComparison.Ordinal) && Ops[index - 1].Slot == slot
            && Ops[index].Member is { Name: "Decimal" or "Int32" or "Int64", Namespace: "System" };

        /// <summary>What the out parameter in <paramref name="slot"/> holds just before <paramref name="at"/>: its last store on each way, or null.</summary>
        private ProgramValue? OutValueAt(int slot, int at, int depth) =>
            ParameterSet(index => StoresThrough(index, slot, depth), new UnknownValue("an out parameter never set"), at, depth);

        /// <summary>
        /// A parameter returned with fields the code set on it on the way to that return (ldarga location; a value; stfld pileType: Rebound's
        /// location.pileType = PileType.Draw): the parameter with those fields, each the last store in the straight run of code just before the
        /// return. A store to it anywhere else (before a branch, so it may or may not have run) makes the value unknown.
        /// </summary>
        private ProgramValue WithFieldsSet(int ret, ProgramValue returned)
        {
            if (returned is not ParameterValue parameter) return returned;
            var stores = new List<int>();
            for (var i = 0; i < Ops.Count; i++)
                if (Ops[i].OpCode == OpCodes.Stfld && ReceiverValue(i, arguments: 1) is ParameterValue { Name: var stored } && stored == parameter.Name) stores.Add(i);
            if (stores.Count == 0) return returned;
            var targets = Ops.SelectMany(instruction => instruction.Targets).ToHashSet();
            // The straight run before the return: back to the first branch, return or branch target.
            var start = ret;
            while (start > 0 && !Ops[start - 1].IsBranch && Ops[start - 1].OpCode != OpCodes.Ret && !targets.Contains(Ops[start].Offset)) start--;
            var fields = new List<(string Field, ProgramValue Value)>();
            foreach (var store in stores)
            {
                if (store < start || store > ret)
                {
                    // A store reached on the way to another return only (its own straight run) leaves this one's value as it was.
                    if (!StraightBeforeAnotherReturn(store, targets)) return new UnknownValue($"{parameter.Name}.{Ops[store].Member?.Name} set before a branch");
                    continue;
                }
                var field = Ops[store].Member?.Name ?? "?";
                fields.RemoveAll(set => set.Field == field);
                fields.Add((field, ValueBefore(store)));
            }
            return fields.Count == 0 ? returned : new FieldsSetValue(parameter, fields);
        }

        /// <summary>A store with nothing but straight code between it and a return (no branch, no branch target after it).</summary>
        private bool StraightBeforeAnotherReturn(int store, HashSet<int> targets)
        {
            for (var i = store + 1; i < Ops.Count; i++)
            {
                if (targets.Contains(Ops[i].Offset) || Ops[i].IsBranch) return false;
                if (Ops[i].OpCode == OpCodes.Ret) return true;
            }
            return false;
        }

        /// <summary>What a method returns when one way reaches its return (a helper's value); null otherwise.</summary>
        public ProgramValue? ReturnedValue()
        {
            ProgramValue? returned = null;
            for (var i = 0; i < Ops.Count; i++)
            {
                if (Ops[i].OpCode != OpCodes.Ret || ReachedAt(i) is not { IsNever: false }) continue;
                if (returned != null) return null;
                returned = ValueBefore(i);
            }
            return returned is UnknownValue ? null : returned;
        }

        /// <summary>What a foreach starting at <paramref name="start"/> goes over, as a value: the collection its GetEnumerator is asked of.</summary>
        public ProgramValue? LoopItems(int start)
        {
            for (var i = start - 1; i >= 0 && i >= start - 24; i--)
            {
                if (Ops[i].Member is not { Name: "GetEnumerator" } || !Ops[i].IsCall) continue;
                var items = ReadBack(i, depth: 0).Value;
                return items is UnknownValue ? null : items;
            }
            return null;
        }

        /// <summary>The value on top of the stack just before <paramref name="index"/>, read back as an expression.</summary>
        public ProgramValue ValueBefore(int index) => ReadBack(index, depth: 0).Value;

        /// <summary>
        /// The value the code just before <paramref name="end"/> leaves, and where its code starts (-1 when it can't be read): constants,
        /// the card's values, properties and methods of what they're asked of, comparisons, arithmetic, a lambda, a local or a field
        /// stored once (what was stored), and what an awaited command gave back.
        /// </summary>
        private (ProgramValue Value, int Start) ReadBack(int end, int depth, bool merge = true)
        {
            var at = end - 1;
            if (at < 0 || depth > 16) return (new UnknownValue("value"), -1);
            // Two ways meeting here with a value each (bool flag = a && b): the value is each way's, on its condition.
            if (merge && Merged(end, depth) is { } merged) return merged;
            var instruction = Ops[at];
            var name = instruction.OpCode.Name!;
            if (instruction.Integer is { } constant && name.StartsWith("ldc", StringComparison.Ordinal)) return (new ConstantValue(constant), at);
            if (instruction.OpCode == OpCodes.Ldnull) return (NullValue.Instance, at);
            if (instruction.OpCode == OpCodes.Ldstr) return (new NamedValue("\"" + instruction.Text + "\""), at);
            if (name.StartsWith("ldarg", StringComparison.Ordinal))
            {
                var argument = Argument(instruction.Slot ?? 0);
                // A parameter the body sets (starg) before this read: its last store on each way, else what it was given.
                if (instruction.Slot is { } argumentSlot && argument is ParameterValue
                    && ParameterSet(index => Ops[index].Slot == argumentSlot && Ops[index].OpCode.Name!.StartsWith("starg", StringComparison.Ordinal), argument, at, depth) is { } set)
                    return (set, at);
                return (argument, at);
            }
            if (name.StartsWith("ldloc", StringComparison.Ordinal))
            {
                // A nullable with no value made in place and read at once (ldloca x; initobj Nullable<T>; ldloc x: a ?. chain's null way): null.
                if (!name.StartsWith("ldloca", StringComparison.Ordinal) && at >= 2 && Ops[at - 1].OpCode == OpCodes.Initobj && Ops[at - 1].Member is { Name: "Nullable`1" }
                    && Ops[at - 2].OpCode.Name!.StartsWith("ldloca", StringComparison.Ordinal) && Ops[at - 2].Slot == instruction.Slot && !_jumpsTo.ContainsKey(at - 1) && !_jumpsTo.ContainsKey(at))
                    return (NullValue.Instance, at - 2);
                if (instruction.Slot is { } slot && StoredOnce(slot) is { } store && ReadBack(store, depth + 1) is { Start: >= 0 } stored)
                    // A name (the card itself, kept in a local by the state machine) is the same wherever it's read; anything else is as it
                    // was where it was stored. One stored just before it's read (stloc x; ldloca x: a nullable's GetValueOrDefault) is a temporary
                    // whose code starts where the stored value's does, so what's under it on the stack is read from there.
                    return (stored.Value is NamedValue or ConstantValue or ParameterValue ? stored.Value : new StoredValue("", stored.Value, Ops[store].Offset),
                        store == at - 1 ? stored.Start : at);
                if (instruction.Slot is { } flagSlot && FlagOf(flagSlot) is { } flag) return (new ConditionValue(flag), at);
                if (instruction.Slot is { } lastSlot && LastStored(lastSlot, at, depth) is { } last) return (last, at);
                if (instruction.Slot is { } builtSlot && BuiltOnce(builtSlot, depth) is { } built) return (built, at);
                if (instruction.Slot is { } outSlot && !name.StartsWith("ldloca", StringComparison.Ordinal) && PassedOut(index => Ops[index].Slot == outSlot && Ops[index].OpCode.Name!.StartsWith("ldloca", StringComparison.Ordinal), depth) is { } given)
                    return (given, at);
                return (new UnknownValue("local"), at);
            }
            // An out parameter read through its address (ldarg slot; ldobj or ldind.*: a Try hook's modifiedAmount *= 2): what the code stored there.
            if ((instruction.OpCode == OpCodes.Ldobj || name.StartsWith("ldind", StringComparison.Ordinal)) && at > 0
                && Ops[at - 1].OpCode.Name!.StartsWith("ldarg", StringComparison.Ordinal) && Ops[at - 1].Slot is { } addressSlot && !_jumpsTo.ContainsKey(at)
                && _method?.GetParameters() is { } addressParameters && (_method.IsStatic ? addressSlot : addressSlot - 1) is var addressIndex
                && addressIndex >= 0 && addressIndex < addressParameters.Length && addressParameters[addressIndex].ParameterType.IsByRef)
                return (OutValueAt(addressSlot, at - 1, depth) ?? new UnknownValue("an out parameter never set"), at - 1);
            if (name.StartsWith("conv", StringComparison.Ordinal) || instruction.OpCode == OpCodes.Box || instruction.OpCode == OpCodes.Unbox_Any
                || instruction.OpCode == OpCodes.Castclass)
                return ReadBack(at, depth + 1);
            // A negation (-powerAmount: Malaise's Strength loss) is decimal's own (op_UnaryNegation), which the evaluator works out.
            if (instruction.OpCode == OpCodes.Neg)
            {
                var negated = ReadBack(at, depth + 1);
                return negated.Start < 0 ? (new UnknownValue("neg"), -1)
                    : (new QueryValue("Decimal", "op_UnaryNegation", null, new[] { negated.Value }, "Int32"), negated.Start);
            }
            // A copy (dup) is the value it copies; so is what a test of a copy leaves below it (x?.y, pile == null || pile.Type...: dup, brfalse).
            if (instruction.OpCode == OpCodes.Dup) return ReadBack(at, depth + 1);
            if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch && at > 0 && Ops[at - 1].OpCode == OpCodes.Dup
                && CodeDecoder.StackEffect(instruction) is (1, 0))
                return ReadBack(at - 1, depth + 1);
            // x is T (isinst): x when it's a T, else null.
            if (instruction.OpCode == OpCodes.Isinst && instruction.Member is { } tested)
            {
                var inner = ReadBack(at, depth + 1);
                return (new QueryValue(tested.Name, "is", inner.Value, Array.Empty<ProgramValue>(), tested.Name), inner.Start);
            }
            if (instruction.OpCode == OpCodes.Ldsfld && instruction.Member is { } staticField) return (new NamedValue(staticField.DeclaringType + "." + staticField.Name), at);
            // A cached lambda (ldsfld <>9__x; dup; brtrue; pop; ldsfld <>9; ldftn; newobj; dup; stsfld <>9__x): the lambda.
            if (instruction.OpCode == OpCodes.Stsfld && instruction.Member is { } cache && cache.Name.StartsWith("<>9__", StringComparison.Ordinal))
            {
                for (var back = at - 1; back >= 0 && back >= at - 12; back--)
                    if (Ops[back].OpCode == OpCodes.Ldsfld && Ops[back].Member?.Name == cache.Name)
                        return (LambdaBetween(back, at), back);
                return (new UnknownValue("lambda"), -1);
            }
            if ((instruction.OpCode == OpCodes.Ldfld || instruction.OpCode == OpCodes.Ldflda) && instruction.Member is { } field)
            {
                var receiver = ReadBack(at, depth + 1);
                // The state machine's or a closure's fields: the card, a parameter (cardPlay), a local kept across awaits.
                if (field.DeclaringType.StartsWith('<') || field.Name.StartsWith('<'))
                {
                    if (field.Name == "<>4__this") return (NamedValue.This, receiver.Start);
                    if (field.Name.Contains(">5__", StringComparison.Ordinal) && FieldStoredOnce(field.Name) is { } fieldStore && ReadBack(fieldStore, depth + 1) is { Start: >= 0 } stored)
                        return (new StoredValue(FieldName(field.Name), stored.Value, Ops[fieldStore].Offset), receiver.Start);
                    // A local a lambda captures (a closure's field: Ki Chant's drawPile), stored once here: what was stored.
                    if (field.DeclaringType.StartsWith("<>c__DisplayClass", StringComparison.Ordinal) && !field.Name.StartsWith('<')
                        && FieldStoredOnce(field.Name) is { } capturedStore && ReadBack(capturedStore, depth + 1) is { Start: >= 0, Value: not (ParameterValue or NamedValue) } captured)
                        return (captured.Value is ConstantValue ? captured.Value : new StoredValue(field.Name, captured.Value, Ops[capturedStore].Offset), receiver.Start);
                    // A local kept across awaits and stored on several ways (Tempest's X, one more upgraded): its last store on each way.
                    if (field.Name.Contains(">5__", StringComparison.Ordinal) && FieldLastStored(field.Name, at, depth) is { } last)
                        return (last is StoredValue lastStore ? lastStore with { Name = FieldName(field.Name) } : last, receiver.Start);
                    // A local kept across awaits that a call fills as its out parameter (Remove(card, out lightning)): what the call gave back.
                    if (field.Name.Contains(">5__", StringComparison.Ordinal) && instruction.OpCode == OpCodes.Ldfld
                        && PassedOut(index => Ops[index].OpCode == OpCodes.Ldflda && Ops[index].Member?.Name == field.Name, depth) is { } givenOut)
                        return (givenOut, receiver.Start);
                    var named = new NamedValue(FieldName(field.Name));
                    // A parameter the state machine keeps, set by the body before this read (Personal Hive's `dealer = dealer.PetOwner.Creature` for
                    // Osty's hit): its last store on each way, else what it was given.
                    if (field.DeclaringType.Contains(">d__", StringComparison.Ordinal) && !field.Name.StartsWith('<')
                        && ParameterSet(index => Ops[index].OpCode == OpCodes.Stfld && Ops[index].Member?.Name == field.Name, named, at, depth) is { } set)
                        return (set, receiver.Start);
                    return (named, receiver.Start);
                }
                return (new QueryValue(field.DeclaringType, field.Name, receiver.Value, Array.Empty<ProgramValue>(), field.ValueType), receiver.Start);
            }
            if (name is "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un")
            {
                var right = ReadBack(at, depth + 1);
                if (right.Start < 0) return (new UnknownValue(name), -1);
                var left = ReadBack(right.Start, depth + 1);
                var comparison = name switch
                {
                    "ceq" => Comparison.Equal,
                    // x != null: cgt.un against null.
                    "cgt.un" when right.Value is NullValue => Comparison.NotEqual,
                    "cgt" or "cgt.un" => Comparison.Greater,
                    _ => Comparison.Less,
                };
                // !(a == b): ceq against 0 of a comparison is the other comparison.
                if (name == "ceq" && right.Value is ConstantValue { Value: 0 } && left.Value is CompareValue negated)
                    return (negated with { Op = CompareValue.Negated(negated.Op) }, left.Start);
                return (new CompareValue(comparison, left.Value, right.Value) { Unsigned = name is "clt.un" || name == "cgt.un" && right.Value is not NullValue }, left.Start);
            }
            if (name is "add" or "sub" or "mul" or "div" or "rem")
            {
                var right = ReadBack(at, depth + 1);
                if (right.Start < 0) return (new UnknownValue(name), -1);
                var left = ReadBack(right.Start, depth + 1);
                return (new ArithmeticValue(name, left.Value, right.Value), left.Start);
            }
            // An array the code fills (newarr; dup; index; value; stelem, for each item).
            if (instruction.OpCode == OpCodes.Newarr)
            {
                var length = ReadBack(at, depth + 1);
                return (new ArrayValue(Array.Empty<ProgramValue>()), length.Start);
            }
            if (name.StartsWith("stelem", StringComparison.Ordinal))
            {
                var item = ReadBack(at, depth + 1);
                var index = item.Start >= 0 ? ReadBack(item.Start, depth + 1) : item;
                if (index.Start < 1 || Ops[index.Start - 1].OpCode != OpCodes.Dup) return (new UnknownValue("array"), -1);
                var array = ReadBack(index.Start - 1, depth + 1);
                return array.Value is ArrayValue filled ? (new ArrayValue(filled.Items.Append(item.Value).ToList()), array.Start) : (new UnknownValue("array"), -1);
            }
            if (instruction.OpCode == OpCodes.Newobj && at > 0 && Ops[at - 1].OpCode == OpCodes.Ldftn)
            {
                var target = ReadBack(at - 1, depth + 1);
                return (MethodGroup(LambdaBetween(at - 1, at), Ops[at - 1].Member, target.Value), target.Start);
            }
            // A method of an object as a delegate (topCards.Contains: x; dup; ldvirtftn; newobj).
            if (instruction.OpCode == OpCodes.Newobj && at > 1 && Ops[at - 1].OpCode == OpCodes.Ldvirtftn && Ops[at - 2].OpCode == OpCodes.Dup)
            {
                var target = ReadBack(at - 2, depth + 1);
                return (MethodGroup(new UnknownValue("delegate"), Ops[at - 1].Member, target.Value), target.Start);
            }
            if (!instruction.IsCall || instruction.Member is not { } member) return (new UnknownValue(name), -1);
            if (member.Name == "GetResult") return AwaitedResult(at, depth);
            if (member.Name is "op_Implicit" or "op_Explicit") return ReadBack(at, depth + 1);
            var cursor = at;
            var arguments = new ProgramValue[member.ParameterCount];
            for (var parameter = member.ParameterCount - 1; parameter >= 0; parameter--)
            {
                var argument = ReadBack(cursor, depth + 1);
                if (argument.Start < 0) return (new UnknownValue(member.Name), -1);
                arguments[parameter] = argument.Value;
                cursor = argument.Start;
            }
            ProgramValue? of = null;
            if (member.HasThis && instruction.OpCode != OpCodes.Newobj)
            {
                var receiver = ReadBack(cursor, depth + 1);
                if (receiver.Start < 0) return (new UnknownValue(member.Name), -1);
                of = receiver.Value;
                cursor = receiver.Start;
            }
            // DynamicVars.Cards.BaseValue, DynamicVars["PlayMax"].IntValue: the card's value.
            // A calculated value worked out (DynamicVars.CalculatedCards.Calculate(target)): the card's value, on the play's target.
            if (member.Name is "get_BaseValue" or "get_IntValue" or "Calculate")
            {
                if (of is QueryValue { Name: "Item", Arguments: [NamedValue key] }) return (new CardValue(key.Name.Trim('"')), cursor);
                if (of is QueryValue { Of: QueryValue { Name: "DynamicVars" } } value) return (new CardValue(_varKeyOf?.Invoke(value.Name) ?? value.Name), cursor);
            }
            // A foreach's item (enumerator.Current): each item it goes over.
            if (member.Name == "get_Current") return (new NamedValue("each"), cursor);
            // A helper of the card's own that works out a value: what it returns.
            if (_helperOf?.Invoke(member, arguments) is { } helped) return (helped, cursor);
            var property = member.Name.StartsWith("get_", StringComparison.Ordinal);
            // A constructor of a generic type keeps the type's arguments (new PowerVar<AimPower>(12): AimPower).
            return (new QueryValue(member.DeclaringType, property ? member.Name[4..] : member.Name, of, arguments, member.ValueType)
                { IsProperty = property && arguments.Length == 0, GenericArguments = member.Name == ".ctor" && member.GenericArguments.Count == 0 ? member.TypeArguments : member.GenericArguments }, cursor);
        }

        /// <summary>What an await gave back (the call GetAwaiter was asked of, before the suspend): the command's result.</summary>
        private (ProgramValue Value, int Start) AwaitedResult(int getResult, int depth)
        {
            // ... call X; callvirt GetAwaiter; stloc a; ldloca a; call get_IsCompleted; brtrue resume; (suspend) resume: ldloca a; call GetResult
            var resume = getResult - 1;
            var jump = _awaitJumps.FirstOrDefault(entry => entry.Value == resume).Key;
            if (jump < 4 || Ops[jump - 4].Member is not { Name: "GetAwaiter" }) return (new UnknownValue("await"), -1);
            var awaited = ReadBack(jump - 4, depth + 1);
            if (awaited.Start < 0) return awaited;
            return awaited.Value is QueryValue query
                ? (new ResultValue(query.Type + "." + query.Name, Ops[jump - 5].Offset, query.Arguments), awaited.Start)
                : awaited;
        }

        /// <summary>
        /// A delegate read: the lambda when its code was read, else a test a method of another object makes of its one argument
        /// (topCards.Contains as x => topCards.Contains(x)); the lambda as read otherwise.
        /// </summary>
        private static ProgramValue MethodGroup(ProgramValue read, CodeMember? method, ProgramValue receiver)
        {
            if (read is not UnknownValue || method is not { Kind: CodeMemberKind.Method, ParameterCount: 1, ValueType: "Boolean" }) return read;
            var test = new QueryValue(method.DeclaringType, method.Name, method.HasThis ? receiver : null, new ProgramValue[] { new ParameterValue("x") }, method.ValueType);
            return new LambdaValue("x", ProgramCondition.Of(new ConditionLiteral($"delegate:{method.FullName}", test, Holds: true)));
        }

        /// <summary>The lambda an ldftn in [from, to) points to.</summary>
        private ProgramValue LambdaBetween(int from, int to)
        {
            for (var i = from; i < to; i++)
                if (Ops[i].OpCode == OpCodes.Ldftn && Ops[i].Member is { } lambda)
                    return _lambdaOf?.Invoke(lambda) is { } read ? read : new UnknownValue(lambda.Name);
            return new UnknownValue("lambda");
        }

        /// <summary>A load of argument <paramref name="slot"/>: the card in its own methods, a lambda's parameter, the state machine.</summary>
        private ProgramValue Argument(int slot)
        {
            if (_method == null) return new UnknownValue("argument");
            var parameters = _method.GetParameters();
            if (_method.IsStatic) return slot < parameters.Length ? new ParameterValue(parameters[slot].Name ?? "x") : new UnknownValue("argument");
            if (slot == 0)
            {
                var owner = _method.DeclaringType?.Name ?? "";
                return owner.StartsWith('<') ? new NamedValue("closure") : NamedValue.This;
            }
            return slot - 1 < parameters.Length ? new ParameterValue(parameters[slot - 1].Name ?? "x") : new UnknownValue("argument");
        }

        private void MarkMachinery()
        {
            // The state local: ldfld <>1__state; stloc.
            int? stateSlot = null;
            for (var i = 0; i + 1 < Ops.Count; i++)
                if (Ops[i].Member is { Name: "<>1__state" } && Ops[i].OpCode == OpCodes.Ldfld && Ops[i + 1].OpCode.Name!.StartsWith("stloc", StringComparison.Ordinal))
                {
                    stateSlot = Ops[i + 1].Slot;
                    break;
                }
            for (var i = 0; i < Ops.Count; i++)
            {
                var instruction = Ops[i];
                // Leaving the try (the end, or an await's suspend), finally blocks' end, the return.
                if (instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S || instruction.OpCode == OpCodes.Endfinally || instruction.OpCode == OpCodes.Ret) _noise[i] = true;
                if (!instruction.IsBranch && instruction.OpCode != OpCodes.Switch) continue;
                // The dispatch on the state (where to resume), and a finally's "not suspended" check.
                if (stateSlot != null && LoadsState(i, stateSlot.Value))
                {
                    _noise[i] = true;
                    _dispatch.Add(i);
                }
                // An await: awaiter.IsCompleted, else suspend (and later resume) before GetResult. Everything up to the jump's target
                // is the machinery.
                if (i > 0 && Ops[i - 1].Member is { Name: "get_IsCompleted" } && instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
                {
                    var target = IndexOf(instruction.Targets[0]);
                    _awaitJumps[i] = target;
                    for (var j = i; j < target && j < Ops.Count; j++) _noise[j] = true;
                }
            }
        }

        /// <summary>
        /// The branch at <paramref name="index"/> tests the state local: ldloc state, then constants and arithmetic on them
        /// (state == 1, state - 1 &lt;= 1).
        /// </summary>
        private bool LoadsState(int index, int stateSlot)
        {
            for (var back = 1; back <= 5 && index - back >= 0; back++)
            {
                var previous = Ops[index - back];
                var name = previous.OpCode.Name!;
                if (name.StartsWith("ldloc", StringComparison.Ordinal) && previous.Slot == stateSlot) return true;
                if (previous.Integer == null && name is not ("sub" or "add" or "conv.u4" or "conv.i4")) return false;
            }
            return false;
        }

        /// <summary>A conditional jump back (not machinery) closes a loop that starts at its target.</summary>
        private void FindLoops()
        {
            for (var i = 0; i < Ops.Count; i++)
            {
                var instruction = Ops[i];
                if (_noise[i] || instruction.OpCode.FlowControl != FlowControl.Cond_Branch) continue;
                var target = IndexOf(instruction.Targets[0]);
                if (target >= i) continue;
                // A for, while or foreach jumps over its body to its condition first: the loop starts after that jump.
                if (!_loopEnds.TryGetValue(target, out var existing) || existing < i) _loopEnds[target] = i;
            }
        }

        /// <summary>What a loop goes over: the enumerator it moves (a foreach) or the value its counter is held under.</summary>
        public string LoopOver(int start, int end)
        {
            for (var i = end; i > start; i--)
            {
                var member = Ops[i].Member;
                if (member is { Name: "MoveNext" }) return EnumeratedBefore(start) is { } items ? "each of " + items : "each item";
                if (member is { Name: "get_IntValue" or "get_BaseValue" } && i > 0 && Ops[i - 1].Member is { } value) return value.Name.Replace("get_", "");
                if (member is { Name: "get_Item" } && Ops[i - 1].Text is { } key) return key;
            }
            return "?";
        }

        /// <summary>What a foreach starting at <paramref name="start"/> goes over: the collection whose GetEnumerator it calls first ("Hand.Cards.ToList").</summary>
        private string? EnumeratedBefore(int start)
        {
            for (var i = start - 1; i >= 0 && i >= start - 24; i--)
            {
                if (Ops[i].Member is not { Name: "GetEnumerator" } enumerator || !Ops[i].IsCall) continue;
                var items = Source(i, enumerator, parameter: enumerator.HasThis ? -1 : 0);
                return items == "?" || items.StartsWith("ld", StringComparison.Ordinal) ? null : items;
            }
            return null;
        }

        /// <summary>
        /// Where argument <paramref name="parameter"/> of the call at <paramref name="callAt"/> comes from (-1: the instance it's
        /// called on): the value it reads ("Block", "Damage", a DynamicVars key), whose property it is ("Owner.Creature",
        /// "cardPlay.Target", "target.Creature"), a constant, or "?" when the way back isn't straight.
        /// </summary>
        public string Source(int callAt, CodeMember called, int parameter, int depth = 0) =>
            CodeDecoder.Producer(Ops, callAt, CodeDecoder.ArgumentDepth(called, parameter), stop: index => _noise[index]) is { } producer ? Describe(producer, depth) : "?";

        private string Describe(int index, int depth)
        {
            var instruction = Ops[index];
            if (instruction.Integer is { } constant) return constant.ToString();
            if (instruction.OpCode == OpCodes.Ldnull) return "null";
            if (instruction.Member is { } member)
            {
                // DynamicVars.Block.BaseValue, DynamicVars["Name"].IntValue: the value's name.
                if (member.Name is "get_BaseValue" or "get_IntValue" && index > 0 && Ops[index - 1].Member is { } holder)
                    return holder.Name == "get_Item" && index > 1 && Ops[index - 2].Text is { } key ? key : holder.Name.Replace("get_", "");
                if (member.Name == "get_Item" && index > 0 && Ops[index - 1].Text is { } itemKey) return itemKey;
                // A state machine's field: the card itself, a parameter (cardPlay) or a local kept across awaits (<target>5__2).
                if (member.Kind == CodeMemberKind.Field)
                {
                    // A local kept across awaits and stored once (<cards>5__2 = hand.Cards.ToList()): what was stored.
                    if (member.Name.Contains(">5__", StringComparison.Ordinal) && depth < 3 && FieldStoredOnce(member.Name) is { } fieldStore
                        && CodeDecoder.Producer(Ops, fieldStore, depth: 0, stop: at => _noise[at]) is { } fieldValue)
                    {
                        var storedInField = Describe(fieldValue, depth + 1);
                        if (storedInField != "?" && !storedInField.StartsWith("ld", StringComparison.Ordinal)) return storedInField;
                    }
                    return FieldName(member.Name);
                }
                var name = member.Name.Replace("get_", "");
                // A property of something: whose (base.Owner.Creature, cardPlay.Target, a chosen teammate's Creature).
                if (member.Kind == CodeMemberKind.Method && member.HasThis && member.Name.StartsWith("get_", StringComparison.Ordinal) && depth < 3)
                {
                    var receiver = Source(index, member, parameter: -1, depth + 1);
                    if (receiver is not ("this" or "?") && !receiver.StartsWith("ld", StringComparison.Ordinal)) name = receiver + "." + name;
                }
                // A pile (PileType.Hand.GetPile(owner)): by its type, "Hand".
                if (member is { Kind: CodeMemberKind.Method, HasThis: false, Name: "GetPile", ParameterCount: >= 1 } && depth < 4
                    && int.TryParse(Source(index, member, parameter: 0, depth + 1), out var pileType)
                    && Enum.IsDefined(typeof(MegaCrit.Sts2.Core.Entities.Cards.PileType), pileType))
                    return ((MegaCrit.Sts2.Core.Entities.Cards.PileType)pileType).ToString();
                // A query over a collection (cards.ToList(), cards.Where(...)): of what.
                if (member is { Kind: CodeMemberKind.Method, HasThis: false, Namespace: "System.Linq", ParameterCount: >= 1 } && depth < 3)
                {
                    var source = Source(index, member, parameter: 0, depth + 1);
                    if (source != "?" && !source.StartsWith("ld", StringComparison.Ordinal)) name = source + "." + name;
                }
                return name;
            }
            if (instruction.Text is { } text) return "\"" + text + "\"";
            // A local stored once in the method (int x = ResolveEnergyXValue(); ... WithHitCount(x)): what was stored.
            if (instruction.Slot is { } slot && instruction.OpCode.Name!.StartsWith("ldloc", StringComparison.Ordinal) && depth < 3 && StoredOnce(slot) is { } store
                && CodeDecoder.Producer(Ops, store, depth: 0, stop: at => _noise[at]) is { } stored)
            {
                var described = Describe(stored, depth + 1);
                if (described != "?" && !described.StartsWith("ld", StringComparison.Ordinal)) return described;
            }
            return instruction.OpCode.Name ?? "?";
        }

        /// <summary>
        /// The one instruction that stores the state machine's field <paramref name="field"/>, when only one does (the machine clearing
        /// it to null when it finishes isn't a value of it).
        /// </summary>
        private int? FieldStoredOnce(string field)
        {
            int? found = null;
            for (var i = 0; i < Ops.Count; i++)
            {
                if (Ops[i].OpCode != OpCodes.Stfld || Ops[i].Member?.Name != field || i > 0 && Ops[i - 1].OpCode == OpCodes.Ldnull) continue;
                if (found != null) return null;
                found = i;
            }
            return found;
        }

        /// <summary>
        /// A local set to true or false on several ways (bool flag = a || b ? true : false): when it's true, each way that stores true;
        /// null when one of its stores isn't a constant or isn't reached yet.
        /// </summary>
        private ProgramCondition? FlagOf(int slot)
        {
            var flag = ProgramCondition.Never;
            var stores = 0;
            for (var i = 0; i < Ops.Count; i++)
            {
                if (Ops[i].Slot != slot || !Ops[i].OpCode.Name!.StartsWith("stloc", StringComparison.Ordinal)) continue;
                stores++;
                if (i == 0 || Ops[i - 1].Integer is not { } stored || !Ops[i - 1].OpCode.Name!.StartsWith("ldc", StringComparison.Ordinal)) return null;
                if (stored == 0) continue;
                if (i >= _reached.Length || _reached[i] is not { } reached) return null;
                flag = flag.Or(reached);
            }
            return stores > 1 ? flag : null;
        }

        /// <summary>
        /// A true-or-false local stored on several ways before it's read at <paramref name="loadAt"/> (bool flag2 = flag; if (flag2) flag2 =
        /// type test: AllForOne's Filter), as a condition: each store's value on the ways it's the last store made (no later one reached).
        /// Null when a store isn't a true-or-false, isn't reached yet, or a store after the read is in a loop with it.
        /// </summary>
        private ProgramValue? LastStored(int slot, int loadAt, int depth) =>
            LastStored(index => Ops[index].Slot == slot && Ops[index].OpCode.Name!.StartsWith("stloc", StringComparison.Ordinal), loadAt, depth);

        /// <summary>The same for a local the state machine keeps across awaits (a field: Tempest's numOfOrbs = X, then one more upgraded).</summary>
        private ProgramValue? FieldLastStored(string field, int loadAt, int depth) =>
            LastStored(index => Ops[index].OpCode == OpCodes.Stfld && Ops[index].Member?.Name == field && !(index > 0 && Ops[index - 1].OpCode == OpCodes.Ldnull),
                loadAt, depth);

        private ProgramValue? LastStored(Func<int, bool> isStore, int loadAt, int depth)
        {
            var stores = new List<int>();
            for (var i = 0; i < Ops.Count; i++)
            {
                if (!isStore(i)) continue;
                // A store in a loop the read is in can come from an earlier round (a loop's counter): not read.
                if (_loopEnds.Any(loop => loop.Key <= loadAt && loadAt <= loop.Value && loop.Key <= i && i <= loop.Value)) return null;
                if (i < loadAt) stores.Add(i);
            }
            if (stores.Count == 0) return null;
            // The last store, when the code runs straight from it to the read (no jump on the way or into it: a temporary the compiler
            // reuses, an awaited result kept a moment), is the value.
            var lastStore = stores[^1];
            var straight = true;
            for (var i = lastStore; i < loadAt && straight; i++)
                straight = !Ops[i].IsBranch && !_jumpsTo.ContainsKey(i + 1);
            if (stores.Count == 1 || straight)
                return ReadBack(lastStore, depth + 1) is { Start: >= 0 } only ? new StoredValue("", only.Value, Ops[lastStore].Offset) : null;
            var value = ProgramCondition.Never;
            var later = ProgramCondition.Never;
            var ways = new List<(ProgramCondition When, ProgramValue Value)>();
            var truths = true;
            for (var storeIndex = stores.Count - 1; storeIndex >= 0; storeIndex--)
            {
                var store = stores[storeIndex];
                if (store >= _reached.Length || _reached[store] is not { } reached) return null;
                var stored = ReadBack(store, depth + 1);
                if (stored.Start < 0) return null;
                var last = reached.And(later.Not());
                later = later.Or(reached);
                if (last.IsNever) continue;
                ways.Insert(0, (last, stored.Value));
                if (AsCondition(stored.Value, $"{_site}:{Ops[store].Offset:X4}:stored") is { } holds) value = value.Or(last.And(holds));
                else truths = false;
            }
            return truths ? new ConditionValue(value) : new WaysValue(ways);
        }

        /// <summary>
        /// A parameter the body sets before the read at <paramref name="loadAt"/> (a store <paramref name="isStore"/> finds): what the read gives on
        /// each way, the last store reached there or, where none is, the parameter as it was given (<paramref name="given"/>). Null when the body
        /// never sets it before the read; an unknown value when a store can't be read or one in a loop with the read can come from an earlier
        /// round, so a parameter set is never read as the one given.
        /// </summary>
        private ProgramValue? ParameterSet(Func<int, bool> isStore, ProgramValue given, int loadAt, int depth)
        {
            var stores = new List<int>();
            for (var i = 0; i < Ops.Count; i++)
            {
                if (!isStore(i)) continue;
                if (_loopEnds.Any(loop => loop.Key <= loadAt && loadAt <= loop.Value && loop.Key <= i && i <= loop.Value)) return new UnknownValue($"{given} set in a loop");
                if (i < loadAt) stores.Add(i);
            }
            if (stores.Count == 0) return null;
            var later = ProgramCondition.Never;
            var ways = new List<(ProgramCondition When, ProgramValue Value)>();
            for (var storeIndex = stores.Count - 1; storeIndex >= 0; storeIndex--)
            {
                var store = stores[storeIndex];
                if (store >= _reached.Length || _reached[store] is not { } reached) return new UnknownValue($"{given} set");
                // A number's default set in place (initobj through its address: DefaultsThrough) stores 0.
                var stored = Ops[store].OpCode == OpCodes.Initobj ? (Value: (ProgramValue)new ConstantValue(0), Start: store) : ReadBack(store, depth + 1);
                if (stored.Start < 0) return new UnknownValue($"{given} set");
                var last = reached.And(later.Not());
                later = later.Or(reached);
                if (!last.IsNever) ways.Insert(0, (last, stored.Value));
            }
            var unset = later.Not();
            if (!unset.IsNever) ways.Add((unset, given));
            return ways.Count == 1 && ways[0].When.IsAlways ? ways[0].Value : new WaysValue(ways);
        }

        /// <summary>
        /// A struct local built in place once (ldloca slot; ...; call T::.ctor: Purity's CardSelectorPrefs): the constructor with its
        /// arguments; null otherwise.
        /// </summary>
        private ProgramValue? BuiltOnce(int slot, int depth)
        {
            ProgramValue? built = null;
            for (var i = 0; i < Ops.Count; i++)
            {
                if (Ops[i] is not { Member: { Name: ".ctor", HasThis: true } constructor } || Ops[i].OpCode != OpCodes.Call) continue;
                if (CodeDecoder.Producer(Ops, i, constructor.ParameterCount) is not { } receiver || Ops[receiver].Slot != slot
                    || !Ops[receiver].OpCode.Name!.StartsWith("ldloca", StringComparison.Ordinal)) continue;
                if (built != null) return null;
                var arguments = ArgumentValues(i, constructor);
                built = new QueryValue(constructor.DeclaringType, ".ctor", null, arguments, constructor.DeclaringType);
            }
            return depth > 8 ? null : built;
        }

        /// <summary>
        /// A local only a call fills, by its address (out var x: ldloca, or a state machine's field by ldflda), at the one place the code
        /// passes it: what the call gave back through that parameter (OutValue); null when it's passed more than once or not to a call.
        /// </summary>
        private ProgramValue? PassedOut(Func<int, bool> isAddress, int depth)
        {
            int? address = null;
            for (var i = 0; i < Ops.Count; i++)
            {
                if (!isAddress(i)) continue;
                if (address != null) return null;
                address = i;
            }
            if (address is not { } at || depth > 8) return null;
            for (var call = at + 1; call < Ops.Count && call <= at + 12; call++)
            {
                if (!Ops[call].IsCall || Ops[call].Member is not { Kind: CodeMemberKind.Method } member) continue;
                for (var parameter = 0; parameter < member.ParameterCount; parameter++)
                    if (CodeDecoder.Producer(Ops, call, CodeDecoder.ArgumentDepth(member, parameter), stop: index => _noise[index]) == at)
                        return ReadBack(call + 1, depth + 1, merge: false) is { Start: >= 0 } made ? new OutValue(made.Value, parameter) : null;
            }
            return null;
        }

        /// <summary>The one instruction that stores local <paramref name="slot"/>, when only one does (else null: which value is meant isn't straight).</summary>
        private int? StoredOnce(int slot)
        {
            int? found = null;
            for (var i = 0; i < Ops.Count; i++)
            {
                if (Ops[i].Slot != slot || !Ops[i].OpCode.Name!.StartsWith("stloc", StringComparison.Ordinal)) continue;
                if (found != null) return null;
                found = i;
            }
            return found;
        }

        /// <summary>"&lt;&gt;4__this" is the card; "&lt;target&gt;5__2" is the local "target"; a parameter keeps its name.</summary>
        private static string FieldName(string field)
        {
            if (field == "<>4__this") return "this";
            if (field.StartsWith('<') && field.IndexOf('>') is var close and > 1) return field[1..close];
            return field;
        }
    }
}