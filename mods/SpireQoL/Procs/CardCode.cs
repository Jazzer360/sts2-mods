using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SpireQoL.Procs;

/// <summary>
/// What a game card's own play calls (its OnPlay, the async state machine and local functions in it), read from the installed game's
/// code once per card class. Rules read a card's effects from it (PlayFacts: it draws, gives energy, gains stars), so a card added in a
/// game update, or a card that differs between the game's branches, is read as it is there; the card scan (CardCoverage) reads the same.
/// </summary>
internal static class CardCode
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly ConcurrentDictionary<Type, IReadOnlySet<string>> Cache = new();

    /// <summary>"Type.Method" (and "PowerCmd.Apply&lt;T&gt;") for every call in the card's play. Unreadable code: nothing found.</summary>
    public static IReadOnlySet<string> OnPlayCalls(Type cardType) => Cache.GetOrAdd(cardType, Read);

    /// <summary>The card's play calls Type.Method.</summary>
    public static bool Calls(Type cardType, string type, string method) => OnPlayCalls(cardType).Contains(type + "." + method);

    private static IReadOnlySet<string> Read(Type cardType)
    {
        var set = new HashSet<string>();
        foreach (var method in OnPlayMethods(cardType))
        {
            try
            {
                foreach (var instruction in SpireQoL.Diagnostics.GameCode.Instructions(method))
                {
                    if (instruction.operand is not MethodInfo called || called.DeclaringType == null) continue;
                    set.Add(called.DeclaringType.Name + "." + called.Name);
                    if (called.IsGenericMethod) set.Add(called.DeclaringType.Name + "." + called.Name + "<" + called.GetGenericArguments()[0].Name + ">");
                }
            }
            catch
            {
                // unreadable method: nothing found
            }
        }
        return set;
    }

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.PileType?>> MadeIntoCache = new();

    /// <summary>
    /// The pile each card the play makes goes into (CardPileCmd.AddGeneratedCardToCombat, AddGeneratedCardsToCombat,
    /// AddToCombatAndPreview: the PileType it passes), read from its code, one per call; null for a call whose pile isn't written
    /// as a constant there.
    /// </summary>
    public static IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.PileType?> MadeInto(Type cardType) => MadeIntoCache.GetOrAdd(cardType, ReadMadeInto);

    private static readonly HashSet<string> MakeCalls = new() { "AddGeneratedCardToCombat", "AddGeneratedCardsToCombat", "AddToCombatAndPreview" };

    private static IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.PileType?> ReadMadeInto(Type cardType)
    {
        var piles = new List<MegaCrit.Sts2.Core.Entities.Cards.PileType?>();
        foreach (var method in OnPlayMethods(cardType))
        {
            List<HarmonyLib.CodeInstruction> code;
            try { code = SpireQoL.Diagnostics.GameCode.Instructions(method); } catch { continue; }
            for (var i = 0; i < code.Count; i++)
            {
                if (code[i].operand is not MethodInfo called || called.DeclaringType?.Name != "CardPileCmd" || !MakeCalls.Contains(called.Name)) continue;
                var pileParameter = Array.FindIndex(called.GetParameters(), parameter => parameter.ParameterType == typeof(MegaCrit.Sts2.Core.Entities.Cards.PileType));
                piles.Add(pileParameter >= 0 && ArgumentSource(code, i, called, pileParameter) is { } source && Constant(source) is { } value
                    ? (MegaCrit.Sts2.Core.Entities.Cards.PileType)value : null);
            }
        }
        return piles;
    }

    /// <summary>
    /// The instruction that pushed argument <paramref name="parameter"/> of the call at <paramref name="callAt"/>, walking back over
    /// what each instruction pushes and pops; null when the way there isn't straight (a jump, or a jump's target) or it's unclear.
    /// </summary>
    internal static HarmonyLib.CodeInstruction? ArgumentSource(List<HarmonyLib.CodeInstruction> code, int callAt, MethodInfo called, int parameter)
    {
        var count = called.GetParameters().Length + (called.IsStatic ? 0 : 1);
        var index = parameter + (called.IsStatic ? 0 : 1);
        var wanted = count - index; // slots from the top of the stack: the last argument is 1
        for (var i = callAt - 1; i >= 0; i--)
        {
            var instruction = code[i];
            if (instruction.labels.Count > 0 && i < callAt - 1) return null;
            if (instruction.opcode.FlowControl is System.Reflection.Emit.FlowControl.Branch or System.Reflection.Emit.FlowControl.Cond_Branch) return null;
            if (StackEffect(instruction) is not var (push, pop)) return null;
            if (wanted <= push) return push == 1 ? instruction : null;
            wanted += pop - push;
        }
        return null;
    }

    /// <summary>An int constant an instruction loads (ldc.i4 and its short forms); null for any other.</summary>
    private static int? Constant(HarmonyLib.CodeInstruction instruction)
    {
        var opcode = instruction.opcode;
        if (opcode == System.Reflection.Emit.OpCodes.Ldc_I4_M1) return -1;
        if (opcode.Value >= System.Reflection.Emit.OpCodes.Ldc_I4_0.Value && opcode.Value <= System.Reflection.Emit.OpCodes.Ldc_I4_8.Value)
            return opcode.Value - System.Reflection.Emit.OpCodes.Ldc_I4_0.Value;
        if (opcode == System.Reflection.Emit.OpCodes.Ldc_I4_S || opcode == System.Reflection.Emit.OpCodes.Ldc_I4) return Convert.ToInt32(instruction.operand);
        return null;
    }

    /// <summary>How many values an instruction pushes and pops; null when that can't be told.</summary>
    private static (int Push, int Pop)? StackEffect(HarmonyLib.CodeInstruction instruction)
    {
        var opcode = instruction.opcode;
        int push, pop;
        switch (opcode.StackBehaviourPush)
        {
            case System.Reflection.Emit.StackBehaviour.Push0: push = 0; break;
            case System.Reflection.Emit.StackBehaviour.Push1_push1: push = 2; break;
            case System.Reflection.Emit.StackBehaviour.Varpush:
                if (instruction.operand is not MethodInfo pushed) return null;
                push = pushed.ReturnType == typeof(void) ? 0 : 1;
                break;
            default: push = 1; break;
        }
        switch (opcode.StackBehaviourPop)
        {
            case System.Reflection.Emit.StackBehaviour.Pop0: pop = 0; break;
            case System.Reflection.Emit.StackBehaviour.Pop1 or System.Reflection.Emit.StackBehaviour.Popi or System.Reflection.Emit.StackBehaviour.Popref: pop = 1; break;
            case System.Reflection.Emit.StackBehaviour.Varpop:
                if (instruction.operand is not MethodBase popped) return null;
                pop = popped.GetParameters().Length + (popped.IsStatic || opcode == System.Reflection.Emit.OpCodes.Newobj ? 0 : 1);
                break;
            case System.Reflection.Emit.StackBehaviour.Popi_popi_popi or System.Reflection.Emit.StackBehaviour.Popref_popi_popi
                or System.Reflection.Emit.StackBehaviour.Popref_popi_popi8 or System.Reflection.Emit.StackBehaviour.Popref_popi_popr4
                or System.Reflection.Emit.StackBehaviour.Popref_popi_popr8 or System.Reflection.Emit.StackBehaviour.Popref_popi_popref:
                pop = 3; break;
            default: pop = 2; break;
        }
        return (push, pop);
    }

    /// <summary>
    /// OnPlay, its state machine, and the local functions it declares (named "&lt;OnPlay&gt;g__...", in the card or a closure class,
    /// with their own state machines): Blossom Shot applies its Stretch from one.
    /// </summary>
    public static IEnumerable<MethodBase> OnPlayMethods(Type cardType)
    {
        var nested = NestedTypes(cardType).ToList();
        return cardType.GetMethods(All).Where(method => method.Name == "OnPlay" || method.Name.StartsWith("<OnPlay>", StringComparison.Ordinal)).Cast<MethodBase>()
            .Concat(nested.SelectMany(type => type.GetMethods(All)).Where(method => method.Name.StartsWith("<OnPlay>", StringComparison.Ordinal)))
            .Concat(nested
                .Where(type => type.Name.Contains("<OnPlay>") && typeof(IAsyncStateMachine).IsAssignableFrom(type))
                .Select(type => type.GetMethod("MoveNext", All))
                .Where(method => method != null)
                .Cast<MethodBase>());
    }

    /// <summary>Every type nested in <paramref name="type"/>, at any depth (closures hold local functions' state machines).</summary>
    public static IEnumerable<Type> NestedTypes(Type type)
    {
        foreach (var nestedType in type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
        {
            yield return nestedType;
            foreach (var inner in NestedTypes(nestedType)) yield return inner;
        }
    }
}
