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
