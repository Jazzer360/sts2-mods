using Godot;
using HarmonyLib;
using SpireQoL.Combat;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// One compact line under the player's HP: incoming damage, your block at end of turn, and
/// what you'll actually lose. Incoming counts enemy attacks plus blockable self damage (Burn,
/// Decay, Infection, Constrict...). "Take" adds unblockable HP loss (Regret, Demise...), since
/// that's the real end result. Spec: docs/FEATURES.md#incoming-damage.
/// </summary>
public sealed class IncomingDamageFeature : IFeature, IFrameFeature
{
    private static readonly Color IncomingColor = new(1f, 0.6f, 0.5f);
    private static readonly Color BlockColor = new(0.55f, 0.8f, 1f);
    private static readonly Color TakeColor = new(1f, 0.35f, 0.3f);
    private static readonly Color SafeColor = new(0.5f, 0.95f, 0.6f);

    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private Label? _incoming;
    private Label? _block;
    private Label? _result;

    public IncomingDamageFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "incoming-damage";
    public string DisplayName => "Incoming Damage";
    public bool EnabledByDefault => true;

    public static readonly WidgetLayout DefaultLayout = new()
    {
        FollowGame = true, OffsetX = 0, OffsetY = 6,
        Anchor = ScreenAnchor.Left, // fallback when the health bar isn't on screen
    };

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
        _widget = _ctx.Widget(Id, "Incoming Damage", DefaultLayout);
        _widget.FollowTarget = Sts2State.PlayerStatusRect;

        var row = UiKit.Row(14);
        _incoming = UiKit.Label("", 20, IncomingColor);
        _block = UiKit.Label("", 20, BlockColor);
        _result = UiKit.Label("", 20, TakeColor);
        row.AddChild(_incoming);
        row.AddChild(_block);
        row.AddChild(_result);
        _widget.Content.AddChild(row);
    }

    public void OnFrame(OverlayHost host)
    {
        _ctx.IncomingShown = false;
        var m = Sts2State.IsPlayerTurn ? TurnEndModel.Compute() : null;
        if (m == null || (m.BlockableTotal == 0 && m.UnblockableTotal == 0))
        {
            _widget!.HasContent = false;
            return;
        }

        _widget!.HasContent = true;
        _ctx.IncomingShown = true;

        var take = m.DamageThrough + m.UnblockableTotal;
        _incoming!.Text = $"Incoming {m.BlockableTotal + m.UnblockableTotal}";
        _block!.Text = $"Block {m.ExpectedBlock}";
        _result!.Text = take > 0 ? $"Take {take}" : "No damage";
        _result.AddThemeColorOverride("font_color", take > 0 ? TakeColor : SafeColor);
    }
}
