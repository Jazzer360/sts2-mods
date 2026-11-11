using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireQoL.Features;

/// <summary>
/// A compact tray (default: above the hand) with one tick-marked bar per counter relic or
/// countdown power. Segments fill as the counter advances; the bar flashes when the next
/// qualifying action fires it. Counters that carry across turns sort first.
/// Spec: docs/FEATURES.md#relic-counters.
///  - Relics: RelicModel.ShowCounter / DisplayAmount / Status (Active = one step from firing).
///  - Powers: PowerModel.DisplayAmount is "steps left" for countdown powers (Panache, Orbit...).
/// </summary>
public sealed class RelicCountersFeature : IFeature, IFrameFeature
{
    private static readonly Color Fill = new(0.95f, 0.75f, 0.35f);
    private static readonly Color PowerFill = new(0.6f, 0.75f, 1f);
    private static readonly Color Empty = new(1f, 1f, 1f, 0.15f);
    private static readonly Color Ready = new(1f, 0.35f, 0.9f);
    private static readonly Color Warn = new(1f, 0.3f, 0.25f);
    private static readonly Color Ok = new(0.4f, 0.9f, 0.5f);

    /// <summary>Counters longer than this show only the countdown number, no bar.</summary>
    private const int MaxBarSteps = 12;

    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private readonly Dictionary<AbstractModel, Row> _rows = new(ReferenceEqualityComparer.Instance);

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

    private readonly record struct RowState(int Filled, int Total, bool Flash, Color Color, string Label);

    public void Attach(OverlayHost host)
    {
        // Right of the player's HP bar, bottom edges level, so it never sits on top of enemies.
        // Necrobinder: Osty stands right of you, so at the default placement it goes right of
        // Osty's HP bar instead (once moved by the player, it's always relative to yours).
        // Falls back to bottom-center above the hand when the HP bar isn't on screen.
        _widget = _ctx.Widget(Id, "Relic Counters", new WidgetLayout
        {
            FollowGame = true, OffsetX = 0, OffsetY = 0,
            Anchor = ScreenAnchor.Bottom,
        });
        _widget.AttachToWhenDefault(Sts2State.OstyHpBarRect, AttachSide.RightOfBottom)
               .AttachTo(Sts2State.PlayerHpBarRect, AttachSide.RightOfBottom);
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

        // (model, sort key, state)
        var entries = new List<(AbstractModel Model, int Sort, RowState State)>();

        foreach (var relic in me.Relics)
        {
            var spec = RelicSpecs.For(relic.GetType().Name);
            if (spec == null || !spec.InTray || !_ctx.Settings.TrackerEnabled(spec.TypeName, !spec.TurnTimer)) continue;
            if (Evaluate(relic, spec, me) is { } state)
            {
                entries.Add((relic, spec.CrossTurn ? 0 : 2, state));
            }
        }

        foreach (var (power, spec) in CountdownPowers(me))
        {
            if (!_ctx.Settings.TrackerEnabled(spec.TypeName)) continue;
            entries.Add((power, spec.CrossTurn ? 1 : 3, EvaluatePower(power, spec)));
        }

        var index = 0;
        foreach (var (model, _, state) in entries.OrderBy(e => e.Sort))
        {
            if (!_rows.TryGetValue(model, out var row))
            {
                row = MakeRow(model);
                _rows[model] = row;
                w.Content.AddChild(row.Root);
            }
            row.Seen = true;
            row.Root.Visible = true;
            w.Content.MoveChild(row.Root, index++);

            var fill = state.Color;
            if (state.Flash)
            {
                var p = UiKit.Pulse(host.Time, _ctx.Settings.FlashHz);
                fill = fill.Lerp(Ready, p);
                row.Root.Modulate = new Color(1, 1, 1, 0.65f + 0.35f * p);
            }
            else
            {
                row.Root.Modulate = Colors.White;
            }
            // Long counters (Cacophony's 33) read better as just the countdown number.
            var showBar = state.Total <= MaxBarSteps;
            row.Bar.Root.Visible = showBar;
            if (showBar) row.Bar.Set(state.Filled, state.Total, fill, Empty);
            row.Text.Text = state.Label;
            if (!showBar) row.Text.AddThemeColorOverride("font_color", fill);
        }

        foreach (var (model, row) in _rows.ToList())
        {
            if (row.Seen) continue;
            row.Root.Visible = false;
            row.Root.QueueFree();
            _rows.Remove(model);
        }

        w.HasContent = index > 0;
    }

