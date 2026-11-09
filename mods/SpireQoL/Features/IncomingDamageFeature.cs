using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using SpireQoL.Combat;
using SpireQoL.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
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
    private TextureRect? _attackIcon;
    private readonly Texture2D?[] _attackTiers = new Texture2D?[5];

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

        for (var tier = 1; tier <= 5; tier++)
        {
            _attackTiers[tier - 1] = LoadIntentIcon($"attack/intent_attack_{tier}");
        }
        _attackIcon = UiKit.Icon(_attackTiers[0], IconSize);
        grid.AddChild(_attackIcon);
        _incoming = Number(IncomingColor);
        grid.AddChild(_incoming);

        grid.AddChild(UiKit.Icon(LoadIntentIcon("intent_defend"), IconSize));
        _block = Number(BlockColor);
        grid.AddChild(_block);

        // Necrobinder: what Osty absorbs (Die For You), shown only while Osty is out.
        _ostyIcon = UiKit.Icon(GameIcons.Power<DieForYouPower>(), IconSize);
        grid.AddChild(_ostyIcon);
        _osty = Number(OstyColor);
        grid.AddChild(_osty);

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
        var m = Sts2State.IsPlayerTurn ? TurnEndModel.Compute() : null;
        if (m == null || (m.BlockableTotal == 0 && m.UnblockableTotal == 0))
        {
            _widget!.HasContent = false;
            return;
        }

        _widget!.HasContent = true;

        var take = m.Taken;
        var incoming = m.BlockableTotal + m.UnblockableTotal;
        _attackIcon!.Texture = _attackTiers[AttackTier(incoming) - 1] ?? _attackTiers.FirstOrDefault(t => t != null);
        _incoming!.Text = $"{incoming}";
        _block!.Text = $"− {m.ExpectedBlock}";
        var ostyOut = m.OstyHp > 0;
        _ostyIcon!.Visible = ostyOut;
        _osty!.Visible = ostyOut;
        if (ostyOut)
        {
            _osty.Text = $"− {m.OstyTaken}";
            // Red when the enemy turn kills Osty (its overkill already counts toward yours).
            _osty.AddThemeColorOverride("font_color", m.OstyDies ? TakeColor : OstyColor);
        }
        _result!.Text = $"{take}";
        _result.AddThemeColorOverride("font_color", take > 0 ? TakeColor : SafeColor);
    }

    private TextureRect? _ostyIcon;
    private Label? _osty;
    private static readonly Color OstyColor = new(0.75f, 0.85f, 0.7f);

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

    /// <summary>Same tiers the game uses for an enemy's attack intent icon (AttackIntent.GetTexture).</summary>
    private static int AttackTier(int damage) => damage switch
    {
        < 5 => 1,
        < 10 => 2,
        < 20 => 3,
        < 40 => 4,
        _ => 5,
    };

    /// <summary>
    /// The game's own intent icons (atlases/intent_atlas.sprites/*.tres). Attack uses the tiered
    /// attack/intent_attack_N icons; the plain intent_attack.tres isn't shipped (shows a placeholder).
    /// </summary>
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
