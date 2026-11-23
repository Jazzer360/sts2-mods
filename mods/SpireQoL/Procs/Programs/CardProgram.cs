using System.Reflection;
using System.Reflection.Emit;

namespace SpireQoL.Procs.Programs;

/// <summary>One step of a play, in the order the card's code runs it (decision #182, docs/CARD_PROGRAMS.md).</summary>
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
    /// <summary>An attack's builder calls (WithHitCount, Targeting, TargetingAllOpponents, BeforeDamage...), in order.</summary>
    public IReadOnlyList<string> Builder { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Where an attack's WithHitCount gets its count ("HitCount", "Repeat", a constant, "ResolveEnergyXValue" for an X-cost card's X);
    /// null when its builder sets none (one hit).
    /// </summary>
    public string? HitCount { get; init; }

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

    public override string ToString() => "choice " + Command.Name + When;
}

/// <summary>
/// A card the code makes (CombatState.CreateCard&lt;T&gt;: Jest's Sadness, Mourning Blow's): not a change by itself (a command adds it to
/// a pile, or the card turns into it after the play), but what it makes, by type, for the rules that ask (CardEffects.MakesSadness).
/// </summary>
public sealed record CreateStep(int Offset, string Card) : ProgramStep(Offset)
{
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

    public override string ToString() => (Over == "?" && While != null ? $"loop while {While}" : $"loop over {Over}") + $" [{string.Join(", ", Body)}]" + When;
}

/// <summary>Something the reader couldn't follow: the steps it holds or stands for aren't known.</summary>
public sealed record UnknownStep(int Offset, string Reason) : ProgramStep(Offset)
{
    public override string ToString() => "unknown: " + Reason + When;
}

/// <summary>A card's play as its code runs it: its steps in order, and what the reader couldn't follow.</summary>
public sealed record CardProgram(string Card, IReadOnlyList<ProgramStep> Steps)
{
    public static readonly CardProgram Empty = new("", Array.Empty<ProgramStep>());

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

    public override string ToString() => Card + ": " + string.Join(", ", Steps);
}

