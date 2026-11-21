using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Players;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Diagnostics;

/// <summary>ProcAudit, part: A play's issues as a log line and an error report, and the combat summary.</summary>
public sealed partial class ProcAudit
{
    private void Report(Window window, Player me, List<CombatHistoryEntry> hist)
    {
        try
        {
            var entries = window.Indices.Select(index => hist[index]).ToList();
            var flashed = window.Flashes.Select(flash => flash.Model).ToList();
            var issues = Compare(window, me, entries, flashed, out var model);
            ShuffleIssues(window, me, entries, flashed, issues, model);
            DrawnIssues(window, me, entries, issues, model);
            // The before/after snapshot checks: they see everything between the play's start and its end.
            var snapshot = new List<string>();
            AfterPlayIssues(window, entries, snapshot, model);
            LethalIssues(window, entries, snapshot, model);
            PowerChangeIssues(window, me, entries, flashed, snapshot, model);
            EverythingIssues(window, me, entries, flashed, snapshot, model);
            if (window.RanDuringPause != null)
            {
                // Another action ran inside this play (during your choice): its effects are in the snapshots too.
                // The history checks above still ran; these are logged, and counted in the combat summary.
                _interleaved++;
                foreach (var issue in snapshot) model.Skip("", "another action ran during your choice", $"{window.RanDuringPause}: {issue}");
            }
            else issues.AddRange(snapshot);
            if (issues.Count > 0 && window.Target is { IsPlayer: true } ally && ally != me.Creature
                && CardCoverage.TeammateTargetUnmodelled(window.Chosen ?? window.Card))
            {
                // Aimed at a teammate (Ignition's Plasma goes to their orbs): the simulator applies the card to you.
                model.Skip("", "aimed at a teammate; the simulator applies it to you", string.Join("; ", issues));
                issues.Clear();
            }
            if (window.StaleBecause != null && issues.Count > 0)
            {
                // Played before the previous card finished: its tabs didn't see that card's effects.
                model.Skip("", "its tabs were computed before another play finished", $"{window.StaleBecause} finished first ({string.Join("; ", issues)})");
                issues.Clear();
            }
            if (window.Facts?.SimPending == true)
            {
                // Its simulation was still running when the frame stopped waiting: every tab the simulator gives may be missing (every
                // proc tab can come from a model's log, its block and damage too: SimDependent, as for a capped search below), so none
                // of those checks can be held to it. Counted as not checked (decision #92).
                var simOnly = issues.Where(SimDependent).ToList();
                foreach (var issue in simOnly) model.Skip("", "simulation pending", issue);
                issues.RemoveAll(simOnly.Contains);
            }
            if (window.Facts is { Sim: null, SimPending: false } && issues.Any(SimDependent))
            {
                // The card's simulation couldn't be built (it threw: reported as a crash on its own): every tab the simulator gives
                // is missing, so those checks would only repeat that crash. Counted, not reported (decision #92).
                var fromSim = issues.Where(SimDependent).ToList();
                foreach (var issue in fromSim) model.Skip("", "no simulation for this card", issue);
                issues.RemoveAll(fromSim.Contains);
            }
            if (window.Facts is { } facts && (facts.SimIncomplete || facts.Unmodelled.Count > 0))
            {
                // Its tabs came from simulations stopped at the memory cap, or not modelled (decisions #107, #92): what's shown
                // carries the ?, and what happened may be one of those. Every proc tab can come from the simulator (each model's log),
                // so their checks aren't reported; the snapshot checks run their own simulation and stay.
                var why = facts.Unmodelled.Count > 0 ? "a branch of its simulation not modelled" : "its simulation stopped at the memory cap";
                var fromSim = issues.Where(SimDependent).ToList();
                foreach (var issue in fromSim) model.Skip("", why, issue);
                issues.RemoveAll(fromSim.Contains);
            }
            if (issues.Count > 0 && window.Facts?.Sim is { FullStart: not null } handSim
                && handSim.AimFor(window.Target) is { } handAim
                && handSim.HandChangesThePlay(handSim.PlayByTarget[handAim]))
            {
                // Count teammates' hands is off and their hand changes the play: its tabs were worked out without what decides it
                // (decision #25), so they can't be held to what happened. The state after the play is checked with it.
                var fromSim = issues.Where(SimDependent).ToList();
                foreach (var issue in fromSim) model.Skip("", "a teammate's hand changes it; Count teammates' hands is off", issue);
                issues.RemoveAll(fromSim.Contains);
            }
            // What the card showed in hand against the same prediction (decision #103): after the filters above, which are about
            // what happened, not about what was shown; this check has its own (ShownCheck).
            if (window.ShownDiff != null) issues.AddRange(window.ShownDiff);
            var turn = Sts2State.TurnNumber;
            if (window.ShownSkip != null)
            {
                var shownSkip = "tabs shown: " + window.ShownSkip;
                _skipCounts[shownSkip] = (_skipCounts.TryGetValue(shownSkip, out var skn) ? skn : 0) + 1;
                Telemetry.DevEvents.Post(new Telemetry.CheckSkipped(Telemetry.Audits.Play, turn, shownSkip, window.CardName));
            }
            foreach (var skip in model.Skips)
            {
                var reason = skip.Reason;
                _skipCounts[reason] = (_skipCounts.TryGetValue(reason, out var skipCount) ? skipCount : 0) + 1;
                // The play that ended the fight: what the game never logged or cleaned up first, which no model can close (Expected).
                var expected = window.FightEnded && skip.Expected;
                Telemetry.DevEvents.Post(new Telemetry.CheckSkipped(Telemetry.Audits.Play, turn, reason, window.CardName, expected));
            }
            foreach (var reason in window.Facts?.Unmodelled ?? Array.Empty<string>())
                _gapCounts[reason] = (_gapCounts.TryGetValue(reason, out var gapCount) ? gapCount : 0) + 1;
            _checked++;
            if (issues.Count == 0) _matched++;
            Telemetry.DevEvents.Post(new Telemetry.CheckDone(Telemetry.Audits.Play, turn, issues.Count == 0, issues.Count == 0 ? window.CardName : issues[0].Split(':')[0]));
            foreach (var issue in issues)
            {
                var kind = issue.Split(':')[0];
                _issueCounts[kind] = (_issueCounts.TryGetValue(kind, out var issueCount) ? issueCount : 0) + 1;
            }
            var log = ModEntry.Log;
            var tabs = window.Predicted.Count == 0 ? "none" : string.Join(", ", window.Predicted.Select(tab => tab.Text ?? (tab.Amount > 0 ? $"{tab.Key} {tab.Amount}" : tab.Key)));
            if (Sts2State.TurnNumber != _turnPlaysTurn)
            {
                _turnPlays.Clear();
                _turnPlaysTurn = Sts2State.TurnNumber;
            }
            _turnPlays.Add($"{_turnPlays.Count + 1}. {window.CardName}: tabs {tabs} → {(issues.Count == 0 ? "matched" : string.Join("; ", issues))}");
            if (issues.Count > 0)
            {
                log.Detail($"[audit] played {window.CardName}: {string.Join("; ", issues)}. Tabs were: {tabs}.");
                _tabFilter = _ctx.Decor.Filter;
                var lines = WindowLines(window, hist, me);
                foreach (var line in lines) log.Detail($"[audit]   {line}");
                Report(window, issues, tabs, lines, entries.Count, string.Join("\n", _turnPlays));
            }
            foreach (var note in model) log.Detail($"[audit] [model] played {window.CardName}: {note}");
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't check {window.CardName}: {ex.Message}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "audit-check");
        }
    }

