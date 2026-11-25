using System.Reflection;
using System.Reflection.Emit;

namespace SpireQoL.Procs.Programs;

/// <summary>What kind of member an instruction names.</summary>
public enum CodeMemberKind { Method, Field, Type }

/// <summary>
/// A member an instruction names, by name only, so beta's code (loaded in the game) and main's (read from its assembly file in the
/// tests) read the same way (decision #182). Type names are simple (no namespace); a nested type keeps its own name.
/// </summary>
public sealed record CodeMember(CodeMemberKind Kind, string DeclaringType, string Name)
{
    /// <summary>A generic method's type arguments (PowerCmd.Apply&lt;VulnerablePower&gt;: "VulnerablePower").</summary>
    public IReadOnlyList<string> GenericArguments { get; init; } = Array.Empty<string>();
    /// <summary>The declaring type's generic arguments (new PowerVar&lt;AimPower&gt;(12): "AimPower"); empty for a type that isn't generic.</summary>
    public IReadOnlyList<string> TypeArguments { get; init; } = Array.Empty<string>();
    /// <summary>A method's parameters (without its instance).</summary>
    public int ParameterCount { get; init; }
    /// <summary>A method's parameters' types, in order (simple names: "Decimal", "BlockVar").</summary>
    public IReadOnlyList<string> ParameterTypes { get; init; } = Array.Empty<string>();
    /// <summary>A method's parameters' names, in order; empty when they can't be read (a reference in an assembly file names none).</summary>
    public IReadOnlyList<string> ParameterNames { get; init; } = Array.Empty<string>();
    /// <summary>A method that takes an instance (not static), or a constructor.</summary>
    public bool HasThis { get; init; }
    /// <summary>A method that leaves a value (not void).</summary>
    public bool ReturnsValue { get; init; }
    /// <summary>A field's type, or a method's return type (simple names).</summary>
    public string ValueType { get; init; } = "";
    /// <summary>The declaring type's namespace.</summary>
    public string Namespace { get; init; } = "";

    /// <summary>"CreatureCmd.GainBlock".</summary>
    public string FullName => DeclaringType + "." + Name;

    public override string ToString() => FullName + (GenericArguments.Count > 0 ? "<" + string.Join(",", GenericArguments) + ">" : "");
}

/// <summary>One instruction of a method body: its offset, opcode and operand (a member, a branch's targets, a constant or a text).</summary>
public sealed record CodeOp(int Offset, OpCode OpCode)
{
    public CodeMember? Member { get; init; }
    /// <summary>A branch's target offsets (a switch has several).</summary>
    public IReadOnlyList<int> Targets { get; init; } = Array.Empty<int>();
    public long? Integer { get; init; }
    public string? Text { get; init; }
    /// <summary>The local or argument index a load or store names.</summary>
    public int? Slot { get; init; }

    public bool IsCall => OpCode == OpCodes.Call || OpCode == OpCodes.Callvirt || OpCode == OpCodes.Newobj;
    public bool IsBranch => OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch;

    public override string ToString() => $"{Offset:X4} {OpCode.Name}" + (Member != null ? " " + Member : "") + (Targets.Count > 0 ? " -> " + string.Join(",", Targets.Select(target => target.ToString("X4"))) : "")
        + (Integer != null ? " " + Integer : "") + (Text != null ? " \"" + Text + "\"" : "") + (Slot != null ? " #" + Slot : "");
}

/// <summary>
/// A method body's instructions from its IL bytes, its members named by the member reader and its strings by
/// the string reader: the game's loaded module in the game, a metadata reader over an assembly file in the tests.
/// </summary>
public static class CodeDecoder
{
    private static readonly Dictionary<short, OpCode> ByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(opCode => opCode.Value);

