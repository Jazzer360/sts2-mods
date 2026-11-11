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
                    foreach (var ins in PatchProcessor.GetOriginalInstructions(m))
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
