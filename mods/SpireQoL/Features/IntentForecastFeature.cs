using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireQoL.Sim;
using SpireQoL.Cards;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Intent Forecast (decision #72): while you hover an enemy's intent, its next moves after that intent, stacked upward above it
/// (+1 nearest the intent).
/// Every turn its move rules make certain, then the first random pick with each move it can make and its chance; drawn with
/// the game's own intent nodes (NIntent: icon, animation, damage as things stand). Worked out from its move rules and the
/// moves it has made (IntentReader, IntentForecast); never from a roll. Nothing when its intent on screen is hidden.
/// Checked when each move is rolled (IntentForecastAudit). Spec: docs/FEATURES.md#intent-forecast.
/// </summary>
public sealed class IntentForecastFeature : IFeature, IFrameFeature
{
    private static readonly Color ChanceColor = new(1f, 0.9f, 0.6f);

    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private VBoxContainer? _rows;
    private Creature? _enemy;
    private string _key = "";
    /// <summary>The labels and power chips, each in a holder, moved every frame with the intents' bob.</summary>
    private readonly List<Control> _bobbing = new();
    /// <summary>
    /// Each row's spacers and what sits either side of its intents (the chance left, the power icons right): the narrower side
    /// gets a spacer, so the intents are centred over the intent on screen.
    /// </summary>
    private readonly List<(Control LeftSpacer, List<Control> Left, List<Control> Right, Control RightSpacer)> _balance = new();
    private const int RowGap = 6;
    /// <summary>Between a random pick's moves, side by side.</summary>
    private const int OptionGap = 20;
    /// <summary>Holders that only bob (a chance above its move), rather than sitting level with the intent icon beside them.</summary>
    private readonly HashSet<Control> _bobOnly = new();
    private float _bob;

    public IntentForecastFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "intent-forecast";
    public string DisplayName => "Intent Forecast";
    public bool EnabledByDefault => true;

    public static readonly WidgetLayout DefaultLayout = new()
    {
        FollowGame = true, OffsetX = 0, OffsetY = 0,
        Anchor = ScreenAnchor.Left,
    };

    public void Apply(Harmony harmony) => IntentForecastAudit.Patch(harmony);

    public void Attach(OverlayHost host)
    {
        _widget = _ctx.Widget(Id, "Intent Forecast", DefaultLayout);
        _widget.Frameless = true;
        // Above the intent, the later turns higher up; beside it when there's no room above (an enemy near the top).
        _widget.AttachTo(() => IntentRect(_enemy), AttachSide.Above)
               .AttachTo(() => IntentRect(_enemy), AttachSide.RightOfBottom);
        // No backdrop: the intents float like the game's own (the labels carry an outline instead).
        _rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _rows.AddThemeConstantOverride("separation", 2);
        _widget.Content.AddChild(_rows);
    }

    public void OnFrame(OverlayHost host)
    {
        var enemy = Sts2State.IsPlayerTurn ? HoveredIntentEnemy() : null;
        var forecast = enemy != null ? IntentReader.Forecast(enemy) : null;
        if (enemy == null || forecast == null)
        {
            _enemy = null;
            _key = "";
            _widget!.HasContent = false;
            return;
        }
        _enemy = enemy;
        var (turns, view) = forecast.Value;
        var key = enemy.GetHashCode() + "|" + string.Join("/", turns.Select(t => string.Join(",", t.Moves.Select(m => $"{m.Move}:{m.Chance:0.###}"))));
        if (key != _key)
        {
            _key = key;
            Build(enemy, turns, view);
        }
        _widget!.HasContent = true;
        // NIntent._Process's bob, for rows with no intent icon to follow.
        var y = -(Mathf.Sin(Time.GetTicksMsec() * 0.001f * Mathf.Pi + _bob) * 10f + 8f);
        // The narrower side of each row's intents gets a spacer (each shown child adds its width and a gap): the intents sit centred.
        foreach (var (leftSpacer, left, right, rightSpacer) in _balance)
        {
            if (!GodotObject.IsInstanceValid(leftSpacer) || !GodotObject.IsInstanceValid(rightSpacer)) continue;
            float Side(List<Control> xs) => xs.Where(GodotObject.IsInstanceValid).Sum(x => x.CustomMinimumSize.X + RowGap);
            var d = Side(right) - Side(left);
            Pad(leftSpacer, d);
            Pad(rightSpacer, -d);
        }
        foreach (var c in _bobbing)
        {
            if (!GodotObject.IsInstanceValid(c) || c.GetParent() is not Control holder) continue;
            // Its size, known once it's on screen (a label's font): the holder takes it, so the row lays out around it.
            var size = c.GetCombinedMinimumSize();
            if (holder.CustomMinimumSize != size) holder.CustomMinimumSize = size;
            c.Size = size;
            // Level with the intent icon in its row as it is drawn this frame (bob included); else centred, then the bob.
            var top = !_bobOnly.Contains(c) && IconCentre(holder) is { } centre ? centre - size.Y / 2 : (holder.Size.Y - size.Y) / 2 + y + 8f;
            c.Position = new Vector2(0, top);
        }
    }

    /// <summary>A spacer shown only when its side is short by <paramref name="by"/>, as wide as that less its own gap.</summary>
    private static void Pad(Control spacer, float by)
    {
        spacer.Visible = by > 0.5f; // a hidden child takes no gap
        var width = Math.Max(0, by - RowGap);
        if (spacer.Visible && !Mathf.IsEqualApprox(spacer.CustomMinimumSize.X, width)) spacer.CustomMinimumSize = new Vector2(width, 0);
    }

    /// <summary>The y, in the holder's own coordinates, of the centre of the intent icon drawn in its row (the NIntent's sprite).</summary>
    private static float? IconCentre(Control holder)
    {
        if (holder.GetParent()?.GetChildren().OfType<NIntent>().FirstOrDefault() is not { } intent) return null;
        if (intent.GetNodeOrNull<Node2D>("%Intent") is not { } sprite) return null;
        return (holder.GetGlobalTransform().AffineInverse() * sprite.GlobalPosition).Y;
    }

    /// <summary>A control in a plain holder of its size (rows are containers, which would undo the bob), added to the bobbing list.</summary>
    private Control Bobbing(Control inner, bool followIcon = true)
    {
        if (!followIcon) _bobOnly.Add(inner);
        inner.MouseFilter = Control.MouseFilterEnum.Ignore;
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = inner.GetCombinedMinimumSize() };
        holder.AddChild(inner);
        inner.Size = holder.CustomMinimumSize;
        _bobbing.Add(inner);
        return holder;
    }

    private static Label Outlined(Label l)
    {
        l.AddThemeConstantOverride("outline_size", 6);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        return l;
    }

    private void Build(Creature enemy, List<ForecastTurn> turns, MoveStateMachineView view)
    {
        UiKit.Clear(_rows!);
        _bobbing.Clear();
        _bobOnly.Clear();
        _balance.Clear();
        var targets = enemy.CombatState?.PlayerCreatures.ToList() ?? new List<Creature>();
        // The game's intents bob (NIntent._Process: a sine from _timeOffset); ours start in step with the intent on screen.
        var bob = _bob = BobOffset(enemy);
        for (var t = 0; t < turns.Count; t++)
        {
            var turn = turns[t];
            // One row per turn, each on top of the last, so the forecast reads upward from the intent (next turn just above it).
            var row = UiKit.Row(turn.Certain ? RowGap : OptionGap);
            row.Alignment = BoxContainer.AlignmentMode.Center;
            row.MouseFilter = Control.MouseFilterEnum.Ignore;
            // In the tree first: NIntent finds its parts in _Ready, and UpdateIntent before that throws.
            _rows!.AddChild(row);
            _rows.MoveChild(row, 0);
            if (turn.Certain)
            {
                // Its intents centred over the intent on screen, the power icons to their right (a spacer on the left balances them).
                var leftSpacer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
                var rightSpacer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
                row.AddChild(leftSpacer);
                var right = new List<Control>();
                AddMove(row, enemy, targets, bob, view, turn.Moves[0].Move, right);
                row.AddChild(rightSpacer);
                _balance.Add((leftSpacer, new List<Control>(), right, rightSpacer));
                continue;
            }
            // A random pick: its moves side by side, each with its chance above it.
            foreach (var (moveId, chance) in turn.Moves)
            {
                var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
                column.AddThemeConstantOverride("separation", 0);
                row.AddChild(column);
                var pct = Outlined(UiKit.Label($"{Math.Round(chance * 100)}%", 18, ChanceColor));
                pct.HorizontalAlignment = HorizontalAlignment.Center;
                var holder = Bobbing(pct, followIcon: false);
                holder.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
                column.AddChild(holder);
                var line = UiKit.Row(RowGap);
                line.Alignment = BoxContainer.AlignmentMode.Center;
                line.MouseFilter = Control.MouseFilterEnum.Ignore;
                column.AddChild(line);
                AddMove(line, enemy, targets, bob, view, moveId, new List<Control>());
            }
        }
    }

    /// <summary>A move's intents (the game's own nodes, bobbing in step with the intent on screen) and the powers it applies.</summary>
    private void AddMove(HBoxContainer line, Creature enemy, List<Creature> targets, float bob, MoveStateMachineView view, string moveId, List<Control> right)
    {
        if (view.Move(moveId) is not { } move) return;
        foreach (var intent in move.Intents)
        {
            var node = NIntent.Create(bob);
            node.MouseFilter = Control.MouseFilterEnum.Ignore;
            line.AddChild(node);
            if (node.IsNodeReady()) node.UpdateIntent(intent, targets, enemy);
            else node.Ready += () => node.UpdateIntent(intent, targets, enemy);
        }
        // The powers the move applies (Frail 2), from its code: a reminder of what a buff or debuff intent is.
        if (enemy.Monster is not { } monster) return;
        foreach (var applied in MovePowers.Of(monster, move))
        {
            var chip = Bobbing(PowerChip(applied));
            right.Add(chip);
            line.AddChild(chip);
        }
    }

    /// <summary>A power's icon with its amount beside it (none when the amount isn't a number the move keeps).</summary>
    private static Control PowerChip(MovePowers.Applied applied)
    {
        var chip = UiKit.Row(1);
        chip.MouseFilter = Control.MouseFilterEnum.Ignore;
        var icon = UiKit.Icon(GameIcons.Power(applied.Power), 30);
        icon.MouseFilter = Control.MouseFilterEnum.Ignore;
        chip.AddChild(icon);
        if (applied.Amount is { } a)
        {
            var amount = Outlined(UiKit.Label(a.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), 18, Colors.White));
            amount.VerticalAlignment = VerticalAlignment.Center;
            chip.AddChild(amount);
        }
        return chip;
    }

    /// <summary>The bob timing (NIntent._timeOffset) of the enemy's first intent on screen, or 0.</summary>
    private static float BobOffset(Creature enemy)
    {
        var node = NCombatRoom.Instance?.CreatureNodes.FirstOrDefault(n => GodotObject.IsInstanceValid(n) && n.Entity == enemy);
        var first = node?.IntentContainer?.GetChildren().OfType<NIntent>().FirstOrDefault();
        return first != null ? Sts2State.Private(first, "_timeOffset", 0f) : 0f;
    }

    /// <summary>The living enemy whose intent is under the mouse.</summary>
    private static Creature? HoveredIntentEnemy()
    {
        if (NCombatRoom.Instance is not { } room) return null;
        var mouse = room.GetViewport().GetMousePosition();
        foreach (var n in room.CreatureNodes)
        {
            if (!GodotObject.IsInstanceValid(n) || n.Entity is not { IsEnemy: true, IsAlive: true } e) continue;
            if (IntentRect(e) is { } r && r.Grow(6).HasPoint(mouse)) return e;
        }
        return null;
    }

    /// <summary>
    /// Screen rect of an enemy's intents on screen (the intent icons themselves, joined: the container doesn't always size to
    /// them), or null.
    /// </summary>
    private static Rect2? IntentRect(Creature? enemy)
    {
        if (enemy == null || NCombatRoom.Instance is not { } room) return null;
        var node = room.CreatureNodes.FirstOrDefault(n => GodotObject.IsInstanceValid(n) && n.Entity == enemy);
        var intents = node?.IntentContainer;
        if (intents == null || !GodotObject.IsInstanceValid(intents) || !intents.IsVisibleInTree()) return null;
        Rect2? rect = null;
        foreach (var i in intents.GetChildren().OfType<NIntent>())
        {
            if (!i.IsVisibleInTree() || i.Size.X < 1) continue;
            var r = Sts2State.ScreenRect(i);
            rect = rect is { } u ? u.Merge(r) : r;
        }
        if (rect == null && intents.Size.X >= 1) rect = Sts2State.ScreenRect(intents);
        return rect;
    }
}