    /// <summary>
    /// One Sentry report per issue, grouped by what's wrong rather than by amounts: a missing or
    /// silent source groups by that source; block, damage and budget issues group by the card.
    /// </summary>
    private static Func<string, bool>? _tabFilter;

    private static string Hidden(Window window)
    {
        var hidden = _tabFilter == null ? new List<string>() : window.Predicted.Where(tab => !_tabFilter(tab.Key)).Select(tab => tab.Key).ToList();
        return hidden.Count == 0 ? "none" : string.Join(", ", hidden);
    }

    private static void Report(Window otherWindow, List<string> issues, string tabs, List<string> window, int windowSize, string turnPlays)
    {
        var reporter = Sts2Mods.Core.Diagnostics.ErrorReporter.Instance;
        if (reporter is not { Enabled: true }) return;
        var card = otherWindow.Card.GetType().Name;
        foreach (var issue in issues)
        {
            var colon = issue.IndexOf(':');
            var kind = colon > 0 ? issue[..colon] : issue;
            var rest = colon > 0 ? issue[(colon + 1)..].Trim() : "";
            // "unexplained: 2 more draws than the card's own 1" → effect "draws";
            // "unexplained: powers applied (X, Y)" → effect "powers".
            var effect = rest.Contains(" more ")
                ? rest.Split(" more ")[1].Split(" than ")[0]
                : rest.Split(' ')[0];
            string[] fp = kind switch
            {
                "no tab" or "didn't fire" => new[] { kind, rest.Split(' ')[0] },
                // Which tab on which card: a cache that missed something about this card, or a tab drawn from an old frame.
                "tabs shown" => new[] { kind, card, rest.Split(' ')[0] },
                "power went down" => new[] { kind, rest.Split(' ')[0] },
                // Grouped by the sources that could have done it (amounts and cards left out).
                _ when kind.StartsWith("untracked", StringComparison.Ordinal) => new[] { kind, rest.Contains("could be: ") ? rest[(rest.IndexOf("could be: ", StringComparison.Ordinal) + 10)..].TrimEnd(')') : card },
                // Which powers, and which way an amount was off: different causes, different issues.
                "unexplained" when effect == "powers" => new[] { kind, card, "powers", rest },
                "unexplained" => new[] { kind, card, effect },
                _ when rest.StartsWith("predicted", StringComparison.Ordinal) => new[] { kind, card, Direction(rest) },
                _ => new[] { kind, card },
            };
            var extra = new Dictionary<string, string> { ["audit_window"] = string.Join("\n", window), ["turn_plays"] = turnPlays };
            if (otherWindow.Before != null)
            {
                foreach (var key in new[] { "player", "player_powers", "relics", "orbs", "enemies", "hand" })
                    if (otherWindow.Before.TryGetValue(key, out var before)) extra["before_" + key] = before;
            }
            if (kind.StartsWith("after play", StringComparison.Ordinal) && otherWindow.Facts?.Sim != null)
            {
                extra["sim_state"] = otherWindow.Facts.Sim.Start.ToJson();
                if (otherWindow.After != null) extra["sim_state_after"] = otherWindow.After.ToJson();
            }
            var lag = otherWindow.Lag < 0 ? "never seen in hand" : $"{otherWindow.Lag} frame(s)";
            // Tags: the card, the check, and the source the issue names (a relic or power, or the candidates).
            var source = fp.Length > 1 && fp[1] != card ? fp[1] : "";
            var tags = new Dictionary<string, string>
            {
                ["card"] = card, ["check"] = kind, ["source"] = source, ["effect"] = kind == "unexplained" ? effect : "",
                ["encounter"] = Sts2State.Combat?.Encounter?.GetType().Name ?? "",
            };
            reporter.Mismatch("proc", $"Proc audit: {issue} (played {card})", fp,
                $"Played {otherWindow.CardName} ({card}).\nIssue: {issue}\nAll issues this play: {string.Join("; ", issues)}\nTabs before the play: {tabs}" +
                $"\nFrom the card's last frame in hand to its play starting: {lag}" +
                $"\nExpected from the card itself: {(otherWindow.Facts != null ? Snapshot.FactsLine(otherWindow.Facts) : "no facts")}" +
                $"\nTabs hidden by settings: {Hidden(otherWindow)}" +
                $"\nPlay: {windowSize} history entries from its action (span {otherWindow.From}..{otherWindow.To}), play started at {otherWindow.Start}; shuffles {otherWindow.Shuffles}",
                extra, tags);
        }
    }

