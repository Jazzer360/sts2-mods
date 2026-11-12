using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Cards;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// The stacked "math problem" Incoming Damage shows (yours, and a hovered teammate's):
///
///   [attack] 14
///   [defend] − 5
///   [osty]   − 3   (only while Osty is out)
///   ───────────
///          =  9
/// </summary>
internal sealed class DamageMathPanel
{
    private static readonly Color IncomingColor = new(1f, 0.6f, 0.5f);
    private static readonly Color BlockColor = new(0.55f, 0.8f, 1f);
    private static readonly Color TakeColor = new(1f, 0.35f, 0.3f);
    private static readonly Color SafeColor = new(0.5f, 0.95f, 0.6f);
    private static readonly Color OstyColor = new(0.75f, 0.85f, 0.7f);
    private const int FontSize = 22;
    private const float IconSize = 30;

    private readonly Label _incoming;
    private readonly Label _block;
    private readonly Label _result;
    private readonly TextureRect _attackIcon;
    private readonly TextureRect _ostyIcon;
    private readonly Label _osty;
    private readonly Texture2D?[] _attackTiers = new Texture2D?[5];

    public DamageMathPanel(Control parent)
    {
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

        parent.AddChild(grid);
    }

    /// <param name="osty">What Osty takes and whether that kills him; null when Osty isn't out.</param>
    public void Show(int incoming, int block, (int Taken, bool Dies)? osty, int take)
    {
        _attackIcon.Texture = _attackTiers[AttackTier(incoming) - 1] ?? _attackTiers.FirstOrDefault(t => t != null);
        _incoming.Text = $"{incoming}";
        _block.Text = $"− {block}";
        _ostyIcon.Visible = osty != null;
        _osty.Visible = osty != null;
        if (osty is { } o)
        {
            _osty.Text = $"− {o.Taken}";
            // Red when the enemy turn kills Osty (its overkill already counts toward yours).
            _osty.AddThemeColorOverride("font_color", o.Dies ? TakeColor : OstyColor);
        }
        _result.Text = $"{take}";
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
