using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Players;
using SpireQoL.Combat;
using SpireQoL.Sim;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Checks Incoming Damage's forecast against what actually happened, and logs a mismatch to the
/// game log (godot.log, lines start with [SpireQoL] [audit]) so forecast bugs surface.
///
/// The forecast from the last frame of your turn (before ending the turn starts, so it covers all
/// end-of-turn effects) is frozen. When your next turn starts, the combat history recorded in
/// between is split by whose turn each entry happened on (the entry's own CurrentSide):
///  - your end of turn (player side): block gained there vs the forecast's end-of-turn block;
///  - your end of turn + the enemy turn: HP you and Osty lost vs the forecast. Entries from your
///    next turn (Poison ticking on you at its start, etc.) are left out.
/// It also checks the outcome simulator enemy by enemy (Sim/SimEngine; the forecast's own run):
///  - dead before it acted: predicted in every branch but it acted, or in no branch but it died first;
///  - its HP when your end of turn was over, against the branches' range;
///  - damage instances that got through its block during your end of turn (what Slumber and Asleep
///    count), against the branches' range;
///  - Imbalanced: whether its first hit on you was fully blocked, when every branch agreed.
/// Only what no branch allows is reported, so a random target never causes a report.
/// A one-line summary per combat gives the match rate. Read-only: it only reads the history.
/// </summary>
public sealed class ForecastAudit : IFeature, IFrameFeature
{
    public string Id => "forecast-audit";
    public string DisplayName => "Forecast Audit (log only)";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
    }

    public void Attach(OverlayHost host)
    {
    }

    private TurnEndModel? _last;      // forecast on the latest frame of your turn
    private int _lastHistoryCount;    // history size at that frame
    private int _lastTurn;
    private bool _wasPlayerTurn;

    private TurnEndModel? _frozen;
    private int _frozenAt;
    private int _frozenTurn;

    private CombatHistory? _history;
    private int _checked;
    private int _matched;
    /// <summary>
    /// Decision #92, rule 1: turns not checked, by reason (a gap, not a pass), and what the forecasts didn't model, by reason
    /// (rule 2): both in the combat summary, so the logs show which gaps come up and how often.
    /// </summary>
    private static readonly Dictionary<string, int> Skips = new(), Gaps = new();

    private static void CountIn(Dictionary<string, int> counts, string reason) => counts[reason] = (counts.TryGetValue(reason, out var n) ? n : 0) + 1;

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        // You died before your next turn: the fight ends without one, so check the turn now, with what happened up to your death.
        if (_frozen != null && _history != null && me?.Creature is { IsDead: true })
        {
            Compare(_frozen, _history, _frozenAt, me, youDied: true);
            _frozen = null;
        }
        if (me == null || !Sts2State.InCombat)
        {
            Summarize();
            _frozen?.Job?.Cancel();
            _frozen = null;
            _wasPlayerTurn = false;
            _history = null;
            return;
        }

        var history = CombatManager.Instance.History;
        if (!ReferenceEquals(history, _history))
        {
            Summarize();
            _history = history;
            _frozen?.Job?.Cancel();
            _frozen = null;
        }

        var playerTurn = Sts2State.IsPlayerTurn;
        if (playerTurn)
        {
            // A frozen forecast from the previous turn: the enemy turn is over, compare.
            if (_frozen != null && Sts2State.TurnNumber > _frozenTurn)
            {
                Compare(_frozen, history, _frozenAt, me);
                _frozen = null;
            }
            _last = TurnEndModel.Compute();
            _lastHistoryCount = history.Entries.Count();
            _lastTurn = Sts2State.TurnNumber;
        }
        else if (_wasPlayerTurn && _last != null)
        {
            // Ending the turn just started: freeze the last forecast.
            _frozen = _last.Full; // teammates' hands included (decision #25: the check uses everything that isn't a roll)
            // Its exploration goes on after the board changes (decision #87): the check compares against all of it.
            if (_frozen.Job is { } frozenJob) frozenJob.Pinned = true;
            _frozenAt = _lastHistoryCount;
            _frozenTurn = _lastTurn;
        }
        _wasPlayerTurn = playerTurn;
    }

    private void Summarize()
    {
        static string List(Dictionary<string, int> c) => string.Join(", ", c.OrderByDescending(k => k.Value).Select(k => $"{k.Key} ×{k.Value}"));
        if (_checked > 0) ModEntry.Log.Info($"[audit] combat summary: {_checked} enemy turns forecast, {_matched} matched.");
        if (Skips.Count > 0) ModEntry.Log.Info($"[audit] combat summary: enemy turns not checked: {List(Skips)}.");
        if (Gaps.Count > 0) ModEntry.Log.Info($"[audit] combat summary: not modelled in the forecast: {List(Gaps)}.");
        _checked = _matched = 0;
        Skips.Clear();
        Gaps.Clear();
    }

    /// <summary>The side whose turn an entry happened on (CombatHistoryEntry.CurrentSide is private).</summary>
    private static CombatSide SideOf(CombatHistoryEntry e) => Sts2State.Private(e, "CurrentSide", CombatSide.Player);

    /// <summary>
    /// Each simulated teammate's HP lost this round against the forecast's range (worst branch to best),
    /// on every game: a teammate is simulated through its own code path (not the one their game checks for
    /// themselves), and Teammate Incoming Damage shows it. Skipped when a teammate auto-played a card at end
    /// of turn (Stampede: a random card). What's shown leaves their hand out (decision #25); when that range
    /// differs, the line says so, and an HP loss only the cards in their hand explain is logged, not reported.
    /// </summary>
    private static string TeammateIssues(TurnEndModel f, List<DamageReceivedEntry> damage, List<CombatHistoryEntry> endOfTurn, List<string> issues)
    {
        var o = f.Outcome;
        var line = "";
        for (var k = 0; k < f.Teammates.Count && o.Branches.Count > 0 && k < o.Branches[0].Teammates.Count; k++)
        {
            var mate = f.Teammates[k];
            var name = SafeCharacter(mate);
            // Overkill counts, as yours does (decision #93: the forecast isn't capped by their HP).
            var actual = damage.Where(d => d.Result.Receiver == mate.Creature).Sum(d => d.Result.UnblockedDamage + d.Result.OverkillDamage);
            var (min, max) = (o.TeammateTakenMin(k), o.TeammateTakenMax(k));
            line += $"; teammate {name} HP lost {actual} (predicted {(min == max ? $"{max}" : $"{min}-{max}")})";
            var shown = k < f.TeammateView.Count ? f.TeammateView[k] : null;
            if (shown != null && (shown.TakenMin != min || shown.TakenMax != max))
                line += $" (shown without their hand: {(shown.TakenMin == shown.TakenMax ? $"{shown.TakenMax}" : $"{shown.TakenMin}-{shown.TakenMax}")})";
            if (endOfTurn.OfType<CardPlayStartedEntry>().Any(e => e.CardPlay.IsAutoPlay && e.CardPlay.Card.Owner == mate))
            {
                line += " (auto-played a card: not checked)";
                CountIn(Skips, "a teammate auto-played a card at end of turn");
                continue;
            }
            if (actual > max || (!o.Truncated && actual < min)) issues.Add($"teammate {name} HP lost: forecast {(min == max ? $"{max}" : $"{min}-{max}")}, actual {actual}");
            // Their Osty (Teammate Incoming Damage shows what he absorbs).
            if (mate.Osty is { } osty)
            {
                var ostyActual = damage.Where(d => d.Result.Receiver == osty).Sum(d => d.Result.UnblockedDamage);
                var (oMin, oMax) = (o.TeammateOstyTakenMin(k), o.TeammateOstyTakenMax(k));
                line += $", their Osty {ostyActual} (predicted {(oMin == oMax ? $"{oMax}" : $"{oMin}-{oMax}")})";
                if (ostyActual > oMax || (!o.Truncated && ostyActual < oMin)) issues.Add($"teammate {name} Osty HP lost: forecast {(oMin == oMax ? $"{oMax}" : $"{oMin}-{oMax}")}, actual {ostyActual}");
            }
        }
        return line;
    }

    private static string SafeCharacter(Player p)
    {
        try { return p.Character.GetType().Name; } catch { return "?"; }
    }

    /// <param name="youDied">You died before your next turn (the fight ended): the enemy turn stopped there, so your HP lost is
    /// checked as a death against the forecast's, and the enemies left to act didn't.</param>
    private void Compare(TurnEndModel f, CombatHistory history, int from, Player me, bool youDied = false)
    {
        // The frozen forecast's latest exploration (decision #87); it isn't needed after this.
        f = f.Refreshed();
        // What it didn't model (rule 2): logged and counted, whatever the check finds.
        foreach (var reason in f.Outcome.UnmodelledReasons) CountIn(Gaps, reason);
        if (f.Outcome.AnyUnmodelled) ModEntry.Log.Info($"[audit] [gaps] turn {_frozenTurn}: the forecast didn't model: {string.Join("; ", f.Outcome.UnmodelledReasons)}");
        if (f.Outcome.Truncated) CountIn(Gaps, "the forecast's outcomes weren't all explored");
        if (f.Job is { } job)
        {
            job.Pinned = false;
            job.Cancel();
            if (!job.Finished) ModEntry.Log.Info($"[audit] [outcome] turn {_frozenTurn}: the forecast's exploration hadn't finished; compared with {f.Outcome.Branches.Count} branches explored so far");
        }
        // Your end of turn (player side), then the enemy turn; stop at your next turn.
        var window = new List<CombatHistoryEntry>();
        var endOfTurn = new List<CombatHistoryEntry>();
        var sawEnemy = false;
        foreach (var e in history.Entries.Skip(from))
        {
            var side = SideOf(e);
            if (side == CombatSide.Enemy) sawEnemy = true;
            else if (sawEnemy) break;
            window.Add(e);
            if (!sawEnemy) endOfTurn.Add(e);
        }

        // No enemy turn came (an extra turn: The Hanged Man's ExtraTurnPower.ShouldTakeExtraTurn): nothing to compare.
        if (!sawEnemy && !youDied)
        {
            ModEntry.Log.Info($"[audit] [outcome] turn {_frozenTurn}: not checked (no enemy turn followed: an extra turn)");
            CountIn(Skips, "an extra turn (no enemy turn to compare)");
            return;
        }

        var osty = me.Osty;
        var damage = window.OfType<DamageReceivedEntry>().ToList();
        // A hit that kills you (Fairy in a Bottle, Lizard Tail bring you back) takes only the HP you had left; the
        // forecast isn't capped by your HP, so the overkill counts too (Creature.LoseHpInternal: OverkillDamage).
        var actualTaken = damage.Where(d => d.Result.Receiver == me.Creature).Sum(d => d.Result.UnblockedDamage + d.Result.OverkillDamage);
        var actualOsty = osty == null ? 0 : damage.Where(d => d.Result.Receiver == osty).Sum(d => d.Result.UnblockedDamage);
        var actualBlockGain = endOfTurn.OfType<BlockGainedEntry>().Where(b => b.Actor == me.Creature).Sum(b => b.Amount);

        // Cards of yours auto-played at end of turn: the simulator plays Stampede's, Simpleton's, Howl from Beyond and
        // I Am Invincible (SimEngine.AutoPostPlay), Stampede's and Simpleton's picks a branch each. The history shows which
        // cards were picked and what they hit, so only the branches that picked the same are checked (decision #86). The turn
        // isn't checked when the pick that happened changed your hand (it drew or made cards: what came after isn't
        // simulated), or when the count matches no branch (another source).
        var issues = new List<string>();
        var autoPlays = endOfTurn.OfType<CardPlayStartedEntry>().Where(e => e.CardPlay.IsAutoPlay && e.CardPlay.IsFirstInSeries && e.CardPlay.Card.Owner == me).ToList();
        if (autoPlays.Count > 0 && autoPlays[0] is var auto)
        {
            var hand = f.State.P.AutoHand?.Select(c => c.Id).ToHashSet();
            var picks = hand == null ? new List<SimPick>() : autoPlays.Where(e => hand.Contains(SimAdapter.HandId(e.CardPlay.Card)))
                .Select(e => new SimPick(SimAdapter.HandId(e.CardPlay.Card), e.CardPlay.Target is { } t && f.Ids.TryGetValue(t, out var tid) ? tid : -1)).ToList();
            bool Picked(SimState b) => b.AutoPlaysUnknown
                ? b.AutoPlayed <= autoPlays.Count && picks.Count >= b.AutoPicks.Count && picks.Take(b.AutoPicks.Count).SequenceEqual(b.AutoPicks)
                : b.AutoPlayed == autoPlays.Count && picks.SequenceEqual(b.AutoPicks);
            string Predicted() => $"HP lost {actualTaken} (predicted {(f.TakenMin == f.Taken ? $"{f.Taken}" : $"{f.TakenMin}-{f.Taken}")})";
            var narrowed = f.Narrowed(Picked);
            if (narrowed == null && hand != null && picks.Count == autoPlays.Count && f.State.P.Stampede + f.State.P.Simpleton > 0)
            {
                // Every auto-play was a pick from your hand, and no branch made those picks: the simulator's picks are wrong.
                var names = string.Join(", ", autoPlays.Select(e => $"{e.CardPlay.Card.GetType().Name}{(e.CardPlay.Target is { } t ? $" at {t.Monster?.GetType().Name ?? "you"}" : "")}"));
                issues.Add($"auto-play: you auto-played {names}, and no branch picked that");
            }
            else if (narrowed == null)
            {
                ModEntry.Log.Info($"[audit] [outcome] turn {_frozenTurn}: not checked (you auto-played {auto.CardPlay.Card.GetType().Name} at end of turn, which no branch did); {Predicted()}");
                CountIn(Skips, "an end-of-turn auto-play no branch made (another source)");
                return;
            }
            else if (narrowed.Outcome.Branches.Any(b => b.AutoPlaysUnknown))
            {
                // The pick it stopped at (the last one in that branch).
                var last = narrowed.Outcome.Branches.First(b => b.AutoPlaysUnknown).AutoPicks.LastOrDefault();
                var changer = autoPlays.FirstOrDefault(e => last != null && SimAdapter.HandId(e.CardPlay.Card) == last.Card)?.CardPlay.Card ?? auto.CardPlay.Card;
                ModEntry.Log.Info($"[audit] [outcome] turn {_frozenTurn}: not checked (you auto-played {changer.GetType().Name} at end of turn, which changed your hand); {Predicted()}");
                CountIn(Skips, "an end-of-turn auto-play that changed your hand");
                return;
            }
            else
            {
                f = narrowed;
            }
        }

        // A branch the forecast didn't model (decision #92, rule 2) is unknown both ways: what happened may be that branch, so
        // nothing can be called wrong. The gap is counted above; the check is skipped and counted too (rule 1).
        if (f.Outcome.AnyUnmodelled)
        {
            ModEntry.Log.Info($"[audit] [outcome] turn {_frozenTurn}: not checked (a branch the forecast didn't model: {string.Join("; ", f.Outcome.UnmodelledReasons)}); HP lost {actualTaken} (predicted {(f.TakenMin == f.Taken ? $"{f.Taken}" : $"{f.TakenMin}-{f.Taken}")})");
            CountIn(Skips, "a branch the forecast didn't model");
            return;
        }
        // Not every outcome explored (decision #87: the exploration hadn't finished, or ran out of room): the worst case still holds
        // (the safe run bounds it), but the best case and "never happens" come from the explored branches only, and an unexplored
        // one may be what happened. Those are left unchecked, never reported.
        var open = f.Outcome.Truncated;

        // Less than forecast is expected when random-target end-of-turn damage was left out: it may
        // have killed an attacker (the forecast stays on the safe side on purpose).
        // The forecast is the worst branch; a random target can make it come in lower (down to TakenMin).
        // Your death ends the enemy turn: hits after it never come, so it's the death that's checked (the forecast's worst
        // branch, or any branch when a roll decides it).
        if (youDied)
        {
            if (!f.Dies && !f.Outcome.Branches.Any(b => b.Died)) issues.Add($"your death: forecast you survive (HP lost {f.Taken}), but you died (HP lost {actualTaken})");
        }
        else if (actualTaken > f.Taken || (!open && actualTaken < f.TakenMin)) issues.Add($"HP lost: forecast {f.Taken}, actual {actualTaken}");
        if (actualOsty > f.OstyTaken || (!open && actualOsty < f.OstyTakenMin)) issues.Add($"Osty HP lost: forecast {f.OstyTaken}, actual {actualOsty}");
        if (actualBlockGain != f.BlockGain) issues.Add($"end-of-turn block: forecast {f.BlockGain}, actual {actualBlockGain}");
        // Teammates (multiplayer, decision #24): each one's HP lost against their range over the branches.
        var teamLine = TeammateIssues(f, damage, endOfTurn, issues);
        var enemyIssues = EnemyIssues(f, window, endOfTurn, me, out var outcomeLine, out var actuals);
        issues.AddRange(enemyIssues.Select(x => x.Issue));
        // Each number above is checked against its own range over the branches; together they must
        // also come from one branch (one set of random targets explains everything that happened).
        var o = f.Outcome;
        if (issues.Count == 0 && !youDied && !o.Truncated && o.Branches.Count > 1)
        {
            bool Explains(SimState b) =>
                b.Taken == actualTaken && (osty == null || b.OstyTaken == actualOsty) && b.AutoPlayed == autoPlays.Count
                && actuals.All(kv => b.Enemy(kv.Key) is { } e && e.HpAtTurnEnd == kv.Value.Hp && e.UnblockedInstances == kv.Value.Through && e.DiedBeforeActing == kv.Value.Died);
            if (!o.Branches.Any(Explains))
                issues.Add($"no single branch: each result is possible on its own, but none of the {o.Branches.Count} branches gives them all (HP lost {actualTaken}; {outcomeLine})");
        }
        // Every enemy turn, matched or not: predicted vs actual per enemy, so patterns show before they're reports.
        ModEntry.Log.Info($"[audit] [outcome] turn {_frozenTurn}:{(youDied ? " you died;" : "")} HP lost {actualTaken} (predicted {(f.TakenMin == f.Taken ? $"{f.Taken}" : $"{f.TakenMin}-{f.Taken}")}); {outcomeLine}{teamLine}");

        _checked++;
        if (issues.Count == 0)
        {
            _matched++;
            return;
        }

        var log = ModEntry.Log;
        log.Warn($"[audit] turn {_frozenTurn} enemy turn: {string.Join("; ", issues)}.");
        log.Warn($"[audit]   forecast: block {f.CurrentBlock} + end of turn {List(f.EndOfTurnBlock)}; blockable {List(f.Blockable)}; " +
                 $"unblockable {List(f.Unblockable)}; your end-of-turn damage {List(f.EndOfTurnDamage)}; " +
                 $"skipped (poison {f.SkippedPoisoned}, end of turn {f.SkippedEndOfTurn}), cancelled hits {f.CancelledHits}, Osty HP {f.OstyHp}.");
        var lines = new List<string>();
        foreach (var e in window)
        {
            if (e is DamageReceivedEntry or BlockGainedEntry)
            {
                log.Warn($"[audit]   actual: {e.HumanReadableString}");
                lines.Add(e.HumanReadableString);
            }
        }

        // One report per issue, grouped by what was wrong and the encounter.
        var reporter = Sts2Mods.Core.Diagnostics.ErrorReporter.Instance;
        if (reporter is not { Enabled: true }) return;
        var encounter = Sts2State.Combat?.Encounter?.GetType().Name ?? "unknown";
        var breakdown = $"block {f.CurrentBlock} + end of turn {List(f.EndOfTurnBlock)}\nblockable {List(f.Blockable)}\nunblockable {List(f.Unblockable)}\n" +
                        $"your end-of-turn damage {List(f.EndOfTurnDamage)}\nskipped: poison {f.SkippedPoisoned}, end of turn {f.SkippedEndOfTurn}; cancelled hits {f.CancelledHits}; Osty HP {f.OstyHp}";
        var prediction = Prediction(f);
        var fullWindow = string.Join("\n", window.Select((e, i) => $"#{from + i} {ProcAudit.Describe(e)}{(SideOf(e) == CombatSide.Enemy ? " (enemy turn)" : "")}"));
        foreach (var issue in issues)
        {
            var kind = issue.Split(':')[0];
            // Enemy outcome issues group by the monster too; amounts are left out of the fingerprint.
            var monster = enemyIssues.FirstOrDefault(x => x.Issue == issue).Monster;
            var fp = monster != null ? new[] { kind, encounter, monster } : new[] { kind, encounter };
            reporter.Mismatch("forecast", $"Forecast audit: {issue} ({encounter}, turn {_frozenTurn})", fp,
                $"Enemy turn after your turn {_frozenTurn}.\nIssue: {issue}\nAll issues: {string.Join("; ", issues)}\n\nForecast:\n{breakdown}\n\nSimulator, per enemy (over {f.Outcome.Branches.Count} branch(es){(f.Outcome.Truncated ? ", truncated" : "")}):\n{prediction}",
                new Dictionary<string, string> { ["audit_window"] = fullWindow, ["sim_state"] = f.State.ToJson() },
                new Dictionary<string, string> { ["check"] = kind, ["encounter"] = encounter, ["monster"] = monster ?? "" });
        }
    }

    /// <summary>
    /// The simulator's per-enemy predictions against what the history shows (see the class summary).
    /// Returns each issue with the monster it's about.
    /// </summary>
    private static List<(string Issue, string? Monster)> EnemyIssues(TurnEndModel f, List<CombatHistoryEntry> window, List<CombatHistoryEntry> endOfTurn, Player me, out string summary,
        out Dictionary<int, (int Hp, int Through, bool Died)> actuals)
    {
        var result = new List<(string, string?)>();
        actuals = new Dictionary<int, (int Hp, int Through, bool Died)>();
        var parts = new List<string>();
        summary = "";
        var o = f.Outcome;
        if (o.Branches.Count == 0) return result;
        foreach (var (creature, id) in f.Ids)
        {
            var start = f.State.Enemy(id);
            if (start == null || start.Hp <= 0) continue;
            var monster = creature.Monster?.GetType().Name ?? "enemy";
            var name = start.Name;

            // What happened: HP lost and instances through its block during your end of turn; whether it
            // died before its move; its first hit on you.
            // Up to the hit that kills it: an enemy that comes back (Waterfall Giant with Steam Eruption, at
            // 999999999 HP) takes later hits that aren't part of the simulated fight.
            var eot = endOfTurn.OfType<DamageReceivedEntry>().Where(d => d.Result.Receiver == creature).ToList();
            var killHit = eot.FindIndex(d => d.Result.WasTargetKilled);
            if (killHit >= 0) eot = eot.Take(killHit + 1).ToList();
            var hpAtTurnEnd = Math.Max(0, start.Hp - eot.Sum(d => d.Result.UnblockedDamage));
            var through = eot.Count(d => d.Result.UnblockedDamage > 0);
            var killAt = window.FindIndex(e => e is DamageReceivedEntry d && d.Result.Receiver == creature && d.Result.WasTargetKilled);
            var moveAt = window.FindIndex(e => e is MonsterPerformedMoveEntry m && ReferenceEquals(m.Monster, creature.Monster));
            // Killed during its own attack (your Thorns hits back before each hit lands, ThornsPower.BeforeDamageReceived;
            // the move is logged once it ends): its hit still lands, so it acted.
            var hitAfterKill = killAt >= 0 && window.Skip(killAt + 1).Take(moveAt > killAt ? moveAt - killAt - 1 : int.MaxValue)
                .Any(e => e is DamageReceivedEntry d && d.Dealer == creature);
            var diedBeforeActing = killAt >= 0 && (moveAt < 0 || killAt < moveAt) && !hitAfterKill;
            var firstHit = window.OfType<DamageReceivedEntry>().FirstOrDefault(d => d.Result.Receiver == me.Creature && d.Dealer == creature);
            string Range(int a, int b) => a == b ? $"{a}" : $"{a}-{b}";
            parts.Add($"{name} HP {start.Hp}→{hpAtTurnEnd} (pred {Range(o.HpAtTurnEndMin(id), o.HpAtTurnEndMax(id))}), through {through} (pred {Range(o.UnblockedMin(id), o.UnblockedMax(id))}), " +
                      $"died before acting {(diedBeforeActing ? "yes" : "no")} (pred {(o.DeadInAll(id) ? "always" : o.DeadInAny(id) ? "sometimes" : "never")})");

            actuals[id] = (hpAtTurnEnd, through, diedBeforeActing);
            if (o.DeadInAll(id) && !diedBeforeActing)
                result.Add(($"enemy death: {name} predicted dead before acting, but it lived to act (or outlived you)", monster));
            // Not every outcome explored: an unexplored one may be where it dies (DeadInAll already needs them all).
            if (!o.Truncated && !o.DeadInAny(id) && diedBeforeActing)
                result.Add(($"enemy death: {name} predicted alive, but it died before acting", monster));

            if (!o.Truncated)
            {
                int lo = o.HpAtTurnEndMin(id), hi = o.HpAtTurnEndMax(id);
                if (hpAtTurnEnd < lo || hpAtTurnEnd > hi)
                    result.Add(($"enemy HP at turn end: {name} predicted {(lo == hi ? $"{lo}" : $"{lo}-{hi}")}, actual {hpAtTurnEnd}", monster));
                int ulo = o.UnblockedMin(id), uhi = o.UnblockedMax(id);
                if (through < ulo || through > uhi)
                    result.Add(($"hits through block: {name} predicted {(ulo == uhi ? $"{ulo}" : $"{ulo}-{uhi}")}, actual {through}", monster));
            }

            if (start.Imbalanced && firstHit != null)
            {
                var blocked = firstHit.Result.WasFullyBlocked;
                if (o.FirstHitBlockedInAll(id) && !blocked)
                    result.Add(($"Imbalanced: {name}'s first hit predicted fully blocked, but it wasn't", monster));
                if (!o.Truncated && !o.FirstHitBlockedInAny(id) && blocked)
                    result.Add(($"Imbalanced: {name}'s first hit predicted not fully blocked, but it was", monster));
            }
        }
        summary = string.Join("; ", parts) + (o.Truncated ? " [truncated]" : "");
        return result;
    }

    /// <summary>Per enemy: the simulator's start state and predicted outcomes, for reports.</summary>
    private static string Prediction(TurnEndModel f)
    {
        var o = f.Outcome;
        var lines = new List<string>();
        foreach (var (_, id) in f.Ids)
        {
            var e = f.State.Enemy(id);
            if (e == null) continue;
            lines.Add($"{e.Name}: HP {e.Hp}, block {e.Block}, poison {e.Poison}, hits [{string.Join(", ", e.Hits)}]{(e.HitCap != int.MaxValue ? $", cap {e.HitCap}" : "")}" +
                      $" → HP at turn end {o.HpAtTurnEndMin(id)}-{o.HpAtTurnEndMax(id)}, hits through block {o.UnblockedMin(id)}-{o.UnblockedMax(id)}, " +
                      $"dead before acting {(o.DeadInAll(id) ? "always" : o.DeadInAny(id) ? "sometimes" : "never")}" +
                      $"{(e.Imbalanced ? $", first hit fully blocked {(o.FirstHitBlockedInAll(id) ? "always" : o.FirstHitBlockedInAny(id) ? "sometimes" : "never")}" : "")}");
        }
        var p = f.State.P;
        lines.Add($"you: block {p.Block}, orbs [{string.Join(", ", p.Orbs.Select(x => $"{x.Kind} {x.Passive}/{x.Evoke}"))}] (slots {p.OrbCapacity}), Juggernaut {p.Juggernaut}, Plating {p.Plating}, " +
                  $"hand {p.HandCount}, Ethereal exhausting {p.Fading}");
        return string.Join("\n", lines);
    }

    private static string List(IEnumerable<Source> sources)
    {
        var s = string.Join(", ", sources.Select(x => $"{x.Name} {x.Amount}"));
        return s.Length == 0 ? "none" : s;
    }
}
