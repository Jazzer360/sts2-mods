using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
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
        /// <summary>A card's row (Make It So): the game's tiny card as its icon, fitted each frame.</summary>
        public Cards.TinyCardIcon? Tiny;
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

    private readonly List<(AbstractModel Model, int Sort, RowState State, int Order)> _entries = new();
    private readonly List<AbstractModel> _gone = new();
    private static readonly Comparison<(AbstractModel Model, int Sort, RowState State, int Order)> ByKey =
        (left, right) => left.Sort != right.Sort ? left.Sort.CompareTo(right.Sort) : left.Order.CompareTo(right.Order);

    public void OnFrame(OverlayHost host)
    {
        var widget = _widget!;
        var me = Sts2State.Me;
        if (me == null)
        {
            widget.HasContent = false;
            return;
        }

        foreach (var row in _rows.Values) row.Seen = false;

        // (model, sort key, state): kept between frames, filled again each one
        var entries = _entries;
        entries.Clear();

        foreach (var relic in me.Relics)
        {
            var spec = RelicSpecs.For(relic.GetType().Name);
            if (spec == null || !spec.InTray || !_ctx.Settings.TrackerEnabled(spec.TypeName, !spec.TurnTimer)) continue;
            if (Evaluate(relic, spec, me) is { } state)
            {
                entries.Add((relic, spec.CrossTurn ? 0 : 2, state, 0));
            }
        }

        foreach (var (power, spec) in CountdownPowers(me))
        {
            if (!_ctx.Settings.TrackerEnabled(spec.TypeName)) continue;
            entries.Add((power, spec.CrossTurn ? 1 : 3, EvaluatePower(power, spec), 0));
        }

        // Make It So (a card, decision #42): your Skills this turn toward its next return.
        if (Sts2State.InCombat && _ctx.Settings.TrackerEnabled(Procs.ReturnToHand.MakeItSo) && MakeItSo(me) is { } mis) entries.Add((mis.Card, 3, mis.State, 0));

        // By sort key, in the order they were found within one (a stable sort: List.Sort isn't, so the order found breaks ties).
        for (var i = 0; i < entries.Count; i++) entries[i] = entries[i] with { Order = i };
        entries.Sort(ByKey);
        var index = 0;
        foreach (var (model, _, state, _) in entries)
        {
            if (!_rows.TryGetValue(model, out var row))
            {
                // Timed on its own ("relic-counters-row"): a new row loads its icon, which can be slow the first time.
                var start = Godot.Time.GetTicksUsec();
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                row = MakeRow(model);
                host.AddPartTime("relic-counters-row", (Godot.Time.GetTicksUsec() - start) / 1000.0, (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / 1024.0);
                _rows[model] = row;
                widget.Content.AddChild(row.Root);
            }
            row.Seen = true;
            row.Root.Visible = true;
            row.Tiny?.Fit();
            widget.Content.MoveChild(row.Root, index++);

            Paint(row, state, OverlayHost.Time, _ctx.Settings.FlashHz);
        }

        _gone.Clear();
        foreach (var (model, row) in _rows)
            if (!row.Seen) _gone.Add(model);
        foreach (var model in _gone)
        {
            _rows[model].Root.Visible = false;
            _rows[model].Root.QueueFree();
            _rows.Remove(model);
        }

        widget.HasContent = index > 0;
    }

    private static void Paint(Row row, RowState state, float time, float frequency)
    {
        var fill = state.Color;
        if (state.Flash)
        {
            var pulse = UiKit.Pulse(time, frequency);
            fill = fill.Lerp(Ready, pulse);
            row.Root.Modulate = new Color(1, 1, 1, 0.65f + 0.35f * pulse);
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
        if (!showBar) row.Text.AddThemeColorOverride(GodotNames.Name("font_color"), fill);
    }

    /// <summary>The bar colors, by what a row counts (the Guide page names them).</summary>
    internal enum Shade { Relic, Power, Warning, Good }

    /// <summary>
    /// A tray row as it looks in a fight, for the settings menu's Guide page: <paramref name="paint"/> redraws it (call it every
    /// frame, so a flashing row flashes at the tray's speed).
    /// </summary>
    internal static Control ExampleRow(AbstractModel model, int filled, int total, Shade shade, string label, bool flash, out Action<float, float> paint)
    {
        var row = MakeRow(model);
        var color = shade switch { Shade.Power => PowerFill, Shade.Warning => Warn, Shade.Good => Ok, _ => Fill };
        var state = new RowState(filled, total, flash, color, label);
        paint = (time, frequency) =>
        {
            if (!GodotObject.IsInstanceValid(row.Root)) return;
            row.Tiny?.Fit();
            Paint(row, state, time, frequency);
        };
        paint(0, 1);
        return row.Root;
    }

    /// <summary>Countdown powers on you, plus ones on enemies that count your plays.</summary>
    private static IEnumerable<(PowerModel Power, PowerCounterSpec Spec)> CountdownPowers(Player me)
    {
        foreach (var power in me.Creature.Powers)
        {
            if (PowerSpecs.For(power.GetType().Name) is { OnEnemy: false } spec) yield return (power, spec);
        }
        var combat = Sts2State.Combat;
        if (combat == null) yield break;
        foreach (var enemy in combat.Enemies)
        {
            if (!enemy.IsAlive) continue;
            foreach (var power in enemy.Powers)
            {
                if (PowerSpecs.For(power.GetType().Name) is { OnEnemy: true } spec && (power.Target == null || power.Target == me.Creature))
                {
                    yield return (power, spec);
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
            return new RowState(filled, spec.Threshold, Flash: false, Ok, "done");
        }
        var color = spec.OnEnemy ? Warn : PowerFill;
        return new RowState(filled, spec.Threshold, left == 1, color, $"{left} {spec.Unit} left");
    }

    /// <summary>
    /// Make It So in your fight's piles (an Attack Potion's copy too): Skills played this turn toward the next multiple
    /// of its count (MakeItSo.AfterCardPlayedLate, Procs.ReturnToHand). Flashes when your next Skill brings a copy back.
    /// </summary>
    private static (CardModel Card, RowState State)? MakeItSo(Player me)
    {
        var copies = Procs.ReturnToHand.Copies(me, Procs.ReturnToHand.MakeItSo).ToList();
        if (copies.Count == 0) return null;
        var every = Procs.ReturnToHand.Every(copies[0].Card);
        var filled = Procs.ReturnToHand.SkillsThisTurn(me) % every;
        var outside = copies.Count(copy => copy.Pile != PileType.Hand);
        var next = filled == every - 1;
        var label = outside == 0 ? $"{filled}/{every} Skills (in hand)" : next ? $"{filled}/{every} Skills: back on the next" : $"{filled}/{every} Skills";
        return (copies[0].Card, new RowState(filled, every, outside > 0 && next, PowerFill, label));
    }

    private static Row MakeRow(AbstractModel model)
    {
        var root = UiKit.Row(separation: 6);
        if (model is CardModel card)
        {
            var tiny = new Cards.TinyCardIcon(card, size: 28);
            root.AddChild(tiny.Root);
            var cardBar = new TickBar(segWidth: 9, height: 12);
            root.AddChild(cardBar.Root);
            var cardText = UiKit.Label("", size: 15, Colors.White);
            root.AddChild(cardText);
            return new Row { Root = root, Bar = cardBar, Text = cardText, Tiny = tiny };
        }
        Texture2D? icon = null;
        try
        {
            icon = model switch
            {
                RelicModel relic => relic.Icon,
                PowerModel power => power.Icon,
                _ => null,
            };
        }
        catch
        {
            // missing art: bar and text only
        }
        root.AddChild(UiKit.Icon(icon, size: 28));
        var bar = new TickBar(segWidth: 9, height: 12);
        root.AddChild(bar.Root);
        var text = UiKit.Label("", size: 15, Colors.White);
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
                // Hidden when the game hides the relic's counter: done for the fight (Metronome after it
                // fires) or the turn (Brilliant Scarf), or not counting yet.
                if (!relic.ShowCounter && !active) return null;
                if (threshold <= 0) return null;
                var count = Math.Clamp(relic.DisplayAmount, 0, threshold);
                var oneEarly = _ctx.Settings.PulseOneStepEarly && count == threshold - 2;
                return new RowState(count, threshold, active || oneEarly, Fill, $"{count}/{threshold}");
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
                return new RowState(Math.Min(played, threshold), threshold, Flash: false, ok ? Ok : Warn, ok ? $"{played}/{threshold} ok" : "missed");
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
                return new RowState(armed ? 1 : 0, Total: 1, Flash: false, armed ? Ok : Fill, armed ? "ready" : "used");
            }
            case CounterKind.KeepEnergy:
            {
                // Pael's Tears: end your turn with energy left → +N energy next turn.
                var energy = me.PlayerCombatState?.Energy ?? 0;
                var bonus = Sts2State.Var(relic, "Energy", fallback: 2);
                return energy > 0
                    ? new RowState(Filled: 1, Total: 1, Flash: false, Ok, $"+{bonus} next turn")
                    : new RowState(Filled: 0, Total: 1, Flash: false, Warn, "no energy left");
            }
        }
        return null;
    }

    /// <summary>Whether a one-shot relic is still unused. Most expose it as Status == Active.</summary>
    public static bool IsArmed(RelicModel relic)
    {
        return relic.GetType().Name switch
        {
            nameof(Permafrost) => !Sts2State.Private(relic, "ActivatedThisCombat", fallback: true),
            nameof(MusicBox) => !Sts2State.Private(relic, "WasUsedThisTurn", fallback: true),
            nameof(PaelsLegion) => Sts2State.Private(relic, "Cooldown", fallback: 1) <= 0, // Status isn't Active at combat start
            _ => relic.Status == RelicStatus.Active,
        };
    }
}
