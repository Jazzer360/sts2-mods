using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
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

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        if (me == null || !Sts2State.InCombat)
        {
            Summarize();
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
            _frozen = _last;
            _frozenAt = _lastHistoryCount;
            _frozenTurn = _lastTurn;
        }
        _wasPlayerTurn = playerTurn;
    }

    private void Summarize()
    {
        if (_checked == 0) return;
        ModEntry.Log.Info($"[audit] combat summary: {_checked} enemy turns forecast, {_matched} matched.");
        _checked = _matched = 0;
    }

    /// <summary>The side whose turn an entry happened on (CombatHistoryEntry.CurrentSide is private).</summary>
    private static CombatSide SideOf(CombatHistoryEntry e) => Sts2State.Private(e, "CurrentSide", CombatSide.Player);

    private void Compare(TurnEndModel f, CombatHistory history, int from, Player me)
    {
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

        var osty = me.Osty;
        var damage = window.OfType<DamageReceivedEntry>().ToList();
        var actualTaken = damage.Where(d => d.Result.Receiver == me.Creature).Sum(d => d.Result.UnblockedDamage);
        var actualOsty = osty == null ? 0 : damage.Where(d => d.Result.Receiver == osty).Sum(d => d.Result.UnblockedDamage);
        var actualBlockGain = endOfTurn.OfType<BlockGainedEntry>().Where(b => b.Actor == me.Creature).Sum(b => b.Amount);

        var issues = new List<string>();
        // Less than forecast is expected when random-target end-of-turn damage was left out: it may
        // have killed an attacker (the forecast stays on the safe side on purpose).
        // The forecast is the worst branch; a random target can make it come in lower (down to TakenMin).
        if ((actualTaken > f.Taken || actualTaken < f.TakenMin)) issues.Add($"HP lost: forecast {f.Taken}, actual {actualTaken}");
        if ((actualOsty > f.OstyTaken || actualOsty < f.OstyTakenMin)) issues.Add($"Osty HP lost: forecast {f.OstyTaken}, actual {actualOsty}");
        if (actualBlockGain != f.BlockGain) issues.Add($"end-of-turn block: forecast {f.BlockGain}, actual {actualBlockGain}");
        var enemyIssues = EnemyIssues(f, window, endOfTurn, me, out var outcomeLine, out var actuals);
        issues.AddRange(enemyIssues.Select(x => x.Issue));
        // Each number above is checked against its own range over the branches; together they must
        // also come from one branch (one set of random targets explains everything that happened).
        var o = f.Outcome;
        if (issues.Count == 0 && !o.Truncated && o.Branches.Count > 1)
        {
            bool Explains(SimState b) =>
                b.Taken == actualTaken && (osty == null || b.OstyTaken == actualOsty)
                && actuals.All(kv => b.Enemy(kv.Key) is { } e && e.HpAtTurnEnd == kv.Value.Hp && e.UnblockedInstances == kv.Value.Through && e.DiedBeforeActing == kv.Value.Died);
            if (!o.Branches.Any(Explains))
                issues.Add($"no single branch: each result is possible on its own, but none of the {o.Branches.Count} branches gives them all (HP lost {actualTaken}; {outcomeLine})");
        }
        // Every enemy turn, matched or not: predicted vs actual per enemy, so patterns show before they're reports.
        ModEntry.Log.Info($"[audit] [outcome] turn {_frozenTurn}: HP lost {actualTaken} (predicted {(f.TakenMin == f.Taken ? $"{f.Taken}" : $"{f.TakenMin}-{f.Taken}")}); {outcomeLine}");

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
        var fullWindow = string.Join("\n", window.Select((e, i) => $"#{from + i} {ProcAudit.Describe(e, me)}{(SideOf(e) == CombatSide.Enemy ? " (enemy turn)" : "")}"));
        foreach (var issue in issues)
        {
            var kind = issue.Split(':')[0];
            // Enemy outcome issues group by the monster too; amounts are left out of the fingerprint.
            var monster = enemyIssues.FirstOrDefault(x => x.Issue == issue).Monster;
            var fp = monster != null ? new[] { kind, encounter, monster } : new[] { kind, encounter };
            reporter.Mismatch("forecast", $"Forecast audit: {issue} ({encounter}, turn {_frozenTurn})", fp,
                $"Enemy turn after your turn {_frozenTurn}.\nIssue: {issue}\nAll issues: {string.Join("; ", issues)}\n\nForecast:\n{breakdown}\n\nSimulator, per enemy (over {f.Outcome.Branches.Count} branch(es){(f.Outcome.Truncated ? ", truncated" : "")}):\n{prediction}",
                new Dictionary<string, string> { ["audit_window"] = fullWindow, ["sim_state"] = f.State.ToJson() });
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
            var eot = endOfTurn.OfType<DamageReceivedEntry>().Where(d => d.Result.Receiver == creature).ToList();
            var hpAtTurnEnd = Math.Max(0, start.Hp - eot.Sum(d => d.Result.UnblockedDamage));
            var through = eot.Count(d => d.Result.UnblockedDamage > 0);
            var killAt = window.FindIndex(e => e is DamageReceivedEntry d && d.Result.Receiver == creature && d.Result.WasTargetKilled);
            var moveAt = window.FindIndex(e => e is MonsterPerformedMoveEntry m && ReferenceEquals(m.Monster, creature.Monster));
            var diedBeforeActing = killAt >= 0 && (moveAt < 0 || killAt < moveAt);
            var firstHit = window.OfType<DamageReceivedEntry>().FirstOrDefault(d => d.Result.Receiver == me.Creature && d.Dealer == creature);
            string Range(int a, int b) => a == b ? $"{a}" : $"{a}-{b}";
            parts.Add($"{name} HP {start.Hp}→{hpAtTurnEnd} (pred {Range(o.HpAtTurnEndMin(id), o.HpAtTurnEndMax(id))}), through {through} (pred {Range(o.UnblockedMin(id), o.UnblockedMax(id))}), " +
                      $"died before acting {(diedBeforeActing ? "yes" : "no")} (pred {(o.DeadInAll(id) ? "always" : o.DeadInAny(id) ? "sometimes" : "never")})");

            actuals[id] = (hpAtTurnEnd, through, diedBeforeActing);
            if (o.DeadInAll(id) && !diedBeforeActing)
                result.Add(($"enemy death: {name} predicted dead before acting, but it acted", monster));
            if (!o.DeadInAny(id) && diedBeforeActing)
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
                if (!o.FirstHitBlockedInAny(id) && blocked)
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
