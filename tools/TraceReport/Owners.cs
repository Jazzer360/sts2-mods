namespace TraceReport;

/// <summary>Whose code a frame is: the mod's, the game's, Godot's, the .NET runtime's, or someone else's (Harmony, other mods).</summary>
internal enum Owner
{
    Mod,
    Game,
    Godot,
    Runtime,
    Other,
}

internal static class Owners
{
    public static readonly Owner[] All = Enum.GetValues<Owner>();

    public static string Name(Owner owner) => owner.ToString().ToLowerInvariant();

    /// <summary>
    /// A frame's owner from its method's full name and its module: the mod is SpireQoL and Sts2Mods.Core (compiled into it), the game
    /// sts2 (MegaCrit.*), Godot its C# bindings (Godot.*, GodotSharp), the runtime System.*, Microsoft.* and frames without a name. A
    /// Harmony-patched method (DMD&lt;...&gt;) is the method it replaces.
    /// </summary>
    public static Owner Of(string method, string module)
    {
        if (method.StartsWith("DMD<", StringComparison.Ordinal)) method = method[4..];
        if (method.StartsWith("SpireQoL.", StringComparison.Ordinal) || method.StartsWith("Sts2Mods.", StringComparison.Ordinal)
            || module.Equals("SpireQoL", StringComparison.OrdinalIgnoreCase)) return Owner.Mod;
        if (method.StartsWith("MegaCrit.", StringComparison.Ordinal) || module.Equals("sts2", StringComparison.OrdinalIgnoreCase)) return Owner.Game;
        if (method.StartsWith("Godot.", StringComparison.Ordinal) || module.StartsWith("GodotSharp", StringComparison.OrdinalIgnoreCase)
            || module.StartsWith("GodotPlugins", StringComparison.OrdinalIgnoreCase)) return Owner.Godot;
        if (method.Length == 0 || method.StartsWith('?') || method.StartsWith("System.", StringComparison.Ordinal) || method.StartsWith("Microsoft.", StringComparison.Ordinal)
            || method.StartsWith("Internal.", StringComparison.Ordinal) || method.StartsWith("Interop.", StringComparison.Ordinal)
            || module.StartsWith("System.", StringComparison.OrdinalIgnoreCase) || module.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
            || module.Equals("coreclr", StringComparison.OrdinalIgnoreCase)) return Owner.Runtime;
        return Owner.Other;
    }

    /// <summary>
    /// Who an allocation or a CPU sample is charged to: the mod when any of its frames is on the stack (the mod asked for the work, even
    /// done in the game's or the runtime's code), else the game, else Godot, else someone else, else the runtime alone.
    /// </summary>
    public static Owner Charged(IReadOnlyList<Owner> framesLeafFirst)
    {
        foreach (var owner in new[] { Owner.Mod, Owner.Game, Owner.Godot, Owner.Other })
            if (framesLeafFirst.Contains(owner)) return owner;
        return Owner.Runtime;
    }

    /// <summary>A method waiting rather than working (a sample in native code there is a thread blocked, not busy).</summary>
    public static bool Waits(string method) =>
        method.Contains("Wait", StringComparison.Ordinal) || method.Contains("Sleep", StringComparison.Ordinal) || method.Contains(".Join(", StringComparison.Ordinal)
        || method.Contains("LowLevelLifoSemaphore", StringComparison.Ordinal) || method.Contains("LowLevelMonitor", StringComparison.Ordinal)
        || method.Contains("ReadFile", StringComparison.Ordinal) || method.Contains("GetQueuedCompletionStatus", StringComparison.Ordinal);
}