    public static List<CodeOp> Decode(byte[] code, Func<int, CodeMember?> member, Func<int, string?> text)
    {
        var ops = new List<CodeOp>();
        var position = 0;
        while (position < code.Length)
        {
            var offset = position;
            short value = code[position++];
            if (value == 0xFE && position < code.Length) value = unchecked((short)(0xFE00 | code[position++]));
            if (!ByValue.TryGetValue(value, out var opCode)) break;
            var instruction = new CodeOp(offset, opCode);
            switch (opCode.OperandType)
            {
                case OperandType.InlineMethod:
                case OperandType.InlineField:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                    instruction = instruction with { Member = member(BitConverter.ToInt32(code, position)) };
                    position += 4;
                    break;
                case OperandType.InlineString:
                    instruction = instruction with { Text = text(BitConverter.ToInt32(code, position)) };
                    position += 4;
                    break;
                case OperandType.InlineSwitch:
                {
                    var count = BitConverter.ToInt32(code, position);
                    position += 4;
                    var end = position + 4 * count;
                    var targets = new int[count];
                    for (var i = 0; i < count; i++) targets[i] = end + BitConverter.ToInt32(code, position + 4 * i);
                    position = end;
                    instruction = instruction with { Targets = targets };
                    break;
                }
                case OperandType.InlineBrTarget:
                    instruction = instruction with { Targets = new[] { position + 4 + BitConverter.ToInt32(code, position) } };
                    position += 4;
                    break;
                case OperandType.ShortInlineBrTarget:
                    instruction = instruction with { Targets = new[] { position + 1 + (sbyte)code[position] } };
                    position += 1;
                    break;
                case OperandType.InlineI:
                    instruction = instruction with { Integer = BitConverter.ToInt32(code, position) };
                    position += 4;
                    break;
                case OperandType.ShortInlineI:
                    instruction = instruction with { Integer = opCode == OpCodes.Ldc_I4_S ? (sbyte)code[position] : code[position] };
                    position += 1;
                    break;
                case OperandType.InlineI8:
                    instruction = instruction with { Integer = BitConverter.ToInt64(code, position) };
                    position += 8;
                    break;
                case OperandType.InlineR:
                {
                    // A double constant that's a whole number (Shadowmeld's Math.Pow(2.0, ...)) is that number; any other isn't read.
                    var real = BitConverter.ToDouble(code, position);
                    if (Math.Abs(real) < 1e15 && Math.Floor(real) == real) instruction = instruction with { Integer = (long)real };
                    position += 8;
                    break;
                }
                case OperandType.ShortInlineR:
                case OperandType.InlineSig:
                    position += 4;
                    break;
                case OperandType.InlineVar:
                    instruction = instruction with { Slot = BitConverter.ToUInt16(code, position) };
                    position += 2;
                    break;
                case OperandType.ShortInlineVar:
                    instruction = instruction with { Slot = code[position] };
                    position += 1;
                    break;
            }
            ops.Add(ShortForms(instruction));
        }
        return ops;
    }

    /// <summary>The constant or slot a short form names in its opcode (ldc.i4.2, ldloc.1, stloc.3, ldarg.0).</summary>
    private static CodeOp ShortForms(CodeOp instruction)
    {
        var name = instruction.OpCode.Name ?? "";
        if (instruction.Integer == null && name.StartsWith("ldc.i4.", StringComparison.Ordinal) && name != "ldc.i4.s")
            return instruction with { Integer = name == "ldc.i4.m1" ? -1 : name[^1] - '0' };
        if (instruction.Slot == null && name.Length > 2 && name[^2] == '.' && char.IsDigit(name[^1])
            && (name.StartsWith("ldloc", StringComparison.Ordinal) || name.StartsWith("stloc", StringComparison.Ordinal) || name.StartsWith("ldarg", StringComparison.Ordinal)))
            return instruction with { Slot = name[^1] - '0' };
        return instruction;
    }

    /// <summary>How many values an instruction takes off the stack and leaves on it; null when that can't be told.</summary>
    public static (int Pop, int Push)? StackEffect(CodeOp instruction)
    {
        var opCode = instruction.OpCode;
        int push, pop;
        switch (opCode.StackBehaviourPush)
        {
            case StackBehaviour.Push0: push = 0; break;
            case StackBehaviour.Push1_push1: push = 2; break;
            case StackBehaviour.Varpush:
                if (instruction.Member == null) return null;
                push = instruction.Member.ReturnsValue ? 1 : 0;
                break;
            default: push = 1; break;
        }
        switch (opCode.StackBehaviourPop)
        {
            case StackBehaviour.Pop0: pop = 0; break;
            case StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref: pop = 1; break;
            case StackBehaviour.Varpop:
                if (instruction.Member == null) return null;
                if (opCode == OpCodes.Ret) return null;
                pop = instruction.Member.ParameterCount + (instruction.Member.HasThis && opCode != OpCodes.Newobj ? 1 : 0);
                break;
            case StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4
                or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref:
                pop = 3; break;
            default: pop = 2; break;
        }
        if (opCode == OpCodes.Newobj) push = 1;
        return (pop, push);
    }