/// <summary>The loaded game's card programs, read once per card type, and what they say about order.</summary>
public static class CardPrograms
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, CardProgram> Cache = new();

    /// <summary>The card's program (empty when its code can't be read).</summary>
    public static CardProgram Of(Type cardType) => Cache.GetOrAdd(cardType, type =>
    {
        try { return CardProgramReader.Read(type, RuntimeCode.Read); }
        catch { return CardProgram.Empty; }
    });

    /// <summary>
    /// The card discards before it draws (The Hermit, Calculated Gamble's DiscardAndDraw), so its draws have the discarded cards'
    /// room; Acrobatics draws first.
    /// </summary>
    public static bool DiscardsBeforeDraws(Type cardType) => Before(Of(cardType), "Discard", "Draw");

    /// <summary>The program's first command whose name starts with <paramref name="first"/> comes before its first <paramref name="second"/>.</summary>
    internal static bool Before(CardProgram program, string first, string second)
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
/// Reads a card's OnPlay (its async state machine, the local functions it calls) into a <see cref="CardProgram"/>. Works on any
/// branch's code: types are matched by name, and the instructions come from the reader it's given (the loaded game's module, or
/// an assembly file's metadata in the tests). Reads code only, never the game's state or its RNG.
/// </summary>
public static class CardProgramReader
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const int MaxDepth = 3;

    /// <summary>Commands that only show or sound something: they change nothing.</summary>
    private static readonly HashSet<string> ShowOnlyTypes = new() { "Cmd", "SfxCmd", "VfxCmd", "TalkCmd", "ThinkCmd", "MelodyCmd" };

    public static CardProgram Read(Type cardType, Func<MethodBase, List<CodeOp>> read, string method = "OnPlay")
    {
        var body = Body(cardType, method);
        if (body == null) return new CardProgram(cardType.Name, Array.Empty<ProgramStep>());
        var steps = new Reader(cardType, read).Steps(body, depth: 0);
        return new CardProgram(cardType.Name, steps);
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

    private static bool ShowsOnly(string name) =>
        name.StartsWith("Preview", StringComparison.Ordinal) || name.StartsWith("TriggerAnim", StringComparison.Ordinal) || name.Contains("Vfx")
        || name.Contains("Wait") || name.StartsWith("Shake", StringComparison.Ordinal) || name.StartsWith("Play", StringComparison.Ordinal) && name.EndsWith("Anim", StringComparison.Ordinal);

    /// <summary>A question to the game (ArrowCmd.PlayerHasAnyArrows), not a change.</summary>
    private static bool Asks(string name) =>
        name.StartsWith("Is", StringComparison.Ordinal) || name.StartsWith("Has", StringComparison.Ordinal) || name.StartsWith("PlayerHas", StringComparison.Ordinal)
        || name.StartsWith("CreatureIs", StringComparison.Ordinal) || name.StartsWith("Find", StringComparison.Ordinal) || name.StartsWith("Get", StringComparison.Ordinal);

    private sealed class Reader
    {
        private readonly Type _card;
        private readonly Func<MethodBase, List<CodeOp>> _read;

        public Reader(Type card, Func<MethodBase, List<CodeOp>> read)
        {
            _card = card;
            _read = read;
        }

        public List<ProgramStep> Steps(MethodBase method, int depth)
        {
            List<CodeOp> ops;
            try { ops = _read(method); }
            catch (Exception ex) { return new List<ProgramStep> { new UnknownStep(0, $"{method.Name} can't be read ({ex.GetType().Name})") }; }
            var flow = Flow.Of(ops, method, depth < MaxDepth ? LambdaOf : null);
            var steps = new List<ProgramStep>();
            Parse(flow, 0, flow.Ops.Count, steps, outer: null, depth);
            return steps;
        }

        /// <summary>A lambda the code makes (a predicate for Any, Where...): its parameter and what it returns.</summary>
        private LambdaValue? LambdaOf(CodeMember member)
        {
            if (Lambda(member) is not { } body) return null;
            try
            {
                var flow = Flow.Of(_read(body), body, lambdaOf: null);
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
                            into.Add(new LoopStep(instruction.Offset, flow.LoopOver(i, loopEnd), body) { Condition = Relative(entry, outer), While = flow.While(i, loopEnd) });
                    }
                    i = loopEnd;
                    continue;
                }
                if (flow.IsNoise(i) || instruction.IsBranch) continue;
                // Code no way reaches (a handler, a resume point): nothing of it runs in a play.
                if (flow.ReachedAt(i) is not { IsNever: false } reached) continue;
                var condition = Relative(reached, outer);
                if (!instruction.IsCall || instruction.Member is not { } member) continue;
                if (member.Namespace == "MegaCrit.Sts2.Core.Commands" && member.DeclaringType == "CardSelectCmd")
                {
                    into.Add(new ChoiceStep(instruction.Offset, member) { Condition = condition, Arguments = Arguments(flow, i, member) });
                    continue;
                }
                if (member.Name == "CreateCard" && member.GenericArguments.Count == 1)
                {
                    into.Add(new CreateStep(instruction.Offset, member.GenericArguments[0]) { Condition = condition });
                    continue;
                }
                if (member.DeclaringType == "DamageCmd" && member.Name == "Attack")
                {
                    into.Add(Attack(flow, i, member) with { Condition = condition });
                    continue;
                }
                if (IsCommand(member))
                {
                    into.Add(new CommandStep(instruction.Offset, member, Arguments(flow, i, member)) { Condition = condition, Inner = callback ?? (IReadOnlyList<ProgramStep>)Array.Empty<ProgramStep>() });
                    callback = null;
                    continue;
                }
                // The card's own methods and the local functions its OnPlay declares: their steps run here.
                if (OwnMethod(member) is { } own)
                {
                    if (depth >= MaxDepth) into.Add(new UnknownStep(instruction.Offset, $"{member.Name}: calls nested too deep") { Condition = condition });
                    else
                    {
                        var inner = new Reader(_card, _read).Steps(own, depth + 1);
                        into.AddRange(condition == null ? inner : inner.Select(step => step with { Condition = And(condition, step.Condition) }));
                    }
                    continue;
                }
                // A static helper of the game's models that runs commands (Shiv.CreateInHand: Blade Symphony's Shivs): its steps run here.
                if (depth < MaxDepth && GameHelper(member) is { } helper)
                {
                    var inner = new Reader(helper.Owner, _read).Steps(helper.Body, depth + 1);
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

        private static ProgramCondition? And(ProgramCondition? outer, ProgramCondition? inner) => outer == null ? inner : inner == null ? outer : outer.And(inner);

        /// <summary>A step's condition inside the loop it's in (null when it holds on every way).</summary>
        private static ProgramCondition? Relative(ProgramCondition reached, ProgramCondition? outer) => reached.Without(outer) is { IsAlways: false } relative ? relative : null;

        /// <summary>Steps that do something: a command or a choice (a helper or delegate with neither only asks or shows).</summary>
        private static bool Acts(List<ProgramStep> steps) => new CardProgram("", steps).All().Any(step => step is CommandStep or ChoiceStep or UnknownStep);

        /// <summary>The attack DamageCmd.Attack starts: its builder calls up to Execute.</summary>
        private static CommandStep Attack(Flow flow, int at, CodeMember member)
        {
            var builder = new List<string>();
            string? hitCount = null;
            for (var j = at + 1; j < flow.Ops.Count; j++)
            {
                if (flow.Ops[j].Member is not { DeclaringType: "AttackCommand" } call || !flow.Ops[j].IsCall) continue;
                if (call.Name == "Execute") break;
                builder.Add(call.Name);
                if (call.Name == "WithHitCount" && call.ParameterCount >= 1) hitCount = flow.Source(j, call, parameter: 0);
            }
            return new CommandStep(flow.Ops[at].Offset, member, Arguments(flow, at, member)) { Builder = builder, HitCount = hitCount };
        }

        /// <summary>The card's own method (its type or a base type's private helper) or a local function OnPlay declares.</summary>
        private MethodBase? OwnMethod(CodeMember member)
        {
            if (member.Kind != CodeMemberKind.Method || member.HasThis == false && !member.Name.StartsWith('<')) return null;
            if (member.DeclaringType != _card.Name && !member.DeclaringType.StartsWith("<>c", StringComparison.Ordinal)) return null;
            if (!member.Name.StartsWith('<') && member.Name is "OnPlay" or "get_DynamicVars" or "get_Owner" or "get_CombatState") return null;
            if (member.Name.StartsWith("get_", StringComparison.Ordinal) || member.Name.StartsWith("set_", StringComparison.Ordinal)) return null;
            foreach (var type in OwnTypes())
            {
                if (type.Name != member.DeclaringType) continue;
                var found = type.GetMethods(All).FirstOrDefault(method => method.Name == member.Name && method.GetParameters().Length == member.ParameterCount);
                if (found == null) continue;
                // An async helper's code is its state machine's (nested in its type, or in the card for a local function).
                return BodyOf(found);
            }
            return null;
        }

        /// <summary>A static method of another of the game's models (not a command, not a property): the type it's on and its code.</summary>
        private (Type Owner, MethodBase Body)? GameHelper(CodeMember member)
        {
            if (member.Kind != CodeMemberKind.Method || member.HasThis || IsCommand(member) || member.Name.StartsWith('<') || member.Name.StartsWith("op_", StringComparison.Ordinal)) return null;
            if (!member.Namespace.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal) || member.Name.StartsWith("get_", StringComparison.Ordinal)) return null;
            if (_card.Assembly.GetType(member.Namespace + "." + member.DeclaringType) is not { } owner) return null;
            if (owner.GetMethods(All).FirstOrDefault(method => method.Name == member.Name && method.GetParameters().Length == member.ParameterCount) is not { } found) return null;
            return (owner, BodyOf(found));
        }

        private MethodBase? Lambda(CodeMember member)
        {
            foreach (var type in OwnTypes())
            {
                if (type.Name != member.DeclaringType) continue;
                if (type.GetMethods(All).FirstOrDefault(method => method.Name == member.Name) is not { } found) continue;
                return BodyOf(found);
            }
            return null;
        }

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

        private Flow(List<CodeOp> ops, System.Reflection.MethodBase? method, Func<CodeMember, LambdaValue?>? lambdaOf)
        {
            Ops = ops;
            for (var i = 0; i < ops.Count; i++) _indexOf[ops[i].Offset] = i;
            _noise = new bool[ops.Count];
            _method = method;
            _site = method == null ? "" : method.DeclaringType?.Name + "." + method.Name;
            _lambdaOf = lambdaOf;
        }

        public int IndexOf(int offset) => _indexOf.TryGetValue(offset, out var index) ? index : Ops.Count;
        public bool IsNoise(int index) => _noise[index];
        public int? LoopEndAt(int index) => _loopEnds.TryGetValue(index, out var end) ? end : null;
        public ProgramCondition? ReachedAt(int index) => index < _reached.Length ? _reached[index] : null;
        public ProgramCondition? LoopEntry(int start) => _loopEntries.TryGetValue(start, out var entry) ? entry : null;

        public static Flow Of(List<CodeOp> ops, System.Reflection.MethodBase? method = null, Func<CodeMember, LambdaValue?>? lambdaOf = null)
        {
            var flow = new Flow(ops, method, lambdaOf);
            flow.MarkMachinery();
            flow.FindLoops();
            flow.FindConditions();
            return flow;
        }

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
            return new ConditionLiteral(at, new CompareValue(comparison, left, right.Value), Holds: true);
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

        /// <summary>The value on top of the stack just before <paramref name="index"/>, read back as an expression.</summary>
        public ProgramValue ValueBefore(int index) => ReadBack(index, depth: 0).Value;

        /// <summary>
        /// The value the code just before <paramref name="end"/> leaves, and where its code starts (-1 when it can't be read): constants,
        /// the card's values, properties and methods of what they're asked of, comparisons, arithmetic, a lambda, a local or a field
        /// stored once (what was stored), and what an awaited command gave back.
        /// </summary>
        private (ProgramValue Value, int Start) ReadBack(int end, int depth)
        {
            var at = end - 1;
            if (at < 0 || depth > 16) return (new UnknownValue("value"), -1);
            var instruction = Ops[at];
            var name = instruction.OpCode.Name!;
            if (instruction.Integer is { } constant && name.StartsWith("ldc", StringComparison.Ordinal)) return (new ConstantValue(constant), at);
            if (instruction.OpCode == OpCodes.Ldnull) return (NullValue.Instance, at);
            if (instruction.OpCode == OpCodes.Ldstr) return (new NamedValue("\"" + instruction.Text + "\""), at);
            if (name.StartsWith("ldarg", StringComparison.Ordinal)) return (Argument(instruction.Slot ?? 0), at);
            if (name.StartsWith("ldloc", StringComparison.Ordinal))
            {
                if (instruction.Slot is { } slot && StoredOnce(slot) is { } store && ReadBack(store, depth + 1) is { Start: >= 0 } stored)
                    return (new StoredValue("", stored.Value, Ops[store].Offset), at);
                return (new UnknownValue("local"), at);
            }
            if (name.StartsWith("conv", StringComparison.Ordinal) || instruction.OpCode == OpCodes.Box || instruction.OpCode == OpCodes.Unbox_Any
                || instruction.OpCode == OpCodes.Castclass)
                return ReadBack(at, depth + 1);
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
                    return (new NamedValue(FieldName(field.Name)), receiver.Start);
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
                return (new CompareValue(comparison, left.Value, right.Value), left.Start);
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
                return (LambdaBetween(at - 1, at), target.Start);
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
            if (member.Name is "get_BaseValue" or "get_IntValue")
            {
                if (of is QueryValue { Of: QueryValue { Name: "DynamicVars" } } value) return (new CardValue(value.Name), cursor);
                if (of is QueryValue { Name: "Item", Arguments: [NamedValue key] }) return (new CardValue(key.Name.Trim('"')), cursor);
            }
            // A foreach's item (enumerator.Current): each item it goes over.
            if (member.Name == "get_Current") return (new NamedValue("each"), cursor);
            var property = member.Name.StartsWith("get_", StringComparison.Ordinal);
            return (new QueryValue(member.DeclaringType, property ? member.Name[4..] : member.Name, of, arguments, member.ValueType)
                { IsProperty = property && arguments.Length == 0, GenericArguments = member.GenericArguments }, cursor);
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