using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Sts2Mods.Core.Game;

/// <summary>
/// A method that names game parts only Steam's beta branch has (decision #163: the mod is written for beta and accommodates main).
/// Its code is the plain beta code, checked by the compiler; it's only ever called through <see cref="BetaOnly.Gate{T}"/>, which
/// gives null where the game lacks what it names, so main never compiles it (a missing type or member breaks the whole method
/// that names it). The caller gives main's fallback. Keep it small: its body is the only place those parts may be named, and no
/// lambda inside it (a lambda is a method of its own). GameBranchTests holds every rule: beta-only parts are named only in such
/// methods, each such method is called only through a gate, and one whose parts main has gained fails until it's made plain code.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class BetaOnlyAttribute : Attribute { }

public static class BetaOnly
{
    private static readonly ConcurrentDictionary<MethodBase, bool> Known = new();

    /// <summary>
    /// The method (a <see cref="BetaOnlyAttribute"/> one) when the running game has everything it names, else null: main's
    /// fallback goes at the call (<c>Gate?.Invoke(...) ?? fallback</c>).
    /// </summary>
    public static T? Gate<T>(T method) where T : Delegate => Runs(method.Method) ? method : null;

    /// <summary>Every type and member the method's code names (its locals, calls, fields, types) resolves in the running game.</summary>
    public static bool Runs(MethodBase method) => Known.GetOrAdd(method, Resolves);

    private static bool Resolves(MethodBase method)
    {
        try
        {
            if (method.GetCustomAttribute<BetaOnlyAttribute>() == null) return false; // only gated methods are called this way
            var body = method.GetMethodBody();
            if (body == null) return false;
            foreach (var local in body.LocalVariables) _ = local.LocalType.TypeHandle;
            var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            foreach (var (_, token) in Tokens(body.GetILAsByteArray() ?? Array.Empty<byte>()))
            {
                var member = method.Module.ResolveMember(token, typeArguments, methodArguments);
                // A method's parameter and return types must load too (a call would load them).
                if (member is MethodBase called)
                {
                    foreach (var parameter in called.GetParameters()) _ = parameter.ParameterType.TypeHandle;
                    if (called is MethodInfo info) _ = info.ReturnType.TypeHandle;
                }
                else if (member is FieldInfo field) _ = field.FieldType.TypeHandle;
                else if (member is Type type) _ = type.TypeHandle;
            }
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            return true;
        }
        catch
        {
            return false; // a part this branch doesn't have
        }
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(opCode => opCode.Value);

    /// <summary>The metadata tokens in a method body's IL (types, methods, fields; not strings or signatures), each with its opcode.</summary>
    internal static IEnumerable<(OpCode OpCode, int Token)> Tokens(byte[] code)
    {
        var position = 0;
        while (position < code.Length)
        {
            short value = code[position++];
            if (value == 0xFE && position < code.Length) value = (short)(0xFE00 | code[position++]);
            if (!OpCodesByValue.TryGetValue(value, out var opCode)) yield break;
            switch (opCode.OperandType)
            {
                case OperandType.InlineMethod:
                case OperandType.InlineField:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                    yield return (opCode, BitConverter.ToInt32(code, position));
                    position += 4;
                    break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(code, position);
                    position += 4 + 4 * count;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    position += 8;
                    break;
                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.ShortInlineR:
                    position += 4;
                    break;
                case OperandType.InlineVar:
                    position += 2;
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    position += 1;
                    break;
            }
        }
    }
}
