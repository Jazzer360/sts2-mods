using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace SpireQoL.Sim;

/// <summary>
/// The powers an enemy's move applies (decision #72: what a player who knows the game knows about a move), read from the move's
/// own code: its perform method (MoveState._onPerform; an async method, so its state machine's MoveNext) calls
/// PowerCmd.Apply&lt;T&gt; for each power. The amount is the value passed in when it's a constant or a number the monster keeps
/// (a property like CorpseSlug.GoopFrailAmt, read now: ascension included); otherwise unknown (null). Not the target: a debuff
/// intent's power lands on you, a buff's on the enemy side, as the intent already says.
/// </summary>
internal static class MovePowers
{
    public sealed record Applied(Type Power, decimal? Amount);

    /// <summary>Per perform method: each Apply call's power, and how its amount is read (a constant or a getter), or nothing.</summary>
    private sealed record Call(Type Power, decimal? Constant, MethodInfo? Getter);

    private static readonly Dictionary<MethodInfo, List<Call>> Calls = new();

    /// <summary>The powers a move applies, in the order its code applies them (empty when none or unreadable).</summary>
    public static List<Applied> Of(MonsterModel monster, MoveState move)
    {
        var result = new List<Applied>();
        try
        {
            if (Sts2Mods.Core.Game.Sts2State.Private<Delegate?>(move, "_onPerform", null) is not { } perform) return result;
            List<Call> calls;
            lock (Calls)
            {
                if (!Calls.TryGetValue(perform.Method, out calls!)) Calls[perform.Method] = calls = Read(perform.Method);
            }
            foreach (var c in calls)
            {
                decimal? amount = c.Constant;
                if (c.Getter != null && c.Getter.DeclaringType!.IsInstanceOfType(monster))
                {
                    try { amount = Convert.ToDecimal(c.Getter.Invoke(monster, null)); } catch { amount = null; }
                }
                result.Add(new Applied(c.Power, amount));
            }
        }
        catch
        {
            // unreadable: no powers shown
        }
        return result;
    }

    private static List<Call> Read(MethodInfo method)
    {
        var body = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? (MethodBase)method;
        var code = Diagnostics.GameCode.Instructions(body);
        var calls = new List<Call>();
        Type? made = null; // a power made first (ModelDb.Power<T>().ToMutable(): Sandpit, Dampen), applied by the plain Apply
        for (var i = 0; i < code.Count; i++)
        {
            if (code[i].operand is not MethodInfo m) continue;
            if (m is { IsGenericMethod: true, Name: "Power" } && m.DeclaringType?.Name == "ModelDb")
            {
                made = m.GetGenericArguments()[0];
                continue;
            }
            if (m.Name != "Apply" || m.DeclaringType?.Name != "PowerCmd") continue;
            var power = m.IsGenericMethod ? m.GetGenericArguments()[0] : made;
            if (power == null) continue;
            calls.Add(AmountBefore(code, i) is var (constant, getter) ? new Call(power, constant, getter) : new Call(power, null, null));
        }
        // Made but applied another way (Dampen: added to the target directly): the icon alone.
        if (calls.Count == 0 && made != null) calls.Add(new Call(made, null, null));
        return calls;
    }

    /// <summary>
    /// The amount argument: looking back from the call, the first number the monster keeps (an instance getter with a numeric
    /// result) or a constant made into a decimal. Its applier (base.Creature) and card (null) come after it, so they're passed over.
    /// </summary>
    private static (decimal?, MethodInfo?)? AmountBefore(List<CodeInstruction> code, int call)
    {
        for (var j = call - 1; j >= Math.Max(0, call - 12); j--)
        {
            var ins = code[j];
            if (ins.operand is MethodInfo { IsStatic: false } g && g.Name.StartsWith("get_") && g.GetParameters().Length == 0
                && Numeric(g.ReturnType) && typeof(MonsterModel).IsAssignableFrom(g.DeclaringType))
                return (null, g);
            if (Constant(ins) is { } c && j + 1 < code.Count && ToDecimal(code[j + 1])) return (c, null);
            // 1m, 0m, -1m compile to decimal's static fields.
            if (ins.operand is FieldInfo { IsStatic: true } f && f.DeclaringType == typeof(decimal))
                return f.Name switch { "One" => (1m, null), "Zero" => (0m, null), "MinusOne" => (-1m, null), _ => null };
            // Another call that makes a value (a Dynamic var, a calculation): not a number we can read.
            if (ins.operand is MethodInfo other && Numeric(other.ReturnType) && other.Name != "op_Implicit") return null;
        }
        return null;
    }

    private static bool Numeric(Type t) => t == typeof(int) || t == typeof(decimal) || t == typeof(float) || t == typeof(double);

    private static bool ToDecimal(CodeInstruction next) =>
        (next.operand is MethodInfo { Name: "op_Implicit" } mi && mi.ReturnType == typeof(decimal))
        || (next.opcode == OpCodes.Newobj && next.operand is ConstructorInfo ci && ci.DeclaringType == typeof(decimal));

    private static decimal? Constant(CodeInstruction ins)
    {
        var op = ins.opcode;
        if (op == OpCodes.Ldc_I4_M1) return -1;
        if (op == OpCodes.Ldc_I4_0) return 0;
        if (op == OpCodes.Ldc_I4_1) return 1;
        if (op == OpCodes.Ldc_I4_2) return 2;
        if (op == OpCodes.Ldc_I4_3) return 3;
        if (op == OpCodes.Ldc_I4_4) return 4;
        if (op == OpCodes.Ldc_I4_5) return 5;
        if (op == OpCodes.Ldc_I4_6) return 6;
        if (op == OpCodes.Ldc_I4_7) return 7;
        if (op == OpCodes.Ldc_I4_8) return 8;
        if (op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S) return Convert.ToDecimal(ins.operand);
        if (op == OpCodes.Ldc_R4 || op == OpCodes.Ldc_R8) return Convert.ToDecimal(ins.operand);
        return null;
    }
}
