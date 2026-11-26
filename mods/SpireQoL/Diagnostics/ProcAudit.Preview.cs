using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

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
        var sim = window.Prediction?.Sim;
        if (sim == null || window.Prediction?.OutcomeByAim is not { } outcomes || sim.AimFor(window.Target) is not { } aim || !outcomes.TryGetValue(aim, out var preview))
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
        if (window.FightEnded)
        {
            FightEndPreviewIssues(window, entries, sim, preview, issues, model);
            return;
        }
        if (window.After == null || window.AfterIds == null)
        {
            model.Skip("preview", window.AfterSkipped ?? "the board after the play wasn't captured");
            return;
        }
        if (PostPlay(window, sim) is not { } post)
        {
            model.Skip("preview", "the board after the play: its search stopped at a cap or met something not modelled");
            return;
        }
        // The preview is the play's whole turn: what the play itself took from you (Thorns as you hit, a card's HP cost) is in it, and gone already
        // from the board it left, so it's added back from the history.
        var tookInPlay = TakenInPlay(window, entries);
        if (post.TakenMin + tookInPlay < preview.TakenMin || post.TakenMax + tookInPlay > preview.TakenMax)
            issues.Add($"Play Preview: HP lost by the end of the turn showed {Range(preview.TakenMin, preview.TakenMax)}, the play took {tookInPlay} and the board it left gives {Range(post.TakenMin, post.TakenMax)}");
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
    /// Play Preview of a play that won the fight (decision #298): there's no end of turn after it, so you take nothing, which the preview's range
    /// must hold (some outcome of the play ends the fight); and an enemy dead as the fight ends died in this play, which its intent must have
    /// allowed (dead in some outcome). One the fight ended with you, or whose dead weren't read, isn't checked (counted).
    /// </summary>
    private static void FightEndPreviewIssues(Window window, List<CombatHistoryEntry> entries, Combat.SimInputs sim, SimOutcome preview, List<string> issues, ModelLines model)
    {
        if (window.DeadAtFightEnd == null || Sts2State.Me?.Creature is not { IsAlive: true })
        {
            model.Skip("preview", CheckSkip.FightEnded + (window.DeadAtFightEnd == null ? ": who it killed wasn't read" : ": it ended with you"));
            return;
        }
        if (preview.TakenMin > TakenInPlay(window, entries))
            issues.Add($"Play Preview: the play won the fight, but the preview showed {Range(preview.TakenMin, preview.TakenMax)} HP lost in every outcome, more than the {TakenInPlay(window, entries)} the play took");
        foreach (var (creature, id) in sim.Ids)
            if (window.DeadAtFightEnd.Contains(creature) && sim.Start.Enemy(id) is { Hp: > 0 } start && preview.IntentsAfterPlay.TryGetValue(id, out var intent) && !intent.DeadInSome)
                issues.Add($"Play Preview: {start.Name} died in the play that won the fight, but its intent showed it alive in every outcome");
    }

    /// <summary>
    /// The HP the play took from you (each damage to your creature in its history: an enemy's Thorns as you hit, a card's HP cost), which the
    /// preview's turn counts (SimOutcome.TakenMin: every HP you lose from the play on) and the board the play left doesn't.
    /// </summary>
    private static int TakenInPlay(Window window, List<CombatHistoryEntry> entries)
    {
        var you = window.Card.Owner.Creature;
        var taken = 0;
        foreach (var entry in entries)
            if (entry is DamageReceivedEntry hit && ReferenceEquals(hit.Receiver, you)) taken += hit.Result.UnblockedDamage;
        return taken;
    }

    /// <summary>
    /// The end of turn from the board the play left (as shown: teammates' hands left out when the setting is off), searched once for
    /// the lethal and preview checks; null when that search stopped at a cap or met something not modelled.
    /// </summary>
    private static SimOutcome? PostPlay(Window window, Combat.SimInputs sim)
    {
        if (window.PostPlay == null && window.After != null)
            window.PostPlay = SimEngine.Run(sim.FullStart != null ? window.After.WithoutTeammateHands() : window.After);
        return window.PostPlay is { Truncated: false, AnyUnmodelled: false, Branches.Count: > 0 } post ? post : null;
    }
}
