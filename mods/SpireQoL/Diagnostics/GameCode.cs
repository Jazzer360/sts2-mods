using System.Reflection;
using HarmonyLib;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Reads a game method's compiled code (Harmony's PatchProcessor.GetOriginalInstructions), one read at a time: it isn't safe
/// from several threads at once (CoverageWatch reads on a worker thread at load while the frame reads event options), and a
/// read that fails there would be taken as unreadable, so a check would quietly see nothing.
/// </summary>
internal static class GameCode
{
    private static readonly object Gate = new();
    private static bool _emitReady;

    public static List<CodeInstruction> Instructions(MethodBase method)
    {
        _readsOnThisThread++;
        lock (Gate)
        {
            MakeEmitReadyHeld();
            return PatchProcessor.GetOriginalInstructions(method);
        }
    }

    /// <summary>
    /// Decision #391: Harmony's emitted code (a code read here, a patch, AccessTools.FieldRefAccess or MethodDelegate) goes through MonoMod's IL
    /// generator proxy, an assembly made and loaded the first time any thread asks, with nothing stopping two threads doing that at once: the
    /// second load fails ("Assembly with same name is already loaded"), and that read, patch or accessor fails with it. The mod starts reading the
    /// game's code on workers before its own patches are applied, and nothing says another mod's patches came first, so the proxy is made here, on
    /// the game's thread, before any of that starts (ModEntry.Initialize), and again under the read's lock if that failed. A code read and a field
    /// accessor are both made: the one generates no code, the other does, and each has first-use state of its own.
    /// </summary>
    /// <returns>Whether it's ready (false: it failed, and the next read tries again).</returns>
    public static bool MakeEmitReady()
    {
        lock (Gate) return MakeEmitReadyHeld();
    }

    private static bool MakeEmitReadyHeld()
    {
        if (_emitReady) return true;
        try
        {
            _ = PatchProcessor.GetOriginalInstructions(typeof(EmitProbe).GetMethod(nameof(EmitProbe.Read))!);
            var accessor = AccessTools.FieldRefAccess<EmitProbe, int>(nameof(EmitProbe.Value));
            var probe = new EmitProbe { Value = 1 };
            _emitReady = accessor(probe) == 1;
        }
        catch
        {
            // Left for the next read to try: a read that fails is taken as unreadable, as before.
        }
        return _emitReady;
    }

    /// <summary>What the warm-up reads and accesses: a method of the mod's own and a field of the mod's own, so nothing of the game's is touched.</summary>
    private sealed class EmitProbe
    {
        public int Value;

        public int Read() => Value + 1;
    }

    /// <summary>How many methods this thread has read here: what the tests hold a fight's first board read to (none once the load has read the code it asks: decision #254).</summary>
    public static int ReadsOnThisThread => _readsOnThisThread;

    [ThreadStatic] private static int _readsOnThisThread;
}
