using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using SpireQoL.Sim;

namespace SpireQoL.Diagnostics;

public sealed partial class ProcAudit
{
    /// <summary>
    /// Play Preview (decision #183) held to the play: what it showed for the card at the target it was played at against the board the
    /// play left. That board is one of the play's outcomes, so the damage you take from it at the end of the turn must lie in the
    /// preview's range, and each enemy's intent on it (a hit's damage on you) in the range the preview drew on its intent.
    /// </summary>
    private static void PreviewIssues(Window window, List<CombatHistoryEntry> entries, List<string> issues, ModelLines model)
    {
        var sim = window.Facts?.Sim;
        if (sim == null || window.Facts?.OutcomeByAim is not { } outcomes || sim.AimFor(window.Target) is not { } aim || !outcomes.TryGetValue(aim, out var preview))
        {
            model.Skip("preview", "no outcome for the target it was played at");
            return;
        }
        if (preview.Truncated || preview.AnyUnmodelled)
        {
            model.Skip("preview", "its outcome stopped at a cap or met something not modelled (shown with a ?)");
            return;
        }
        if (entries.OfType<CardPlayStartedEntry>().Any(started => started.CardPlay.IsAutoPlay && !ReferenceEquals(started.CardPlay.Card, window.Chosen)))
        {
            model.Skip("preview", AutoPlayReason(window, entries, "auto-played cards"));
            return;
        }
        if (window.FightEnded || window.After == null || window.AfterIds == null)
        {
            model.Skip("preview", window.FightEnded ? "the play ended the fight" : window.AfterSkipped ?? "the board after the play wasn't captured");
            return;
        }
        if (PostPlay(window, sim) is not { } post)
        {
            model.Skip("preview", "the board after the play: its search stopped at a cap or met something not modelled");
            return;
        }
        if (post.TakenMin < preview.TakenMin || post.TakenMax > preview.TakenMax)
            issues.Add($"Play Preview: damage taken at the end of the turn showed {Range(preview.TakenMin, preview.TakenMax)}, the board the play left gives {Range(post.TakenMin, post.TakenMax)}");
        foreach (var (creature, id) in sim.Ids)
        {
            if (!preview.IntentsAfterPlay.TryGetValue(id, out var intent) || intent.DeadInSome) continue;
            if (!window.AfterIds.TryGetValue(creature, out var afterId) || window.After.Enemy(afterId) is not { Alive: true, Intent.Count: > 0 } enemy) continue;
            var now = SimEngine.IntentDamage(window.After, enemy, hit: 0, Who.You);
            if (now < intent.Least || now > intent.Most)
                issues.Add($"Play Preview: {enemy.Name}'s intent showed {Range(intent.Least, intent.Most)} a hit, the board the play left gives {now}");
        }
    }

    /// <summary>
    /// The end of turn from the board the play left (as shown: teammates' hands left out when the setting is off), searched once for
    /// the lethal and preview checks; null when that search stopped at a cap or met something not modelled.
    /// </summary>
    private static SimOutcome? PostPlay(Window window, Procs.SimInputs sim)
    {
        if (window.PostPlay == null && window.After != null)
            window.PostPlay = SimEngine.Run(sim.FullStart != null ? window.After.WithoutTeammateHands() : window.After);
        return window.PostPlay is { Truncated: false, AnyUnmodelled: false, Branches.Count: > 0 } post ? post : null;
    }
}
