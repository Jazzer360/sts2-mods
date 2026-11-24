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
        using var timing = Sts2Mods.Core.Ui.OverlayHost.TimeHook("intent-forecast");
        __state = null;
        try
        {
            if (__instance.MoveStateMachine is not { } machine) return;
            // FindNextMoveState stays put before the first move and on a move that must be performed first.
            if (!Sts2State.Private(machine, "_performedFirstMove", fallback: false) || !__instance.NextMove.CanTransitionAway) return;
            if (IntentReader.Graph(machine) is not { } graph)
            {
                // Intent Forecast can't read this enemy's move rules, so it showed nothing to check: a gap, counted.
                Telemetry.Audits.Skipped(Telemetry.Audits.Intent, "the enemy's move rules couldn't be read", __instance.GetType().Name);
                return;
            }
            CheckConditions(__instance, machine);
            var current = new MoveNodeMove(__instance.NextMove.Id, __instance.NextMove.FollowUpState?.Id ?? __instance.NextMove.FollowUpStateId);
            var turns = IntentForecast.Forecast(graph, current, machine.StateLog.Select(monsterState => monsterState.Id).ToList(), maxTurns: 1);
            __state = turns.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] [intent] forecast before the roll failed: {ex.Message}");
            Telemetry.Audits.Skipped(Telemetry.Audits.Intent, "the forecast before the roll failed", __instance.GetType().Name);
        }
    }

    /// <summary>
    /// The conditional branches as the simulator asks them (decision #195, MoveConditions: each lambda's code translated, asked of the
    /// adapter's state as this monster) against the game's own lambdas, as the game is about to ask them: a difference is reported as a
    /// mismatch. Nothing shown reads them yet; this proves them before anything does.
    /// </summary>
    private static void CheckConditions(MonsterModel monster, MonsterMoveStateMachine machine)
    {
        var states = machine.States.Values.OfType<ConditionalBranchState>().ToList();
        if (states.Count == 0 || Sts2State.Me is not { } me || monster.Creature.CombatState is not { } combat) return;
        var state = SimAdapter.Build(me, combat, out var ids);
        if (!ids.TryGetValue(monster.Creature, out var id)) return;
        var name = monster.GetType().Name;
        foreach (var branchState in states)
            foreach (var (to, lambda) in MoveConditions.BranchesOf(branchState))
            {
                var game = lambda == null || lambda.DynamicInvoke() is true;
                var condition = MoveConditions.Of(monster, lambda);
                var simulated = condition == null ? null : SimEngine.Holds(state, condition, id);
                if (simulated == game) continue;
                var details = $"{name} {branchState.Id} -> {to}: the game's lambda {game}, the simulator's condition {simulated?.ToString() ?? "unread"} ({condition})";
                ModEntry.Log.Detail("[audit] [intent] condition: " + details);
                if (Reported.Add("condition|" + details) && Reported.Count <= 200)
                    Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("intent", $"A conditional move branch read differently from the game: {name}",
                        new[] { "intent", name }, details);
            }
    }

    private static void After(MonsterModel __instance, ForecastTurn? __state)
    {
        using var timing = Sts2Mods.Core.Ui.OverlayHost.TimeHook("intent-forecast");
        if (__state == null) return;
        try
        {
            var picked = __instance.NextMove.Id;
            var name = __instance.GetType().Name;
            var odds = string.Join(", ", __state.Moves.Select(move => $"{move.Move} {move.Chance:0.###}"));
            var chance = __state.Moves.FirstOrDefault(move => move.Move == picked).Chance;
            var ok = chance > 0;
            ModEntry.Log.Detail($"[audit] [intent] {name}: forecast [{odds}], rolled {picked}{(ok ? ", matched" : "")}");
            Telemetry.Audits.Done(Telemetry.Audits.Intent, ok, name);
            if (ok) return;
            var key = name + "|" + odds + "|" + picked;
            if (!Reported.Add(key) || Reported.Count > 200) return;
            var details = $"{name}: forecast [{odds}], the game rolled {picked}. Log: {string.Join(" > ", __instance.MoveStateMachine!.StateLog.Select(monsterState => monsterState.Id))}";
            ModEntry.Log.Detail("[audit] [intent] " + details);
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("intent", $"Intent forecast missed the move the game rolled: {name}",
                new[] { "intent", name }, details);
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] [intent] check after the roll failed: {ex.Message}");
        }
    }
}
