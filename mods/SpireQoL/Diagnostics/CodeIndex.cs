using System.Reflection;

namespace SpireQoL.Diagnostics;

/// <summary>
/// What a game class's compiled code does, for explaining changes the audits see: the methods it
/// calls ("Type.Method", read from its IL, lambdas and async state machines included) and the hooks
/// it overrides. Read once per class (reflection on the game's code; nothing is changed).
/// </summary>
internal static class CodeIndex
{
    private sealed record Info(HashSet<string> Calls, HashSet<string> Overrides);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Info> Cache = new();

    /// <summary>Reads every class of the game's models up front (CoverageWatch does it on a worker thread at load).</summary>
    public static void Prewarm(IEnumerable<Type> types)
    {
        foreach (var type in types) For(type);
    }
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>The class (or a base class below the game's model types) calls one of these ("Type.Method", or "Type.Prefix*" for methods starting with Prefix), or overrides one of these hooks.</summary>
    public static bool Does(Type type, IReadOnlyCollection<string> calls, IReadOnlyCollection<string> overrides)
    {
        for (var otherType = type; otherType != null && otherType.Assembly == type.Assembly && otherType.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models.", StringComparison.Ordinal) == true; otherType = otherType.BaseType)
        {
            var info = For(otherType);
            if (calls.Any(call => call.EndsWith('*') ? info.Calls.Any(otherCall => otherCall.StartsWith(call[..^1], StringComparison.Ordinal)) : info.Calls.Contains(call))) return true;
            if (overrides.Any(info.Overrides.Contains)) return true;
        }
        return false;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> FlashesOnItsOwn = new();

    /// <summary>
    /// The class (or a base below the game's model types) flashes in some method other than the hooks run after a damage or
    /// block amount it changed (AfterModifyingDamageAmount, AfterModifyingBlockAmount: Slow flashes there only, not as it stacks
    /// on each card played). A tabbed source every outcome sets off is held to its flash only then (issue #425); the play's
    /// damage and block checks cover the amount hooks. Async bodies and lambdas count for the method they belong to.
    /// </summary>
    public static bool FlashesOutsideAmountHooks(Type type) => FlashesOnItsOwn.GetOrAdd(type, static otherType =>
    {
        string[] amountHooks = { "AfterModifyingDamageAmount", "AfterModifyingBlockAmount" };
        for (var nextType = otherType; nextType != null && nextType.Assembly == otherType.Assembly && nextType.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models.", StringComparison.Ordinal) == true; nextType = nextType.BaseType)
        {
            var types = new List<Type> { nextType };
            for (var i = 0; i < types.Count; i++) types.AddRange(types[i].GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
            foreach (var nested in types)
            {
                foreach (var method in nested.GetMethods(All).Cast<MethodBase>())
                {
                    bool flashes;
                    try
                    {
                        flashes = SpireQoL.Diagnostics.GameCode.Instructions(method).Any(ins => ins.operand is MethodBase { Name: "Flash" } otherMethod
                            && otherMethod.DeclaringType?.Name is "PowerModel" or "RelicModel" or "AbstractModel");
                    }
                    catch
                    {
                        continue;
                    }
                    if (!flashes) continue;
                    // A compiler-made body ("<AfterCardPlayed>d__5", "<AfterCardPlayed>b__5_0") belongs to the method in its brackets.
                    var owner = method.Name.StartsWith('<') ? OwnerName(method.Name) : nested != nextType && nested.Name.StartsWith('<') ? OwnerName(nested.Name) : method.Name;
                    if (!amountHooks.Contains(owner)) return true;
                }
            }
        }
        return false;
    });

    /// <summary>The method a compiler-made name belongs to: the part in its first brackets ("&lt;Name&gt;d__5" → "Name"), else the name.</summary>
    private static string OwnerName(string name)
    {
        var open = name.IndexOf('<');
        var close = open < 0 ? -1 : name.IndexOf('>', open);
        return open >= 0 && close > open + 1 ? name[(open + 1)..close] : name;
    }

    /// <summary>
    /// A power whose only hooks change a block or damage amount as the game works it out (Modify*, and
    /// AfterModifyingBlockAmount or AfterModifyingDamageAmount, where it flashes): Fasten adds to a Defend's block, Hard to
    /// Kill caps damage. The card's own number already shows it, so its flash is no proc. Play-count and pile modifiers
    /// (Signal Boost, Nostalgia) are procs with tabs and don't count.
    /// </summary>
    public static bool OnlyModifies(Type type)
    {
        var hooks = new List<string>();
        for (var otherType = type; otherType != null && otherType.Assembly == type.Assembly && otherType.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models.", StringComparison.Ordinal) == true; otherType = otherType.BaseType)
            hooks.AddRange(For(otherType).Overrides.Where(member => !member.StartsWith("get_", StringComparison.Ordinal) && !member.StartsWith("set_", StringComparison.Ordinal)));
        string[] amounts = { "AfterModifyingBlockAmount", "AfterModifyingDamageAmount" };
        return hooks.Any(amounts.Contains)
               && hooks.All(hook => amounts.Contains(hook) || hook is "ModifyBlockAdditive" or "ModifyBlockMultiplicative"
                   || (hook.StartsWith("ModifyDamage", StringComparison.Ordinal) && hook != "ModifyDamageGiven"));
    }

    private static Info For(Type type)
    {
        if (Cache.TryGetValue(type, out var info)) return info;
        var calls = new HashSet<string>();
        var types = new List<Type> { type };
        for (var i = 0; i < types.Count; i++) types.AddRange(types[i].GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
        foreach (var otherType in types)
        {
            foreach (var method in otherType.GetMethods(All).Cast<MethodBase>().Concat(otherType.GetConstructors(All)))
            {
                try
                {
                    foreach (var ins in SpireQoL.Diagnostics.GameCode.Instructions(method))
                        if (ins.operand is MethodBase otherMethod && otherMethod.DeclaringType != null) calls.Add(otherMethod.DeclaringType.Name + "." + otherMethod.Name);
                }
                catch
                {
                    // unreadable: skipped
                }
            }
        }
        var overrides = type.GetMethods(All).Where(method => method.IsVirtual && method.GetBaseDefinition().DeclaringType != type).Select(method => method.Name).ToHashSet();
        info = new Info(calls, overrides);
        return Cache.GetOrAdd(type, info);
    }
}