    /// <summary>"predicted 4, actual 0" → "less" (more/less than predicted; amounts left out).</summary>
    private static string Direction(string rest)
    {
        static int Last(string text) => int.TryParse(new string(text.Trim().Reverse().TakeWhile(char.IsDigit).Reverse().ToArray()), out var number) ? number : 0;
        var parts = rest.Split(',');
        if (parts.Length < 2) return "";
        var actual = Last(parts[1]);
        var predicted = parts[0].Replace("predicted", "").Trim();
        var range = predicted.Split('–');
        var low = Last(range[0]);
        var high = Last(range[^1]);
        return actual > high ? "more" : actual < low ? "less" : "";
    }

    private void Summarize()
    {
        // How the simulations did against the frame's wait and their caps (decision #104).
        if (Combat.SimWait.Summary() is { } waits) ModEntry.Log.Detail($"[perf] combat summary: simulations: {waits}.");
        if (_ctx.Predictions.TakeHeldFrames() is > 0 and var held) ModEntry.Log.Detail($"[perf] combat summary: card frames shown with tabs held from an earlier board while simulations ran: {held}.");
        if (Combat.TurnEndModel.TakeHeldFrames() is > 0 and var heldForecast) ModEntry.Log.Detail($"[perf] combat summary: frames showing the forecast held from an earlier board: {heldForecast}.");
        if (SimJob.CapSummary() is { } caps) ModEntry.Log.Detail($"[perf] combat summary: searches stopped by {caps}.");
        if (_checked == 0) return;
        var issues = _issueCounts.Count == 0 ? "" : $" ({string.Join(", ", _issueCounts.OrderByDescending(entry => entry.Value).Select(entry => $"{entry.Key} ×{entry.Value}"))})";
        ModEntry.Log.Detail($"[audit] combat summary: {_checked} card plays checked ({_fromHand} against the tabs in hand, {_interleaved} with another action during a choice), {_matched} matched{issues}.");
        static string List(Dictionary<string, int> counts) => string.Join(", ", counts.OrderByDescending(entry => entry.Value).Select(entry => $"{entry.Key} ×{entry.Value}"));
        if (_skipCounts.Count > 0) ModEntry.Log.Detail($"[audit] combat summary: card play checks skipped: {List(_skipCounts)}.");
        if (_gapCounts.Count > 0) ModEntry.Log.Detail($"[audit] combat summary: not modelled in played cards' simulations: {List(_gapCounts)}.");
        _checked = _matched = _fromHand = _interleaved = 0;
        _issueCounts.Clear();
        _skipCounts.Clear();
        _gapCounts.Clear();
    }
}
