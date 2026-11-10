using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Players;
using SpireQoL.Combat;
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
        if (actualTaken != f.Taken && !(f.RandomLeftOut && actualTaken < f.Taken)) issues.Add($"HP lost: forecast {f.Taken}, actual {actualTaken}");
        if (actualOsty != f.OstyTaken) issues.Add($"Osty HP lost: forecast {f.OstyTaken}, actual {actualOsty}");
        if (actualBlockGain != f.BlockGain) issues.Add($"end-of-turn block: forecast {f.BlockGain}, actual {actualBlockGain}");

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
        foreach (var issue in issues)
        {
            var kind = issue.Split(':')[0];
            reporter.Mismatch("forecast", $"Forecast audit: {issue} ({encounter}, turn {_frozenTurn})", new[] { kind, encounter },
                $"Enemy turn after your turn {_frozenTurn}.\nIssue: {issue}\nAll issues: {string.Join("; ", issues)}\n\nForecast:\n{breakdown}",
                new Dictionary<string, string> { ["audit_window"] = string.Join("\n", lines) });
        }
    }

    private static string List(IEnumerable<Source> sources)
    {
        var s = string.Join(", ", sources.Select(x => $"{x.Name} {x.Amount}"));
        return s.Length == 0 ? "none" : s;
    }
}
