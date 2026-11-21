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
    /// <summary>
    /// A power the move applies and its amount (null: unknown). <paramref name="Maybe"/>: it lands only on some of the move's branches,
    /// and nothing here can tell which (no MoveBranches rule, or a later move whose deciding state can still change): shown without a
    /// number, and the forecast check doesn't hold the move to it.
    /// </summary>
    public sealed record Applied(Type Power, decimal? Amount, bool Maybe = false);

    /// <summary>
    /// Per perform method: each Apply call's power, how its amount is read (a constant or a getter), or nothing; and whether it runs
    /// on every way through the move (<paramref name="Certain"/>) or only on some branches (decision #109).
    /// </summary>
    internal sealed record Call(Type Power, decimal? Constant, MethodInfo? Getter, bool Certain = true, bool Negated = false);

    private static readonly Dictionary<MethodInfo, List<Call>> Calls = new();

    /// <summary>
    /// The powers a move applies, in the order its code applies them (empty when none or unreadable). Powers on only some of the move's
    /// branches (decision #109) are settled by the move's rule (MoveBranches) when <paramref name="now"/> (the move about to happen:
    /// the state that decides it is the state now); otherwise, or with no rule, they're Maybe.
    /// </summary>
    public static List<Applied> Of(MonsterModel monster, MoveState move, bool now = true)
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
            foreach (var call in calls)
            {
                decimal? amount = call.Constant;
                if (call.Getter != null && call.Getter.DeclaringType!.IsInstanceOfType(monster))
                {
                    try { amount = Convert.ToDecimal(call.Getter.Invoke(monster, null)); } catch { amount = null; }
                }
                if (call.Negated && amount is { } positive) amount = -positive; // -SpikenAmount: Toadpole's Spike Spit takes its Thorns away
                result.Add(new Applied(call.Power, amount, Maybe: !call.Certain));
            }
            if (result.Any(applied => applied.Maybe))
            {
                var settled = now ? MoveBranches.Settle(monster, perform.Method, result) : null;
                result = settled?.ToList() ?? Maybes(result);
            }
        }
        catch
        {
            // unreadable: no powers shown
        }
        return result;
    }

    /// <summary>One CreatureCmd.GainBlock call of a move on its own enemy (base.Creature): its amount, as for powers, and its props.</summary>
    internal sealed record BlockCall(decimal? Constant, MethodInfo? Getter, int Props);

    private static readonly Dictionary<MethodInfo, List<BlockCall>> BlockCalls = new();

    /// <summary>
    /// The block a move gives its own enemy (CreatureCmd.GainBlock on base.Creature: block it gives others, Guardbot's, isn't
    /// counted), through the game's block math as things stand (Hook.ModifyBlock: Frail, Dexterity), summed over its calls.
    /// Null when the move gives it none, or when an amount isn't a number we can read (then no number is shown).
    /// </summary>
    public static int? BlockOf(MonsterModel monster, MoveState move)
    {
        try
        {
            if (Sts2Mods.Core.Game.Sts2State.Private<Delegate?>(move, "_onPerform", null) is not { } perform) return null;
            List<BlockCall> calls;
            lock (BlockCalls)
            {
                if (!BlockCalls.TryGetValue(perform.Method, out calls!)) BlockCalls[perform.Method] = calls = ReadBlock(perform.Method);
            }
            if (calls.Count == 0) return null;
            var creature = monster.Creature;
            var total = 0m;
            foreach (var blockCall in calls)
            {
                decimal? amount = blockCall.Constant;
                if (blockCall.Getter != null && blockCall.Getter.DeclaringType!.IsInstanceOfType(monster))
                {
                    try { amount = Convert.ToDecimal(blockCall.Getter.Invoke(monster, null)); } catch { amount = null; }
                }
                if (amount is not { } known) return null;
                total += creature.CombatState is { } combat
                    ? MegaCrit.Sts2.Core.Hooks.Hook.ModifyBlock(combat, creature, known, (MegaCrit.Sts2.Core.ValueProps.ValueProp)blockCall.Props, null, null, out _)
                    : known;
            }
            return (int)total;
        }
        catch
        {
            return null;
        }
    }

    internal static List<BlockCall> ReadBlock(MethodInfo method)
    {
        var body = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? (MethodBase)method;
        var code = Diagnostics.GameCode.Instructions(body);
        var calls = new List<BlockCall>();
        var move = (int)MegaCrit.Sts2.Core.ValueProps.ValueProp.Move;
        for (var i = 0; i < code.Count; i++)
        {
            if (code[i].operand is not MethodInfo { Name: "GainBlock" } otherMethod || otherMethod.DeclaringType?.Name != "CreatureCmd") continue;
            var parameterInfos = otherMethod.GetParameters();
            if (parameterInfos.Length < 2 || parameterInfos[1].ParameterType != typeof(decimal))
            {
                // A BlockVar or another shape: the amount can't be read.
                if (OnOwnCreature(code, i - 1, 8)) calls.Add(new BlockCall(null, null, move));
                continue;
            }
            if (AmountBefore(code, i) is not var (constant, getter, at))
            {
                if (OnOwnCreature(code, i - 1, 8)) calls.Add(new BlockCall(null, null, move));
                continue;
            }
            // The creature is pushed just before the amount: base.Creature (MonsterModel.get_Creature) means its own.
            if (!OnOwnCreature(code, at - 1, 3)) continue;
            // The props follow the amount (after its conversion to decimal for a constant).
            var after = getter != null ? at + 1 : at + 2;
            var props = after < code.Count && Constant(code[after]) is { } otherConstant ? (int)otherConstant : move;
            calls.Add(new BlockCall(constant, getter, props));
        }
        return calls;
    }

    /// <summary>A MonsterModel.get_Creature call within <paramref name="span"/> instructions back from <paramref name="from"/>.</summary>
    private static bool OnOwnCreature(List<CodeInstruction> code, int from, int span)
    {
        for (var j = from; j >= Math.Max(0, from - span + 1); j--)
            if (code[j].operand is MethodInfo { Name: "get_Creature" } method && typeof(MonsterModel).IsAssignableFrom(method.DeclaringType)) return true;
        return false;
    }

    /// <summary>
    /// Branch powers no rule settles: each power once, a power that also lands on every branch shown as certain, the rest Maybe with
    /// no number (two branches giving different amounts: neither is promised).
    /// </summary>
    private static List<Applied> Maybes(List<Applied> all)
    {
        var shown = new List<Applied>();
        foreach (var applied in all)
        {
            if (shown.Any(otherApplied => otherApplied.Power == applied.Power)) continue;
            var same = all.Where(otherApplied => otherApplied.Power == applied.Power).ToList();
            shown.Add(same.Any(otherApplied => !otherApplied.Maybe) && same.All(otherApplied => otherApplied.Amount == same[0].Amount) ? same.First(otherApplied => !otherApplied.Maybe) : new Applied(applied.Power, null, Maybe: true));
        }
        return shown;
    }

    /// <summary>The move's Apply calls as read from its code (the catalog scan and the rules' shape checks).</summary>
    internal static List<Call> CallsOf(MethodInfo method) => Read(method);

    private static List<Call> Read(MethodInfo method)
    {
        var body = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? (MethodBase)method;
        var code = Diagnostics.GameCode.Instructions(body);
        var calls = new List<Call>();
        var at = new List<int>();
        Type? made = null; // a power made first (ModelDb.Power<T>().ToMutable(): Sandpit, Dampen), applied by the plain Apply
        for (var i = 0; i < code.Count; i++)
        {
            if (code[i].operand is not MethodInfo otherMethod) continue;
            if (otherMethod is { IsGenericMethod: true, Name: "Power" } && otherMethod.DeclaringType?.Name == "ModelDb")
            {
                made = otherMethod.GetGenericArguments()[0];
                continue;
            }
            if (otherMethod.Name != "Apply" || otherMethod.DeclaringType?.Name != "PowerCmd") continue;
            var power = otherMethod.IsGenericMethod ? otherMethod.GetGenericArguments()[0] : made;
            if (power == null) continue;
            // A getter followed by a neg: the amount is minus that number (Toadpole's Spike Spit applies -SpikenAmount Thorns).
            calls.Add(AmountBefore(code, i) is var (constant, getter, from)
                ? new Call(power, constant, getter, Negated: getter != null && from + 1 < code.Count && code[from + 1].opcode == OpCodes.Neg)
                : new Call(power, null, null));
            at.Add(i);
        }
        if (calls.Count > 0)
        {
            var certain = Branches.OnEveryPath(code, at);
            for (var callIndex = 0; callIndex < calls.Count; callIndex++) calls[callIndex] = calls[callIndex] with { Certain = certain[callIndex] };
        }
        // Made but applied another way (Dampen: added to the target directly): the icon alone.
        if (calls.Count == 0 && made != null) calls.Add(new Call(made, null, null));
        return calls;
    }

    /// <summary>
    /// The amount argument: looking back from the call, the first number the monster keeps (an instance getter with a numeric
    /// result) or a constant made into a decimal. Its applier (base.Creature) and card (null) come after it, so they're passed over.
    /// </summary>
    private static (decimal?, MethodInfo?, int)? AmountBefore(List<CodeInstruction> code, int call)
    {
        for (var j = call - 1; j >= Math.Max(0, call - 12); j--)
        {
            var ins = code[j];
            if (ins.operand is MethodInfo { IsStatic: false } method && method.Name.StartsWith("get_") && method.GetParameters().Length == 0
                && Numeric(method.ReturnType) && typeof(MonsterModel).IsAssignableFrom(method.DeclaringType))
                return (null, method, j);
            if (Constant(ins) is { } constant && j + 1 < code.Count && ToDecimal(code[j + 1])) return (constant, null, j);
            // 1m, 0m, -1m compile to decimal's static fields.
            if (ins.operand is FieldInfo { IsStatic: true } field && field.DeclaringType == typeof(decimal))
                return field.Name switch { "One" => (1m, null, j), "Zero" => (0m, null, j), "MinusOne" => (-1m, null, j), _ => null };
            // Another call that makes a value (a Dynamic var, a calculation): not a number we can read.
            if (ins.operand is MethodInfo other && Numeric(other.ReturnType) && other.Name != "op_Implicit") return null;
        }
        return null;
    }

    private static bool Numeric(Type type) => type == typeof(int) || type == typeof(decimal) || type == typeof(float) || type == typeof(double);

    private static bool ToDecimal(CodeInstruction next) =>
        (next.operand is MethodInfo { Name: "op_Implicit" } method && method.ReturnType == typeof(decimal))
        || (next.opcode == OpCodes.Newobj && next.operand is ConstructorInfo constructorInfo && constructorInfo.DeclaringType == typeof(decimal));

    private static decimal? Constant(CodeInstruction ins)
    {
        var opCode = ins.opcode;
        if (opCode == OpCodes.Ldc_I4_M1) return -1;
        if (opCode == OpCodes.Ldc_I4_0) return 0;
        if (opCode == OpCodes.Ldc_I4_1) return 1;
        if (opCode == OpCodes.Ldc_I4_2) return 2;
        if (opCode == OpCodes.Ldc_I4_3) return 3;
        if (opCode == OpCodes.Ldc_I4_4) return 4;
        if (opCode == OpCodes.Ldc_I4_5) return 5;
        if (opCode == OpCodes.Ldc_I4_6) return 6;
        if (opCode == OpCodes.Ldc_I4_7) return 7;
        if (opCode == OpCodes.Ldc_I4_8) return 8;
        if (opCode == OpCodes.Ldc_I4 || opCode == OpCodes.Ldc_I4_S) return Convert.ToDecimal(ins.operand);
        if (opCode == OpCodes.Ldc_R4 || opCode == OpCodes.Ldc_R8) return Convert.ToDecimal(ins.operand);
        return null;
    }
}
