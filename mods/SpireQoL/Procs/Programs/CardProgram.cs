using System.Reflection;
using System.Reflection.Emit;

namespace SpireQoL.Procs.Programs;

/// <summary>One step of a play, in the order the card's code runs it (decision #182, docs/CARD_PROGRAMS.md).</summary>
public abstract record ProgramStep(int Offset)
{
    /// <summary>Runs only when a condition holds (an if around it).</summary>
    public string? Condition { get; init; }
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
    /// The steps of a delegate the command is given and runs (ArrowCmd.ShootArrowAtTarget runs Blossom Shot's attack after the
    /// shot): they follow the command.
    /// </summary>
    public IReadOnlyList<ProgramStep> Inner { get; init; } = Array.Empty<ProgramStep>();

    /// <summary>"CreatureCmd.GainBlock", "PowerCmd.Apply&lt;VulnerablePower&gt;".</summary>
    public string Name => Command.ToString();

    public override string ToString() => Name + (Inner.Count > 0 ? " [" + string.Join(", ", Inner) + "]" : "") + (Condition != null ? " if " + Condition : "");
}

/// <summary>A player's pick in the middle of the play (CardSelectCmd): which card is theirs to choose, not whether the play goes on.</summary>
public sealed record ChoiceStep(int Offset, CodeMember Command) : ProgramStep(Offset)
{
    public override string ToString() => "choice " + Command.Name;
}

/// <summary>A loop: its body runs once per round (a foreach over cards, a for up to a value, a do-while).</summary>
public sealed record LoopStep(int Offset, string Over, IReadOnlyList<ProgramStep> Body) : ProgramStep(Offset)
{
    public override string ToString() => $"loop over {Over} [{string.Join(", ", Body)}]";
}

/// <summary>Something the reader couldn't follow: the steps it holds or stands for aren't known.</summary>
public sealed record UnknownStep(int Offset, string Reason) : ProgramStep(Offset)
{
    public override string ToString() => "unknown: " + Reason;
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
    internal static MethodBase? Body(Type owner, string method)
    {
        var machine = owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(type => type.Name.StartsWith("<" + method + ">d", StringComparison.Ordinal));
        if (machine?.GetMethod("MoveNext", All) is { } moveNext) return moveNext;
        return owner.GetMethods(All).FirstOrDefault(candidate => candidate.Name == method);
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
            var flow = Flow.Of(ops);
            var steps = new List<ProgramStep>();
            Parse(flow, 0, flow.Ops.Count, steps, condition: null, depth);
            return steps;
        }

