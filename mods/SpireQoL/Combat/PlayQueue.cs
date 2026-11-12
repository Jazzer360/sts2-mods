using System.Collections;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SpireQoL.Features;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

/// <summary>
/// Plays still resolving or queued (decision #27). Plays resolve one at a time, lowest action id first
/// across every player's queue (ActionQueueSet.GetReadyAction); in multiplayer the local queue is the
/// host's confirmed order (a client's own play is queued when the host's ActionEnqueuedMessage comes back).
/// While one of your plays resolves, predictions start from the simulator state taken just before it began
/// (not the half-applied board) and run the pending plays first, each with the simulated play it had on its
/// last frame in hand, for its actual target. Anything the lead-in can't model (a teammate's queued card, a
/// card with no prediction, enemies changed since the snapshot) returns null: predictions fall back to the
/// live state, and the audit logs those plays instead of reporting them.
/// </summary>
internal static class PlayQueue
{
    public sealed record LeadIn(SimState Base, IReadOnlyDictionary<Creature, int> Ids, IReadOnlyList<SimPlay> Plays, IReadOnlyList<CardModel> Cards);

    private static ActionExecutor? _executor;
    private static GameAction? _snapAction;
    private static SimState? _base;
    private static Dictionary<Creature, int>? _ids;

    /// <summary>Called each frame: keeps the snapshot hook on the current executor.</summary>
    public static void Track()
    {
        var executor = RunManager.Instance?.ActionExecutor;
        if (ReferenceEquals(executor, _executor)) return;
        if (_executor != null) _executor.BeforeActionExecuted -= OnBeforeAction;
        _executor = executor;
        _snapAction = null;
        _base = null;
        if (_executor != null) _executor.BeforeActionExecuted += OnBeforeAction;
    }

    // Before the action runs (also called again when it resumes after a choice: the snapshot stays).
    private static void OnBeforeAction(GameAction action)
    {
        try
        {
            if (action is not PlayCardAction play || ReferenceEquals(action, _snapAction)) return;
            var me = Sts2State.Me;
            var cs = Sts2State.Combat;
            if (me == null || cs == null || play.Player != me) return;
            _base = SimAdapter.Build(me, cs, out var ids);
            _ids = ids;
            _snapAction = action;
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[play queue] couldn't take the snapshot: {ex.Message}");
            _snapAction = null;
            _base = null;
        }
    }

    /// <summary>The lead-in for this frame, or null (nothing pending, or something it can't model).</summary>
    public static LeadIn? Current(Player me)
    {
        try
        {
            var running = _executor?.CurrentlyRunningAction;
            var pending = new List<PlayCardAction>();
            if (running is PlayCardAction r) pending.Add(r);
            foreach (var a in QueuedActions())
            {
                if (a is not PlayCardAction p || ReferenceEquals(a, running)) continue;
                if (a.State is GameActionState.Canceled) continue;
                pending.Add(p);
            }
            if (pending.Count == 0) return null;
            // The snapshot belongs to the play resolving now (taken as it began).
            if (running is not PlayCardAction || !ReferenceEquals(running, _snapAction) || _base == null || _ids == null) return null;
            pending = pending.OrderBy(p => p.Id ?? uint.MaxValue).ToList();

            var plays = new List<SimPlay>();
            var cards = new List<CardModel>();
            foreach (var p in pending)
            {
                if (p.Player != me) return null; // a teammate's queued card: no prediction built for it yet
                var card = p.NetCombatCard.ToCardModelOrNull();
                if (card == null || !ProcIndicatorsFeature.LastFacts.TryGetValue(card, out var facts) || facts.Sim is not { } sim) return null;
                if (!SameIds(sim.Ids, _ids)) return null; // enemies changed since: ids don't line up
                var target = p.Target != null && _ids.TryGetValue(p.Target, out var tid) ? tid : -1;
                if (!sim.PlayByTarget.TryGetValue(target, out var play) && !sim.PlayByTarget.TryGetValue(-1, out play)) return null;
                plays.Add(play);
                cards.Add(card);
            }
            return new LeadIn(_base, _ids, plays, cards);
        }
        catch
        {
            return null;
        }
    }

    private static bool SameIds(IReadOnlyDictionary<Creature, int> a, IReadOnlyDictionary<Creature, int> b)
        => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    /// <summary>Every player's queued actions (ActionQueueSet's private per-player queues).</summary>
    private static IEnumerable<GameAction> QueuedActions()
    {
        var set = RunManager.Instance?.ActionQueueSet;
        if (set == null) yield break;
        if (Sts2State.Private<object?>(set, "_actionQueues", null) is not IEnumerable queues) yield break;
        foreach (var q in queues)
        {
            if (q == null) continue;
            if (Sts2State.Private<object?>(q, "actions", null) is not IEnumerable actions) continue;
            foreach (var a in actions)
                if (a is GameAction g) yield return g;
        }
    }
}
