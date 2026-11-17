using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL;

/// <summary>
/// Checks Intent Forecast's model (decision #72) every time the game rolls an enemy's next move: an observe-only prefix on
/// MonsterModel.RollMove works out the forecast's next turn from the state at that moment (its rules, its move log, its
/// conditions and weights now), and a postfix compares it with the move the game picked. A certain forecast must match; a
/// random one must have given the picked move a chance above 0 (its odds are logged, to be compared across many rolls).
/// Rolls that don't move on (an enemy's first move, a move that must be performed first) aren't checked. Changes nothing.
/// </summary>
internal static class IntentForecastAudit
{
    private static readonly HashSet<string> Reported = new();

    public static void Patch(Harmony harmony)
    {
        var roll = AccessTools.Method(typeof(MonsterModel), nameof(MonsterModel.RollMove));
        harmony.Patch(roll, prefix: new HarmonyMethod(typeof(IntentForecastAudit), nameof(Before)),
            postfix: new HarmonyMethod(typeof(IntentForecastAudit), nameof(After)));
    }

    private static void Before(MonsterModel __instance, out ForecastTurn? __state)
    {
        __state = null;
        try
        {
            if (__instance.MoveStateMachine is not { } machine) return;
            // FindNextMoveState stays put before the first move and on a move that must be performed first.
            if (!Sts2State.Private(machine, "_performedFirstMove", false) || !__instance.NextMove.CanTransitionAway) return;
            if (IntentReader.Graph(machine) is not { } graph) return;
            var current = new MoveNodeMove(__instance.NextMove.Id, __instance.NextMove.FollowUpState?.Id ?? __instance.NextMove.FollowUpStateId);
            var turns = IntentForecast.Forecast(graph, current, machine.StateLog.Select(s => s.Id).ToList(), maxTurns: 1);
            __state = turns.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] [intent] forecast before the roll failed: {ex.Message}");
        }
    }

    private static void After(MonsterModel __instance, ForecastTurn? __state)
    {
        if (__state == null) return;
        try
        {
            var picked = __instance.NextMove.Id;
            var name = __instance.GetType().Name;
            var odds = string.Join(", ", __state.Moves.Select(m => $"{m.Move} {m.Chance:0.###}"));
            var chance = __state.Moves.FirstOrDefault(m => m.Move == picked).Chance;
            var ok = chance > 0;
            ModEntry.Log.Info($"[audit] [intent] {name}: forecast [{odds}], rolled {picked}{(ok ? ", matched" : "")}");
            if (ok) return;
            var key = name + "|" + odds + "|" + picked;
            if (!Reported.Add(key) || Reported.Count > 200) return;
            var details = $"{name}: forecast [{odds}], the game rolled {picked}. Log: {string.Join(" > ", __instance.MoveStateMachine!.StateLog.Select(s => s.Id))}";
            ModEntry.Log.Warn("[audit] [intent] " + details);
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("intent", $"Intent forecast missed the move the game rolled: {name}",
                new[] { "intent", name }, details);
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] [intent] check after the roll failed: {ex.Message}");
        }
    }
}
