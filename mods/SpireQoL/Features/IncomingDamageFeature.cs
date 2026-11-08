using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using SpireQoL.Combat;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// A small stacked "math problem" just left of the player's block indicator:
///
///   [attack] 14
///   [defend] − 5
///   ───────────
///          =  9
///
/// Incoming is damage that will actually land: enemies Poison kills before acting are left out,
/// and hits after retaliation (Thorns etc.) kills an attacker are cancelled (TurnEndModel).
/// Block is your block at end of turn. The result adds unblockable HP loss (Regret, Demise...),
/// since that's what you actually lose. Spec: docs/FEATURES.md#incoming-damage.
/// </summary>
public sealed class IncomingDamageFeature : IFeature, IFrameFeature
{
    private static readonly Color IncomingColor = new(1f, 0.6f, 0.5f);
    private static readonly Color BlockColor = new(0.55f, 0.8f, 1f);
    private static readonly Color TakeColor = new(1f, 0.35f, 0.3f);
    private static readonly Color SafeColor = new(0.5f, 0.95f, 0.6f);
    private const int FontSize = 22;
    private const float IconSize = 30;

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
        FollowGame = true, OffsetX = 0, OffsetY = 0,
        Anchor = ScreenAnchor.Left, // fallback when the health bar isn't on screen
    };

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
        _widget = _ctx.Widget(Id, "Incoming Damage", DefaultLayout);
        // Left of the game's block indicator (it's about block). When the character stands too
        // close to the screen edge for that, fall back to below the HP bar and every row of
        // power icons (they can wrap to several rows).
        _widget.AttachTo(Sts2State.PlayerBlockRect, AttachSide.LeftOf)
               .AttachTo(Sts2State.PlayerStatusRect, AttachSide.Below);

        var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 2);

        grid.AddChild(UiKit.Icon(LoadIntentIcon("intent_attack"), IconSize));
        _incoming = Number(IncomingColor);
        grid.AddChild(_incoming);

        grid.AddChild(UiKit.Icon(LoadIntentIcon("intent_defend"), IconSize));
        _block = Number(BlockColor);
        grid.AddChild(_block);

        grid.AddChild(Rule());
        grid.AddChild(Rule());

        var eq = UiKit.Label("=", FontSize, Colors.White);
        eq.HorizontalAlignment = HorizontalAlignment.Center;
        eq.CustomMinimumSize = new Vector2(IconSize, 0);
        grid.AddChild(eq);
        _result = Number(TakeColor);
        grid.AddChild(_result);

        _widget.Content.AddChild(grid);
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
        _incoming!.Text = $"{m.BlockableTotal + m.UnblockableTotal}";
        _block!.Text = $"− {m.ExpectedBlock}";
        _result!.Text = $"{take}";
        _result.AddThemeColorOverride("font_color", take > 0 ? TakeColor : SafeColor);
    }

    private static Label Number(Color color)
    {
        var l = UiKit.Label("", FontSize, color);
        l.HorizontalAlignment = HorizontalAlignment.Right;
        l.CustomMinimumSize = new Vector2(48, 0);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return l;
    }

    private static ColorRect Rule() => new()
    {
        Color = new Color(1, 1, 1, 0.6f),
        CustomMinimumSize = new Vector2(0, 2),
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>The game's own intent icons (atlases/intent_atlas.sprites/*.tres).</summary>
    private static Texture2D? LoadIntentIcon(string name)
    {
        try
        {
            return ResourceLoader.Load<Texture2D>(ImageHelper.GetImagePath($"atlases/intent_atlas.sprites/{name}.tres"));
        }
        catch
        {
            return null;
        }
    }
}
