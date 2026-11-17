using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

/// <summary>
/// Reads an enemy's move rules (MonsterMoveStateMachine) into the pure forecast model (IntentForecast, decision #72): its states,
/// the moves it has been given this fight (StateLog), and each random branch's weight and each condition as they stand now
/// (their game lambdas, pure queries on the enemy's state). Reads, never rolls.
/// </summary>
internal static class IntentReader
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>The forecast for an enemy, or null when there's none to show (dead, no rules, its intent on screen hidden).</summary>
    public static (List<ForecastTurn> Turns, MoveStateMachineView View)? Forecast(Creature enemy)
    {
        if (!enemy.IsAlive || enemy.Monster is not { } monster || monster.MoveStateMachine is not { } machine) return null;
        var current = monster.NextMove;
        // The move on screen is hidden from you: its rules would give it away.
        if (current.Intents.Any(i => i is HiddenIntent or UnknownIntent)) return null;
        var graph = Graph(machine);
        if (graph == null) return null;
        var log = machine.StateLog.Select(s => s.Id).ToList();
        var turns = IntentForecast.Forecast(graph, Node(current), log);
        return turns.Count == 0 ? null : (turns, new MoveStateMachineView(machine));
    }

    /// <summary>The move rules as the pure model, or null when they can't be read.</summary>
    public static MoveGraph? Graph(MonsterMoveStateMachine machine)
    {
        try
        {
            var nodes = new Dictionary<string, MoveNode>();
            foreach (var (id, state) in machine.States)
            {
                MoveNode? node = state switch
                {
                    MoveState m => Node(m),
                    RandomBranchState r => new MoveNodeRandom(id, r.States.Select(Branch).ToList()),
                    ConditionalBranchState c => new MoveNodeConditional(id, Conditions(c)),
                    _ => null,
                };
                if (node == null) return null; // a kind of state the forecast doesn't know
                nodes[id] = node;
            }
            var initial = Sts2State.Private<MonsterState?>(machine, "_initialState", null);
            return initial == null ? null : new MoveGraph(nodes, initial.Id);
        }
        catch
        {
            return null;
        }
    }

    private static MoveNodeMove Node(MoveState m) => new(m.Id, m.FollowUpState?.Id ?? m.FollowUpStateId);

    private static RandomBranch Branch(RandomBranchState.StateWeight w)
    {
        var repeat = w.repeatType switch
        {
            MoveRepeatType.CanRepeatForever => MoveRepeat.CanRepeatForever,
            MoveRepeatType.CanRepeatXTimes => MoveRepeat.CanRepeatXTimes,
            MoveRepeatType.CannotRepeat => MoveRepeat.CannotRepeat,
            _ => MoveRepeat.UseOnlyOnce,
        };
        float weight;
        try { weight = w.GetWeight(); } catch { weight = float.NaN; } // its lambda can't answer now: only matters if reached
        return new RandomBranch(w.stateId, repeat, w.maxTimes, w.cooldown, weight);
    }

    /// <summary>Each branch of a conditional state with whether its condition holds now (ConditionalBranch.Evaluate).</summary>
    private static List<(string, bool?)> Conditions(ConditionalBranchState c)
    {
        var list = new List<(string, bool?)>();
        if (typeof(ConditionalBranchState).GetProperty("States", Any)?.GetValue(c) is not System.Collections.IEnumerable branches)
            throw new InvalidOperationException("ConditionalBranchState.States not found");
        foreach (var b in branches)
        {
            var t = b.GetType();
            var id = (string)(t.GetField("id", Any)?.GetValue(b) ?? throw new InvalidOperationException("ConditionalBranch.id"));
            var evaluate = t.GetMethod("Evaluate", Any) ?? throw new InvalidOperationException("ConditionalBranch.Evaluate");
            bool? holds;
            try { holds = (float)evaluate.Invoke(b, null)! > 0f; } catch { holds = null; } // only matters if reached
            list.Add((id, holds));
        }
        return list;
    }
}

/// <summary>Looks up a forecast move's game state (for its intents), by id.</summary>
internal sealed class MoveStateMachineView
{
    private readonly MonsterMoveStateMachine _machine;
    public MoveStateMachineView(MonsterMoveStateMachine machine) => _machine = machine;
    public MoveState? Move(string id) => _machine.States.GetValueOrDefault(id) as MoveState;
}
