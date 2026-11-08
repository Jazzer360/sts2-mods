using Godot;
using HarmonyLib;
using SpireQoL.Combat;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// "+N at end of turn": block you'll gain automatically (Plating, Frost orbs, Orichalcum,
/// Cloak Clasp, Ripple Basin), so you stop overblocking. Spec: docs/FEATURES.md#block-forecast.
/// </summary>
public sealed class BlockForecastFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private Label? _headline;
    private Label? _detail;

    public BlockForecastFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "block-forecast";
    public string DisplayName => "Block Forecast";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
        _widget = _ctx.Widget(Id, "Block Forecast", new WidgetLayout
        {
            Anchor = ScreenAnchor.Left, OffsetX = 40, OffsetY = 140,
        });
        _headline = UiKit.Label("", 22, new Color(0.55f, 0.8f, 1f));
        _detail = UiKit.Label("", 14, new Color(0.85f, 0.9f, 1f));
        _widget.Content.AddChild(_headline);
        _widget.Content.AddChild(_detail);
    }

    public void OnFrame(OverlayHost host)
    {
        // When Incoming Damage is showing, it already includes end-of-turn block.
        var m = Sts2State.IsPlayerTurn && !_ctx.IncomingShown ? TurnEndModel.Compute() : null;
        if (m == null || (m.BlockGain == 0 && m.NextTurnBlock.Count == 0))
        {
            _widget!.HasContent = false;
            return;
        }

        _widget!.HasContent = true;
        _headline!.Text = m.BlockGain > 0
            ? $"Block {m.CurrentBlock}  +{m.BlockGain} at end of turn"
            : $"Block {m.CurrentBlock}";
        var parts = m.EndOfTurnBlock.Select(s => $"{s.Name} {s.Amount}")
            .Concat(m.NextTurnBlock.Select(s => $"next turn: {s.Name} {s.Amount}"));
        _detail!.Text = string.Join("  ·  ", parts);
    }
}
