using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// A compact tray (default: above the hand) with one tick-marked bar per counter relic.
/// Segments fill as the counter advances; the bar flashes when the next qualifying action
/// fires it (the game marks the relic Status = Active one step early). Counters that carry
/// across turns sort first. Spec: docs/FEATURES.md#relic-counters.
/// </summary>
public sealed class RelicCountersFeature : IFeature, IFrameFeature
{
    private static readonly Color Fill = new(0.95f, 0.75f, 0.35f);
    private static readonly Color Empty = new(1f, 1f, 1f, 0.15f);
    private static readonly Color Ready = new(1f, 0.35f, 0.9f);
    private static readonly Color Warn = new(1f, 0.3f, 0.25f);
    private static readonly Color Ok = new(0.4f, 0.9f, 0.5f);

    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private readonly Dictionary<RelicModel, Row> _rows = new(ReferenceEqualityComparer.Instance);

    public RelicCountersFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "relic-counters";
    public string DisplayName => "Relic Counters";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    private sealed class Row
    {
        public required HBoxContainer Root;
        public required TickBar Bar;
        public required Label Text;
        public bool Seen;
    }

    public void Attach(OverlayHost host)
    {
        _widget = _ctx.Widget(Id, "Relic Counters", new WidgetLayout
        {
            Anchor = ScreenAnchor.Bottom, OffsetX = 0, OffsetY = -300,
        });
    }

    public void OnFrame(OverlayHost host)
    {
        var w = _widget!;
        var me = Sts2State.Me;
        if (me == null)
        {
            w.HasContent = false;
            return;
        }

        foreach (var r in _rows.Values) r.Seen = false;

        var ordered = me.Relics
            .Select(r => (Relic: r, Spec: RelicSpecs.For(r.GetType().Name)))
            .Where(x => x.Spec != null && x.Spec.InTray && _ctx.Settings.RelicEnabled(x.Spec.TypeName))
            .OrderByDescending(x => x.Spec!.CrossTurn)
            .ThenBy(x => x.Spec!.Kind)
            .ToList();

        var index = 0;
        foreach (var (relic, spec) in ordered)
        {
            var state = Evaluate(relic, spec!, me);
            if (state == null) continue;

            if (!_rows.TryGetValue(relic, out var row))
            {
                row = MakeRow(relic);
                _rows[relic] = row;
                w.Content.AddChild(row.Root);
            }
            row.Seen = true;
            row.Root.Visible = true;
            w.Content.MoveChild(row.Root, index++);

            var (filled, total, flash, color, label) = state.Value;
            var fill = color;
            if (flash)
            {
                fill = fill.Lerp(Ready, UiKit.Pulse(host.Time, _ctx.Settings.FlashHz));
                row.Root.Modulate = new Color(1, 1, 1, 0.65f + 0.35f * UiKit.Pulse(host.Time, _ctx.Settings.FlashHz));
            }
            else
            {
                row.Root.Modulate = Colors.White;
            }
            row.Bar.Set(filled, total, fill, Empty);
            row.Text.Text = label;
        }

        foreach (var (relic, row) in _rows.ToList())
        {
            if (row.Seen) continue;
            row.Root.Visible = false;
            if (!me.Relics.Contains(relic))
            {
                row.Root.QueueFree();
                _rows.Remove(relic);
            }
        }

        w.HasContent = index > 0;
    }

    private static Row MakeRow(RelicModel relic)
    {
        var root = UiKit.Row(6);
        Texture2D? icon = null;
        try { icon = relic.Icon; } catch { /* missing art: text only */ }
        root.AddChild(UiKit.Icon(icon, 28));
        var bar = new TickBar(9, 12);
        root.AddChild(bar.Root);
        var text = UiKit.Label("", 15, Colors.White);
        root.AddChild(text);
        return new Row { Root = root, Bar = bar, Text = text };
    }

    /// <returns>null to hide the row.</returns>
    private (int Filled, int Total, bool Flash, Color Color, string Label)? Evaluate(RelicModel relic, RelicCounterSpec spec, MegaCrit.Sts2.Core.Entities.Players.Player me)
    {
        var active = relic.Status == RelicStatus.Active;
        var threshold = spec.ThresholdVar != null ? Sts2State.Var(relic, spec.ThresholdVar, spec.Fixed) : spec.Fixed;

        switch (spec.Kind)
        {
            case CounterKind.Counter:
            {
                if (!relic.ShowCounter && relic.DisplayAmount <= 0 && !active) return null;
                if (threshold <= 0) return null;
                var n = Math.Clamp(relic.DisplayAmount, 0, threshold);
                var oneEarly = _ctx.Settings.PulseOneStepEarly && n == threshold - 2;
                return (n, threshold, active || oneEarly, Fill, $"{n}/{threshold}");
            }
            case CounterKind.Limit:
            {
                if (threshold <= 0) return null;
                var played = Sts2State.CountPlayedThisTurn(me);
                var left = Math.Max(0, threshold - played);
                return (played, threshold, left == 1, left <= 1 ? Warn : Fill, left == 0 ? "LIMIT" : $"{left} left");
            }
            case CounterKind.StayUnder:
            {
                if (threshold <= 0) return null;
                var played = Sts2State.CountPlayedThisTurn(me);
                var ok = played <= threshold;
                return (Math.Min(played, threshold), threshold, false, ok ? Ok : Warn, ok ? $"{played}/{threshold} ok" : "missed");
            }
            case CounterKind.OnTurn:
            {
                var turn = Sts2State.TurnNumber;
                if (threshold <= 0 || turn >= threshold) return null;
                return (turn, threshold, turn == threshold - 1, Fill, turn == threshold - 1 ? "next turn" : $"turn {threshold}");
            }
            case CounterKind.OneShot:
            {
                var armed = IsArmed(relic, me);
                return (armed ? 1 : 0, 1, false, armed ? Ok : Fill, armed ? "ready" : "used");
            }
        }
        return null;
    }

    /// <summary>Whether a one-shot relic is still unused. Most expose it as Status == Active.</summary>
    public static bool IsArmed(RelicModel relic, MegaCrit.Sts2.Core.Entities.Players.Player me)
    {
        return relic.GetType().Name switch
        {
            "Permafrost" => !Sts2State.Private(relic, "ActivatedThisCombat", true),
            "MusicBox" => !Sts2State.Private(relic, "WasUsedThisTurn", true),
            _ => relic.Status == RelicStatus.Active,
        };
    }
}
