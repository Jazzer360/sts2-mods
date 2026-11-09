using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Cards;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Checks the on-card tabs against what actually happened when you play a card, and logs every
/// mismatch to the game log (lines start with [SpireQoL] [audit]). This is how missing or wrong
/// proc rules surface: real plays go through the game's own systems, and we compare.
///
/// For each card you play (first play of its series, from the game's combat history):
///  - predicted: the card's tabs on the last frame before it was played (CardDecorFrame.Previous);
///  - actual, from that point until your next card play or the end of your turn:
///    - relics and powers that flashed (RelicModel.Flashed / PowerModel.Flashed events);
///    - block you gained from anything but the card itself (BlockGainedEntry without this card);
///    - damage dealt by anything but the card itself (DamageReceivedEntry with no card source),
///      split into damage to enemies and damage to you.
/// Logged: a source that flashed with no tab; a relic tab whose relic didn't flash; predicted proc
/// block that differs from actual; predicted proc damage with none dealt, or the reverse.
/// Read-only: it subscribes to events and reads history, nothing else.
/// </summary>
public sealed class ProcAudit : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;

    public ProcAudit(QolContext ctx) => _ctx = ctx;

    public string Id => "proc-audit";
    public string DisplayName => "Proc Audit (log only)";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
    }

    public void Attach(OverlayHost host)
    {
    }

    private sealed class Window
    {
        public required CardModel Card;
        public required string CardName;
        public required IReadOnlyList<TabSpec> Predicted;
        public required int From;
        public readonly List<AbstractModel> Flashed = new();
    }

    private Window? _open;
    private int _seen;                 // history entries already scanned
    private int _seenAtLastFrame;      // history size at the end of the previous frame
    private CombatHistory? _history;
    private readonly HashSet<AbstractModel> _subscribed = new(ReferenceEqualityComparer.Instance);

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        var cs = Sts2State.Combat;
        if (me == null || cs == null)
        {
            Close(null);
            _history = null;
            return;
        }

        var history = CombatManager.Instance.History;
        if (!ReferenceEquals(history, _history))
        {
            _history = history;
            _seen = _seenAtLastFrame = history.Entries.Count();
            _open = null;
        }

        Subscribe(me, cs);

        var count = history.Entries.Count(); // the history is list-backed: O(1)
        var i = _seen;
        foreach (var entry in count > _seen ? history.Entries.Skip(_seen) : Enumerable.Empty<CombatHistoryEntry>())
        {
            if (entry is CardPlayStartedEntry { CardPlay: var play } && play.Card.Owner == me && play.IsFirstInSeries)
            {
                Close(me);
                var predicted = _ctx.Decor.Previous.TryGetValue(play.Card, out var tabs) ? tabs : Array.Empty<TabSpec>();
                // Procs that fire before the play is recorded (BeforeCardPlayed: Intimidating
                // Helmet, Spirit of Ash...) land just before this entry, so the window starts at
                // the previous frame's history size.
                _open = new Window { Card = play.Card, CardName = SafeTitle(play.Card), Predicted = predicted, From = Math.Min(_seenAtLastFrame, i) };
            }
            i++;
        }
        _seen = count;

        if (_open != null && !Sts2State.IsPlayerTurn) Close(me);
        _seenAtLastFrame = count;
    }

    private void OnRelicFlashed(RelicModel relic, IEnumerable<Creature> _) => _open?.Flashed.Add(relic);

    private void OnPowerFlashed(PowerModel power) => _open?.Flashed.Add(power);

    private void Subscribe(Player me, ICombatState cs)
    {
        foreach (var r in me.Relics)
        {
            if (_subscribed.Add(r)) r.Flashed += OnRelicFlashed;
        }
        foreach (var c in cs.Creatures)
        {
            foreach (var p in c.Powers)
            {
                if (_subscribed.Add(p)) p.Flashed += OnPowerFlashed;
            }
        }
    }

    private void Close(Player? me)
    {
        var w = _open;
        _open = null;
        if (w == null || me == null || _history == null) return;

        try
        {
            Compare(w, me, _history.Entries.Skip(w.From).ToList());
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't check {w.CardName}: {ex.Message}");
        }
    }

    private static void Compare(Window w, Player me, List<CombatHistoryEntry> entries)
    {
        var log = ModEntry.Log;
        var issues = new List<string>();
        var predictedKeys = w.Predicted.Select(t => t.Key).ToHashSet();
        bool Predicted(AbstractModel m)
        {
            var n = m.GetType().Name;
            return predictedKeys.Contains("proc:" + n) || predictedKeys.Contains("relic:" + n) || predictedKeys.Contains("double-play:" + n);
        }

        // Sources that fired with no tab (missing rules). Powers flash for many reasons; only ones
        // on you or your enemies during your card play are listed.
        foreach (var m in w.Flashed.Distinct())
        {
            if (!Predicted(m)) issues.Add($"{m.GetType().Name} fired but had no tab");
        }

        // Relic tabs whose relic didn't flash. (Powers often don't flash, e.g. Feel No Pain, so
        // they're checked by block and damage below instead.)
        // Crossed relic tabs mean "this breaks it" (Art of War, Ripple Basin, Pael's Tears): no flash expected.
        foreach (var t in w.Predicted.Where(t => !t.Crossed && (t.Key.StartsWith("proc:") || t.Key.StartsWith("relic:"))))
        {
            var name = t.Key[(t.Key.IndexOf(':') + 1)..];
            if (me.Relics.Any(r => r.GetType().Name == name) && !w.Flashed.Any(f => f.GetType().Name == name))
            {
                issues.Add($"tab {t.Key} predicted, but the relic didn't flash");
            }
        }

        // Block from procs: every block gain to you that didn't come from this card itself.
        var procBlock = entries.OfType<BlockGainedEntry>()
            .Where(b => b.Actor == me.Creature && !ReferenceEquals(b.CardPlay?.Card, w.Card))
            .Sum(b => b.Amount);
        var predictedBlock = w.Predicted.Where(t => t.Kind == "block").Sum(t => t.Amount);
        if (procBlock != predictedBlock) issues.Add($"proc block predicted {predictedBlock}, actual {procBlock}");

        // Damage from procs (no card source): to enemies, and to you.
        var procDamage = entries.OfType<DamageReceivedEntry>().Where(d => d.CardSource == null).ToList();
        var toEnemies = procDamage.Where(d => d.Result.Receiver.IsEnemy).Sum(d => d.Result.TotalDamage);
        var toMe = procDamage.Where(d => d.Result.Receiver == me.Creature).Sum(d => d.Result.TotalDamage);
        var predictedHarm = w.Predicted.Where(t => t.Kind == "damage" && t.Crossed).Sum(t => t.Amount);
        var predictedHit = w.Predicted.Where(t => t.Kind == "damage" && !t.Crossed).Sum(t => t.Amount);
        // Hit numbers are per enemy (an all-enemy proc shows its per-enemy amount), so only
        // "predicted some, saw none" and the reverse are flagged; amounts are logged for reference.
        if ((predictedHit > 0) != (toEnemies > 0)) issues.Add($"proc damage to enemies predicted {predictedHit} (per enemy), actual {toEnemies} total");
        if (predictedHarm != toMe && (predictedHarm > 0 || toMe > 0)) issues.Add($"proc damage to you predicted {predictedHarm}, actual {toMe}");

        if (issues.Count == 0) return;
        log.Warn($"[audit] played {w.CardName}: {string.Join("; ", issues)}. Tabs were: {(w.Predicted.Count == 0 ? "none" : string.Join(", ", w.Predicted.Select(t => t.Amount > 0 ? $"{t.Key} {t.Amount}" : t.Key)))}.");
        foreach (var e in entries)
        {
            if (e is BlockGainedEntry or DamageReceivedEntry or CardPlayStartedEntry) log.Warn($"[audit]   {e.HumanReadableString}");
        }
    }

    private static string SafeTitle(CardModel c)
    {
        try { return c.Title; } catch { return c.GetType().Name; }
    }
}
