using Godot;
using HarmonyLib;
using SpireQoL.Combat;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Total blockable damage headed at you this round (enemy attacks plus Burn, Decay,
/// Constrict...), compared against current block + end-of-turn block. Unblockable HP loss is
/// listed separately. Spec: docs/FEATURES.md#incoming-damage.
/// </summary>
public sealed class IncomingDamageFeature : IFeature, IFrameFeature
{
    private static readonly Color Bad = new(1f, 0.45f, 0.4f);
    private static readonly Color Good = new(0.5f, 0.95f, 0.6f);

    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private Label? _headline;
    private Label? _result;
    private Label? _detail;
    private Label? _unblockable;

    public IncomingDamageFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "incoming-damage";
    public string DisplayName => "Incoming Damage";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
        _widget = _ctx.Widget(Id, "Incoming Damage", new WidgetLayout
        {
            Anchor = ScreenAnchor.Left, OffsetX = 40, OffsetY = 40,
        });
        _headline = UiKit.Label("", 22, Bad);
        _result = UiKit.Label("", 18, Colors.White);
        _detail = UiKit.Label("", 14, new Color(1f, 0.85f, 0.85f));
        _unblockable = UiKit.Label("", 14, new Color(0.85f, 0.6f, 1f));
        _widget.Content.AddChild(_headline);
        _widget.Content.AddChild(_result);
        _widget.Content.AddChild(_detail);
        _widget.Content.AddChild(_unblockable);
    }

    public void OnFrame(OverlayHost host)
    {
        var m = Sts2State.IsPlayerTurn ? TurnEndModel.Compute() : null;
        if (m == null || (m.BlockableTotal == 0 && m.UnblockableTotal == 0))
        {
            _widget!.HasContent = false;
            return;
        }

        _widget!.HasContent = true;
        _headline!.Text = $"Incoming {m.BlockableTotal}";

        var blockText = m.BlockGain > 0 ? $"{m.CurrentBlock}+{m.BlockGain}" : $"{m.CurrentBlock}";
        if (m.DamageThrough > 0)
        {
            _result!.Text = $"vs block {blockText}  →  take {m.DamageThrough}";
            _result.AddThemeColorOverride("font_color", Bad);
        }
        else
        {
            _result!.Text = m.Overblock > 0
                ? $"vs block {blockText}  →  fully blocked (+{m.Overblock} spare)"
                : $"vs block {blockText}  →  fully blocked";
            _result.AddThemeColorOverride("font_color", Good);
        }

        _detail!.Text = string.Join("  ·  ", m.Blockable.Select(s => $"{s.Name} {s.Amount}"));
        _unblockable!.Visible = m.UnblockableTotal > 0;
        _unblockable.Text = $"Unblockable HP loss {m.UnblockableTotal}: " + string.Join(", ", m.Unblockable.Select(s => $"{s.Name} {s.Amount}"));
    }
}