        /// <summary>The steps of ops[start, end): commands in order, loops as bodies, an if's steps marked with its condition.</summary>
        private void Parse(Flow flow, int start, int end, List<ProgramStep> into, string? condition, int depth)
        {
            var ops = flow.Ops;
            List<ProgramStep>? callback = null;
            for (var i = start; i < end; i++)
            {
                var instruction = ops[i];
                // A loop that starts here (a backward jump to this instruction later on): its body up to that jump.
                if (flow.LoopEndAt(i) is { } loopEnd && loopEnd < end)
                {
                    var body = new List<ProgramStep>();
                    Parse(flow, i, loopEnd, body, condition: null, depth);
                    if (body.Count > 0) into.Add(new LoopStep(instruction.Offset, flow.LoopOver(i, loopEnd), body) { Condition = condition });
                    i = loopEnd;
                    continue;
                }
                if (flow.IsNoise(i)) continue;
                if (instruction.IsBranch)
                {
                    var target = flow.IndexOf(instruction.Targets[0]);
                    // The jump into a loop's condition (for, while, foreach): the loop starts right after it.
                    if (instruction.OpCode.FlowControl == FlowControl.Branch && target > i && flow.LoopEndAt(i + 1) is { } entered && entered >= target) continue;
                    if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch && target > i + 1 && target <= end)
                    {
                        // if (...) { then } [else { ... }]: what runs only when the condition holds, marked with it.
                        var query = flow.Query(i);
                        var thenEnd = target;
                        var elseEnd = target;
                        if (target - 1 > i && ops[target - 1] is { OpCode.FlowControl: FlowControl.Branch } skip && flow.IndexOf(skip.Targets[0]) is var after && after > target && after <= end)
                        {
                            thenEnd = target - 1;
                            elseEnd = after;
                        }
                        Parse(flow, i + 1, thenEnd, into, Join(condition, query), depth);
                        if (elseEnd > target) Parse(flow, target, elseEnd, into, Join(condition, "not " + query), depth);
                        i = elseEnd - 1;
                    }
                    continue;
                }
                if (!instruction.IsCall || instruction.Member is not { } member) continue;
                if (member.Namespace == "MegaCrit.Sts2.Core.Commands" && member.DeclaringType == "CardSelectCmd")
                {
                    into.Add(new ChoiceStep(instruction.Offset, member) { Condition = condition });
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
                        into.AddRange(condition == null ? inner : inner.Select(step => step with { Condition = Join(condition, step.Condition) }));
                    }
                    continue;
                }
                // A delegate whose code calls commands: the next command it's given to runs it (its Inner steps). One left over, or
                // given to something else (an attack builder's BeforeDamage), runs when something else decides: not followed.
                if (instruction.OpCode == OpCodes.Newobj && i > 0 && ops[i - 1].OpCode == OpCodes.Ldftn && ops[i - 1].Member is { } lambda && Lambda(lambda) is { } lambdaBody
                    && depth < MaxDepth)
                {
                    var inner = new Reader(_card, _read).Steps(lambdaBody, depth + 1);
                    if (inner.Count > 0)
                    {
                        if (callback != null) into.Add(new UnknownStep(instruction.Offset, "commands in a callback") { Condition = condition });
                        callback = inner;
                    }
                }
            }
            if (callback != null) into.Add(new UnknownStep(ops.Count > 0 ? ops[^1].Offset : 0, "commands in a callback") { Condition = condition });
        }

        private static string Join(string? outer, string? inner) => outer == null ? inner ?? "" : inner == null ? outer : outer + " and " + inner;

        /// <summary>The attack DamageCmd.Attack starts: its builder calls up to Execute.</summary>
        private static CommandStep Attack(Flow flow, int at, CodeMember member)
        {
            var builder = new List<string>();
            for (var j = at + 1; j < flow.Ops.Count; j++)
            {
                if (flow.Ops[j].Member is not { DeclaringType: "AttackCommand" } call || !flow.Ops[j].IsCall) continue;
                if (call.Name == "Execute") break;
                builder.Add(call.Name);
            }
            return new CommandStep(flow.Ops[at].Offset, member, Arguments(flow, at, member)) { Builder = builder };
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
                return OwnTypes().Select(owner => Body(owner, member.Name)).FirstOrDefault(body => body != null && body.DeclaringType!.Name.StartsWith("<" + member.Name + ">d", StringComparison.Ordinal)) ?? found;
            }
            return null;
        }

        private MethodBase? Lambda(CodeMember member)
        {
            foreach (var type in OwnTypes())
            {
                if (type.Name != member.DeclaringType) continue;
                if (type.GetMethods(All).FirstOrDefault(method => method.Name == member.Name) is not { } found) continue;
                return OwnTypes().Select(owner => Body(owner, member.Name)).FirstOrDefault(body => body != null && body.DeclaringType!.Name.StartsWith("<" + member.Name + ">d", StringComparison.Ordinal)) ?? found;
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

        private Flow(List<CodeOp> ops)
        {
            Ops = ops;
            for (var i = 0; i < ops.Count; i++) _indexOf[ops[i].Offset] = i;
            _noise = new bool[ops.Count];
        }

        public int IndexOf(int offset) => _indexOf.TryGetValue(offset, out var index) ? index : Ops.Count;
        public bool IsNoise(int index) => _noise[index];
        public int? LoopEndAt(int index) => _loopEnds.TryGetValue(index, out var end) ? end : null;

        public static Flow Of(List<CodeOp> ops)
        {
            var flow = new Flow(ops);
            flow.MarkMachinery();
            flow.FindLoops();
            return flow;
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
                if (stateSlot != null && LoadsState(i, stateSlot.Value)) _noise[i] = true;
                // An await: awaiter.IsCompleted, else suspend (and later resume) before GetResult. Everything up to the jump's target
                // is the machinery.
                if (i > 0 && Ops[i - 1].Member is { Name: "get_IsCompleted" } && instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
                {
                    var target = IndexOf(instruction.Targets[0]);
                    for (var j = i; j < target && j < Ops.Count; j++) _noise[j] = true;
                }
            }
        }

        /// <summary>The branch at <paramref name="index"/> tests the state local (ldloc state [; ldc; ...]).</summary>
        private bool LoadsState(int index, int stateSlot)
        {
            for (var back = 1; back <= 3 && index - back >= 0; back++)
            {
                var previous = Ops[index - back];
                if (previous.OpCode.Name!.StartsWith("ldloc", StringComparison.Ordinal) && previous.Slot == stateSlot) return true;
                if (previous.Integer == null && !(previous.OpCode.Name!.StartsWith("ldloc", StringComparison.Ordinal))) return false;
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
                if (member is { Name: "MoveNext" }) return "each item";
                if (member is { Name: "get_IntValue" or "get_BaseValue" } && i > 0 && Ops[i - 1].Member is { } value) return value.Name.Replace("get_", "");
                if (member is { Name: "get_Item" } && Ops[i - 1].Text is { } key) return key;
            }
            return "?";
        }

        /// <summary>The condition a branch tests, named by the last thing it asks the game (a property, a method, a delegate's).</summary>
        public string Query(int branch)
        {
            for (var i = branch - 1; i >= 0 && i >= branch - 12; i--)
            {
                var instruction = Ops[i];
                if (instruction.Member is { Kind: CodeMemberKind.Method } member && member.Name != "op_Implicit" && member.Name != nameof(Action.Invoke))
                    return member.Name.Replace("get_", "");
                if (instruction.Member is { Kind: CodeMemberKind.Field } field && !field.Name.StartsWith("<>", StringComparison.Ordinal)) return field.Name;
            }
            return "?";
        }

        /// <summary>
        /// Where argument <paramref name="parameter"/> of the call at <paramref name="callAt"/> comes from: the value it reads
        /// ("Block", "Damage", a DynamicVars key), a constant, a field, or "?" when the way back isn't straight.
        /// </summary>
        public string Source(int callAt, CodeMember called, int parameter) =>
            CodeDecoder.Producer(Ops, callAt, CodeDecoder.ArgumentDepth(called, parameter), stop: index => _noise[index]) is { } producer ? Describe(producer) : "?";

        private string Describe(int index)
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
                return member.Name.Replace("get_", "");
            }
            if (instruction.Text is { } text) return "\"" + text + "\"";
            return instruction.OpCode.Name ?? "?";
        }
    }
}
