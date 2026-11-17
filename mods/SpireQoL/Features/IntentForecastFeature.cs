using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireQoL.Sim;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Intent Forecast (decision #72): while you hover an enemy, its next moves after the intent on screen, beside that intent.
/// Every turn its move rules make certain, then the first random pick with each move it can make and its chance; drawn with
/// the game's own intent nodes (NIntent: icon, animation, damage as things stand). Worked out from its move rules and the
/// moves it has made (IntentReader, IntentForecast); never from a roll. Nothing when its intent on screen is hidden.
/// Checked when each move is rolled (IntentForecastAudit). Spec: docs/FEATURES.md#intent-forecast.
/// </summary>
public sealed class IntentForecastFeature : IFeature, IFrameFeature
{
    private static readonly Color TurnColor = new(0.75f, 0.75f, 0.8f);
    private static readonly Color ChanceColor = new(1f, 0.9f, 0.6f);

    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private VBoxContainer? _rows;
    private Creature? _enemy;
    private string _key = "";

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
        _widget.AttachTo(() => IntentRect(_enemy), AttachSide.RightOfBottom)
               .AttachTo(() => IntentRect(_enemy), AttachSide.LeftOf);
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", UiKit.PanelStyle(new Color(0.05f, 0.05f, 0.08f, 0.85f), radius: 6, margin: 8));
        _rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _rows.AddThemeConstantOverride("separation", 4);
        panel.AddChild(_rows);
        _widget.Content.AddChild(panel);
    }

    public void OnFrame(OverlayHost host)
    {
        var enemy = Sts2State.IsPlayerTurn ? FocusedEnemy() : null;
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
    }

    private void Build(Creature enemy, List<ForecastTurn> turns, MoveStateMachineView view)
    {
        UiKit.Clear(_rows!);
        var targets = enemy.CombatState?.PlayerCreatures.ToList() ?? new List<Creature>();
        for (var t = 0; t < turns.Count; t++)
        {
            var turn = turns[t];
            for (var i = 0; i < turn.Moves.Count; i++)
            {
                var (moveId, chance) = turn.Moves[i];
                var row = UiKit.Row(6);
                row.MouseFilter = Control.MouseFilterEnum.Ignore;
                // The turn it's for, on its first row: +1 is the turn after the intent on screen.
                var label = UiKit.Label(i == 0 ? $"+{t + 1}" : "", 16, TurnColor);
                label.CustomMinimumSize = new Vector2(24, 0);
                label.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(label);
                // In the tree first: NIntent finds its parts in _Ready, and UpdateIntent before that throws.
                _rows!.AddChild(row);
                if (view.Move(moveId) is { } move)
                {
                    foreach (var intent in move.Intents)
                    {
                        var node = NIntent.Create(0f);
                        node.MouseFilter = Control.MouseFilterEnum.Ignore;
                        row.AddChild(node);
                        if (node.IsNodeReady()) node.UpdateIntent(intent, targets, enemy);
                        else node.Ready += () => node.UpdateIntent(intent, targets, enemy);
                    }
                }
                if (!turn.Certain)
                {
                    var pct = UiKit.Label($"{Math.Round(chance * 100)}%", 18, ChanceColor);
                    pct.VerticalAlignment = VerticalAlignment.Center;
                    row.AddChild(pct);
                }
            }
        }
    }

    /// <summary>The enemy under the mouse (the game's hover focus on its creature node), alive.</summary>
    private static Creature? FocusedEnemy()
    {
        var room = NCombatRoom.Instance;
        if (room == null) return null;
        foreach (var n in room.CreatureNodes)
        {
            if (!GodotObject.IsInstanceValid(n) || !n.IsFocused) continue;
            if (n.Entity is { IsEnemy: true, IsAlive: true } e) return e;
        }
        return null;
    }

    /// <summary>Screen rect of an enemy's intents on screen, or null.</summary>
    private static Rect2? IntentRect(Creature? enemy)
    {
        if (enemy == null || NCombatRoom.Instance is not { } room) return null;
        var node = room.CreatureNodes.FirstOrDefault(n => GodotObject.IsInstanceValid(n) && n.Entity == enemy);
        var intents = node?.IntentContainer;
        if (intents == null || !GodotObject.IsInstanceValid(intents) || intents.Size.X < 1) return null;
        return Sts2State.ScreenRect(intents);
    }
}