    /// <summary>Countdown powers on you, plus ones on enemies that count your plays.</summary>
    private static IEnumerable<(PowerModel Power, PowerCounterSpec Spec)> CountdownPowers(Player me)
    {
        foreach (var p in me.Creature.Powers)
        {
            if (PowerSpecs.For(p.GetType().Name) is { OnEnemy: false } spec) yield return (p, spec);
        }
        var cs = Sts2State.Combat;
        if (cs == null) yield break;
        foreach (var enemy in cs.Enemies)
        {
            if (!enemy.IsAlive) continue;
            foreach (var p in enemy.Powers)
            {
                if (PowerSpecs.For(p.GetType().Name) is { OnEnemy: true } spec && (p.Target == null || p.Target == me.Creature))
                {
                    yield return (p, spec);
                }
            }
        }
    }

    private static RowState EvaluatePower(PowerModel power, PowerCounterSpec spec)
    {
        var left = Math.Clamp(power.DisplayAmount, 0, spec.Threshold);
        var filled = spec.Threshold - left;
        if (left == 0)
        {
            // Pale Blue Dot shows 0 once it has fired this turn.
            return new RowState(filled, spec.Threshold, false, Ok, "done");
        }
        var color = spec.OnEnemy ? Warn : PowerFill;
        return new RowState(filled, spec.Threshold, left == 1, color, $"{left} {spec.Unit} left");
    }

    private static Row MakeRow(AbstractModel model)
    {
        var root = UiKit.Row(6);
        Texture2D? icon = null;
        try
        {
            icon = model switch
            {
                RelicModel r => r.Icon,
                PowerModel p => p.Icon,
                _ => null,
            };
        }
        catch
        {
            // missing art: bar and text only
        }
        root.AddChild(UiKit.Icon(icon, 28));
        var bar = new TickBar(9, 12);
        root.AddChild(bar.Root);
        var text = UiKit.Label("", 15, Colors.White);
        root.AddChild(text);
        return new Row { Root = root, Bar = bar, Text = text };
    }

    /// <returns>null to hide the row.</returns>
    private RowState? Evaluate(RelicModel relic, RelicCounterSpec spec, Player me)
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
                return new RowState(n, threshold, active || oneEarly, Fill, $"{n}/{threshold}");
            }
            case CounterKind.Limit:
            {
                if (threshold <= 0) return null;
                var played = Sts2State.CountPlayedThisTurn(me);
                var left = Math.Max(0, threshold - played);
                return new RowState(played, threshold, left == 1, left <= 1 ? Warn : Fill, left == 0 ? "LIMIT" : $"{left} left");
            }
            case CounterKind.StayUnder:
            {
                if (threshold <= 0) return null;
                var played = Sts2State.CountPlayedThisTurn(me);
                var ok = played <= threshold;
                return new RowState(Math.Min(played, threshold), threshold, false, ok ? Ok : Warn, ok ? $"{played}/{threshold} ok" : "missed");
            }
            case CounterKind.OnTurn:
            {
                var turn = Sts2State.TurnNumber;
                if (threshold <= 0 || turn >= threshold) return null;
                return new RowState(turn, threshold, turn == threshold - 1, Fill, turn == threshold - 1 ? "next turn" : $"turn {threshold}");
            }
            case CounterKind.OneShot:
            {
                var armed = IsArmed(relic);
                return new RowState(armed ? 1 : 0, 1, false, armed ? Ok : Fill, armed ? "ready" : "used");
            }
            case CounterKind.KeepEnergy:
            {
                // Pael's Tears: end your turn with energy left → +N energy next turn.
                var energy = me.PlayerCombatState?.Energy ?? 0;
                var bonus = Sts2State.Var(relic, "Energy", 2);
                return energy > 0
                    ? new RowState(1, 1, false, Ok, $"+{bonus} next turn")
                    : new RowState(0, 1, false, Warn, "no energy left");
            }
        }
        return null;
    }

    /// <summary>Whether a one-shot relic is still unused. Most expose it as Status == Active.</summary>
    public static bool IsArmed(RelicModel relic)
    {
        return relic.GetType().Name switch
        {
            nameof(Permafrost) => !Sts2State.Private(relic, "ActivatedThisCombat", true),
            nameof(MusicBox) => !Sts2State.Private(relic, "WasUsedThisTurn", true),
            "PaelsLegion" => Sts2State.Private(relic, "Cooldown", 1) <= 0, // Status isn't Active at combat start
            _ => relic.Status == RelicStatus.Active,
        };
    }
}
