using System.Reflection;
using System.Runtime.CompilerServices;
using Sts2Mods.Core.Game;

namespace SpireQoL;

/// <summary>
/// What the mod's code would otherwise do the first time it runs, done on a worker as the mod loads (decision #254): its methods compiled
/// (the first board a fight reads, the first card's simulation and the first play's checks each compiled tens of milliseconds of the mod's
/// code on the game's thread). Compiling runs none of it. A beta-only method (decision #163) is compiled only where the running game has
/// what it names, by the same check its gate makes (BetaOnly.Runs), which is then known for the gate.
/// </summary>
internal static class FirstUse
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Compiles every method and constructor of <paramref name="assembly"/> with a body of its own (not one of an open generic).</summary>
    /// <returns>How many were compiled.</returns>
    public static int Compile(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }
        var compiled = 0;
        foreach (var type in types)
        {
            if (type.ContainsGenericParameters) continue;
            foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            {
                if (method.IsAbstract || method.ContainsGenericParameters
                    || (method.MethodImplementationFlags & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) != 0
                    || method.Attributes.HasFlag(MethodAttributes.PinvokeImpl)) continue;
                try
                {
                    if (method.GetCustomAttribute<BetaOnlyAttribute>() != null)
                    {
                        if (BetaOnly.Runs(method)) compiled++;
                        continue;
                    }
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                    compiled++;
                }
                catch
                {
                    // One that can't be compiled ahead (an extern accessor, say) is compiled as it's first called, as before.
                }
            }
        }
        return compiled;
    }
}