    /// <summary>
    /// The instruction that leaves the value <paramref name="depth"/> deep on the stack (0: the top) just before instruction
    /// <paramref name="at"/>, walking back through straight code: null when the way back isn't straight (a jump, an instruction
    /// <paramref name="stop"/> names, one whose stack effect can't be told or that leaves two values), or when a jump in
    /// <paramref name="landings"/> lands on the way (the value could come from either path).
    /// </summary>
    public static int? Producer(IReadOnlyList<CodeOp> ops, int at, int depth, Func<int, bool>? stop = null, IReadOnlySet<int>? landings = null)
    {
        var wanted = depth + 1;
        for (var i = at - 1; i >= 0; i--)
        {
            var instruction = ops[i];
            if (stop?.Invoke(i) == true || instruction.IsBranch) return null;
            if (landings != null && landings.Contains(ops[i + 1].Offset)) return null;
            if (StackEffect(instruction) is not var (pop, push)) return null;
            if (wanted <= push) return push == 1 ? i : null;
            wanted += pop - push;
        }
        return null;
    }

    /// <summary>The offsets jumps land on (branches' and switches' targets).</summary>
    public static IReadOnlySet<int> Landings(IEnumerable<CodeOp> ops) => ops.SelectMany(instruction => instruction.Targets).ToHashSet();

    /// <summary>How deep (0: the top) argument <paramref name="parameter"/> of a call is on the stack just before the call.</summary>
    public static int ArgumentDepth(CodeMember called, int parameter) => called.ParameterCount - parameter - 1;
}

/// <summary>A method of the loaded game read into instructions (cached: the code doesn't change while the game runs).</summary>
public static class RuntimeCode
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<MethodBase, List<CodeOp>> Cache = new();

    public static List<CodeOp> Read(MethodBase method) => Cache.GetOrAdd(method, ReadNow);

    /// <summary>
    /// How many methods this thread has read (not found in the cache): what the tests hold a fight's first board read to (none once the load
    /// has read the code it asks: decision #254).
    /// </summary>
    public static int ReadsOnThisThread => _readsOnThisThread;

    [ThreadStatic] private static int _readsOnThisThread;

    private static List<CodeOp> ReadNow(MethodBase method)
    {
        _readsOnThisThread++;
        var code = method.GetMethodBody()?.GetILAsByteArray();
        if (code == null) return new List<CodeOp>();
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        return CodeDecoder.Decode(code, token =>
        {
            try { return Of(method.Module.ResolveMember(token, typeArguments, methodArguments)); }
            catch { return null; }
        }, token =>
        {
            try { return method.Module.ResolveString(token); }
            catch { return null; }
        });
    }

    /// <summary>A reflected member, by name.</summary>
    public static CodeMember? Of(MemberInfo? member) => member switch
    {
        MethodBase method => new CodeMember(CodeMemberKind.Method, method.DeclaringType?.Name ?? "", method.Name)
        {
            GenericArguments = method.IsGenericMethod ? method.GetGenericArguments().Select(type => type.Name).ToArray() : Array.Empty<string>(),
            TypeArguments = method.DeclaringType is { IsGenericType: true } generic ? generic.GetGenericArguments().Select(type => type.Name).ToArray() : Array.Empty<string>(),
            ParameterCount = method.GetParameters().Length,
            ParameterTypes = method.GetParameters().Select(parameter => parameter.ParameterType.Name).ToArray(),
            ParameterNames = method.GetParameters().Select(parameter => parameter.Name ?? "").ToArray(),
            HasThis = !method.IsStatic,
            ReturnsValue = method is MethodInfo info && info.ReturnType != typeof(void),
            ValueType = method is MethodInfo returning ? returning.ReturnType.Name : "",
            Namespace = method.DeclaringType?.Namespace ?? "",
        },
        FieldInfo field => new CodeMember(CodeMemberKind.Field, field.DeclaringType?.Name ?? "", field.Name)
            { ValueType = field.FieldType.Name, HasThis = !field.IsStatic, Namespace = field.DeclaringType?.Namespace ?? "" },
        Type type => new CodeMember(CodeMemberKind.Type, type.DeclaringType?.Name ?? "", type.Name) { Namespace = type.Namespace ?? "" },
        _ => null,
    };
}
