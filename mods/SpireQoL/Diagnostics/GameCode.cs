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

    public static List<CodeInstruction> Instructions(MethodBase method)
    {
        lock (Gate) return PatchProcessor.GetOriginalInstructions(method);
    }
}
