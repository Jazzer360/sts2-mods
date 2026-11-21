using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireQoL.Cards;
using SpireQoL.Sim;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// A small clock (the run timer's, from the top bar) and progress bar under the End Turn button while predictions are still exploring their outcomes in the
/// background (decision #89): how far along they are together. Hidden the rest of the time, which is nearly always: most
/// fights finish within the frame. Anything not yet certain carries a ? where it's shown (decision #88).
/// Spec: docs/FEATURES.md#exploration-progress.
/// </summary>
public sealed class ExplorationProgressFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private ProgressBar? _bar;
    private TextureRect? _clock;

    public ExplorationProgressFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "exploration-progress";
    public string DisplayName => "Exploration Progress";
    public bool EnabledByDefault => true;

    public static readonly WidgetLayout DefaultLayout = new()
    {
        FollowGame = true, OffsetX = 0, OffsetY = 0,
        Anchor = ScreenAnchor.BottomRight, // fallback when the End Turn button isn't on screen
    };

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
        _widget = _ctx.Widget(Id, "Exploration Progress", DefaultLayout);
        _widget.AttachTo(EndTurnRect, AttachSide.Below);
        var row = UiKit.Row(6);
        _clock = UiKit.Icon(GameIcons.Clock(), 22);
        row.AddChild(_clock);
        _bar = Bar();
        row.AddChild(_bar);
        _widget.Content.AddChild(row);
    }

    public void OnFrame(OverlayHost host)
    {
        var (count, progress) = Sts2State.IsPlayerTurn ? SimJob.Progress() : (0, 1);
        _widget!.HasContent = count > 0;
        if (count == 0) return;
        _bar!.Value = progress;
    }

    /// <summary>The End Turn button (NCombatUi.EndTurnButton) on screen.</summary>
    private static Rect2? EndTurnRect()
    {
        try
        {
            return NCombatRoom.Instance?.Ui?.EndTurnButton is { } b && GodotObject.IsInstanceValid(b) && b.IsVisibleInTree() ? b.GetGlobalRect() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A thin bar, a soft white fill on a dark track (none of the game's glow or cost colors).</summary>
    private static ProgressBar Bar()
    {
        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = false,
            CustomMinimumSize = new Vector2(90, 8),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var track = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.6f) };
        track.SetCornerRadiusAll(3);
        var fill = new StyleBoxFlat { BgColor = new Color(0.9f, 0.9f, 0.85f, 0.85f) };
        fill.SetCornerRadiusAll(3);
        bar.AddThemeStyleboxOverride("background", track);
        bar.AddThemeStyleboxOverride("fill", fill);
        return bar;
    }
}
