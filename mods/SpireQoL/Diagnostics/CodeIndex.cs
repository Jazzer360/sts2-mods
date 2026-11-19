using System.Reflection;
using HarmonyLib;

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
        foreach (var t in types) For(t);
    }
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>The class (or a base class below the game's model types) calls one of these ("Type.Method", or "Type.Prefix*" for methods starting with Prefix), or overrides one of these hooks.</summary>
    public static bool Does(Type type, IReadOnlyCollection<string> calls, IReadOnlyCollection<string> overrides)
    {
        for (var t = type; t != null && t.Assembly == type.Assembly && t.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models.") == true; t = t.BaseType)
        {
            var info = For(t);
            if (calls.Any(c => c.EndsWith('*') ? info.Calls.Any(x => x.StartsWith(c[..^1])) : info.Calls.Contains(c))) return true;
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
    public static bool FlashesOutsideAmountHooks(Type type) => FlashesOnItsOwn.GetOrAdd(type, static t =>
    {
        string[] amountHooks = { "AfterModifyingDamageAmount", "AfterModifyingBlockAmount" };
        for (var c = t; c != null && c.Assembly == t.Assembly && c.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models.") == true; c = c.BaseType)
        {
            var types = new List<Type> { c };
            for (var i = 0; i < types.Count; i++) types.AddRange(types[i].GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
            foreach (var nt in types)
            {
                foreach (var m in nt.GetMethods(All).Cast<MethodBase>())
                {
                    bool flashes;
                    try
                    {
                        flashes = SpireQoL.Diagnostics.GameCode.Instructions(m).Any(ins => ins.operand is MethodBase { Name: "Flash" } mb
                            && mb.DeclaringType?.Name is "PowerModel" or "RelicModel" or "AbstractModel");
                    }
                    catch
                    {
                        continue;
                    }
                    if (!flashes) continue;
                    // A compiler-made body ("<AfterCardPlayed>d__5", "<AfterCardPlayed>b__5_0") belongs to the method in its brackets.
                    var owner = m.Name.StartsWith('<') ? OwnerName(m.Name) : nt != c && nt.Name.StartsWith('<') ? OwnerName(nt.Name) : m.Name;
                    if (!amountHooks.Contains(owner)) return true;
                }
            }
        }
        return false;
    });

    /// <summary>The method a compiler-made name belongs to: the part in its first brackets ("&lt;Name&gt;d__5" → "Name"), else the name.</summary>
    private static string OwnerName(string name)
    {
        var i = name.IndexOf('<');
        var j = i < 0 ? -1 : name.IndexOf('>', i);
        return i >= 0 && j > i + 1 ? name[(i + 1)..j] : name;
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
        for (var t = type; t != null && t.Assembly == type.Assembly && t.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models.") == true; t = t.BaseType)
            hooks.AddRange(For(t).Overrides.Where(n => !n.StartsWith("get_") && !n.StartsWith("set_")));
        string[] amounts = { "AfterModifyingBlockAmount", "AfterModifyingDamageAmount" };
        return hooks.Any(amounts.Contains)
               && hooks.All(n => amounts.Contains(n) || n is "ModifyBlockAdditive" or "ModifyBlockMultiplicative"
                   || (n.StartsWith("ModifyDamage") && n != "ModifyDamageGiven"));
    }

    private static Info For(Type type)
    {
        if (Cache.TryGetValue(type, out var info)) return info;
        var calls = new HashSet<string>();
        var types = new List<Type> { type };
        for (var i = 0; i < types.Count; i++) types.AddRange(types[i].GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
        foreach (var t in types)
        {
            foreach (var m in t.GetMethods(All).Cast<MethodBase>().Concat(t.GetConstructors(All)))
            {
                try
                {
                    foreach (var ins in SpireQoL.Diagnostics.GameCode.Instructions(m))
                        if (ins.operand is MethodBase mb && mb.DeclaringType != null) calls.Add(mb.DeclaringType.Name + "." + mb.Name);
                }
                catch
                {
                    // unreadable: skipped
                }
            }
        }
        var overrides = type.GetMethods(All).Where(m => m.IsVirtual && m.GetBaseDefinition().DeclaringType != type).Select(m => m.Name).ToHashSet();
        info = new Info(calls, overrides);
        return Cache.GetOrAdd(type, info);
    }
}
