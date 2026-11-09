using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
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
/// end-of-turn effects) is frozen. When your next turn starts, the combat history entries recorded
/// in between (CombatManager.History: the game's own log of every damage instance) give the
/// actual HP you and Osty lost. Read-only: it only reads the history.
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

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        if (me == null || !Sts2State.InCombat)
        {
            _frozen = null;
            _wasPlayerTurn = false;
            return;
        }

        var history = CombatManager.Instance.History;
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

    private static void Compare(TurnEndModel f, CombatHistory history, int from, MegaCrit.Sts2.Core.Entities.Players.Player me)
    {
        var entries = history.Entries.Skip(from).ToList();
        var damage = entries.OfType<DamageReceivedEntry>().ToList();
        var mine = damage.Where(d => d.Result.Receiver == me.Creature).ToList();
        var osty = me.Osty;
        var ostyHits = osty == null ? new List<DamageReceivedEntry>() : damage.Where(d => d.Result.Receiver == osty).ToList();

        var actualTaken = mine.Sum(d => d.Result.UnblockedDamage);
        var actualOsty = ostyHits.Sum(d => d.Result.UnblockedDamage);
        var predictedTaken = f.DamageThrough + f.UnblockableTotal;

        if (actualTaken == predictedTaken && actualOsty == f.OstyTaken) return;

        var log = ModEntry.Log;
        log.Warn($"[audit] turn {Sts2State.TurnNumber - 1}: forecast took {predictedTaken} (Osty {f.OstyTaken}), actually took {actualTaken} (Osty {actualOsty}).");
        log.Warn($"[audit]   forecast: block {f.CurrentBlock} + end of turn {string.Join(", ", f.EndOfTurnBlock.Select(s => $"{s.Name} {s.Amount}"))}; " +
                 $"blockable {string.Join(", ", f.Blockable.Select(s => $"{s.Name} {s.Amount}"))}; " +
                 $"unblockable {string.Join(", ", f.Unblockable.Select(s => $"{s.Name} {s.Amount}"))}; " +
                 $"skipped (poison) {f.SkippedPoisoned}, cancelled hits {f.CancelledHits}, Osty HP {f.OstyHp}.");
        foreach (var e in entries)
        {
            if (e is DamageReceivedEntry or BlockGainedEntry) log.Warn($"[audit]   actual: {e.HumanReadableString}");
        }
    }
}
