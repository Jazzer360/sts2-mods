using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SpireQoL.Cards;

/// <summary>
/// Icons for on-card tabs, all taken from the game's own art so indicators look native.
/// Lookups are cached; a missing icon returns null (the tab still shows, just without art).
/// </summary>
public static class GameIcons
{
    private static readonly Dictionary<Type, Texture2D?> Cache = new();
    private static Texture2D? _drawPile;

    public static Texture2D? Relic(RelicModel relic)
    {
        try { return relic.Icon; } catch { return null; }
    }

    public static Texture2D? Power(PowerModel power)
    {
        try { return power.Icon; } catch { return null; }
    }

    /// <summary>A power's icon by its type, from its canonical model (an enemy move's power: Intent Forecast).</summary>
    public static Texture2D? Power(Type powerType)
        => Cached(powerType, () => ModelDb.GetById<PowerModel>(ModelDb.GetId(powerType)).Icon);

    public static Texture2D? Enchantment(EnchantmentModel enchantment)
    {
        try { return enchantment.Icon; } catch { return null; }
    }

    /// <summary>A power's icon by type, from its canonical model (e.g. Echo Form, Master Planner).</summary>
    public static Texture2D? Power<T>() where T : PowerModel
        => Cached(typeof(T), () => ModelDb.Power<T>().Icon);

    /// <summary>An enchantment's icon by type (e.g. Steady for Retain, Goopy for added Exhaust).</summary>
    public static Texture2D? Enchantment<T>() where T : EnchantmentModel
        => Cached(typeof(T), () => ModelDb.Enchantment<T>().Icon);

    /// <summary>
    /// The discard pile button's icon (NCombatUi.DiscardPile → "Icon"), used for the shuffle tag:
    /// a reshuffle is the discard pile going back into the draw pile. Pile icons aren't
    /// referenced by path in code, only in the scene, so it's read from the live button.
    /// </summary>
    public static Texture2D? DiscardPile()
    {
        if (Valid(_discardPile)) return _discardPile;
        _discardPile = PileIcon(NCombatRoom.Instance?.Ui?.DiscardPile) ?? LoadTexture(DiscardPilePath);
        return _discardPile;
    }

    /// <summary>The draw pile button's icon (NCombatUi.DrawPile → "Icon").</summary>
    public static Texture2D? DrawPile()
    {
        if (Valid(_drawPile)) return _drawPile;
        _drawPile = PileIcon(NCombatRoom.Instance?.Ui?.DrawPile);
        return _drawPile;
    }

    public static Texture2D? Potion(PotionModel potion)
    {
        try { return potion.Image; } catch { return null; }
    }

    /// <summary>The skull from the game's emote wheel (the lethal tab).</summary>
    public static Texture2D? Skull()
    {
        if (Valid(_skull)) return _skull;
        _skull = LoadTexture("res://images/ui/emote/skull.png");
        return _skull;
    }

    private static Texture2D? _skull;

    /// <summary>
    /// The clock beside the run timer in the top bar (NTopBar.Timer, NRunTimer: its icon is a picture in its scene, so it's read
    /// from the live node). Null until the top bar exists; the time-limit power's icon then stands in.
    /// </summary>
    public static Texture2D? Clock()
    {
        if (Valid(_clock)) return _clock;
        try
        {
            var timer = MegaCrit.Sts2.Core.Nodes.NRun.Instance?.GlobalUi?.TopBar?.Timer;
            if (timer != null && GodotObject.IsInstanceValid(timer))
            {
                foreach (var node in Descendants(timer))
                    if (node is TextureRect { Texture: { } t }) return _clock = t;
            }
        }
        catch
        {
            // the stand-in below
        }
        return Power(typeof(MegaCrit.Sts2.Core.Models.Powers.BattlewornDummyTimeLimitPower));
    }

    private static Texture2D? _clock;

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>The head of the game's targeting arrow (NTargetingArrow: ui/combat/targeting_arrow_head.png): the aim marker.</summary>
    public static Texture2D? TargetArrow()
    {
        if (Valid(_targetArrow)) return _targetArrow;
        _targetArrow = LoadTexture("res://images/ui/combat/targeting_arrow_head.png");
        return _targetArrow;
    }

    private static Texture2D? _targetArrow;

    /// <summary>The heart from the top bar's HP display (packed/common_ui/top_bar/top_bar_heart.png): HP in the rest sum.</summary>
    public static Texture2D? Heart()
    {
        if (Valid(_heart)) return _heart;
        _heart = LoadTexture("res://images/packed/common_ui/top_bar/top_bar_heart.png");
        return _heart;
    }

    private static Texture2D? _heart;

    /// <summary>A rest-site option's own icon (RestSiteOption.Icon's path: ui/rest_site/option_&lt;id&gt;.png), e.g. "heal", "mend".</summary>
    public static Texture2D? RestOption(string id) =>
        LoadTexture(MegaCrit.Sts2.Core.Helpers.ImageHelper.GetImagePath($"ui/rest_site/option_{id}.png"));

    /// <summary>A run modifier's icon (Night Terrors), or null.</summary>
    public static Texture2D? Modifier(ModifierModel modifier)
    {
        try { return modifier.Icon; } catch { return null; }
    }

    private static Texture2D? _discardPile;

    /// <summary>The discard pile art outside combat (the game's own path, NetScreenTypeExtensions CardPile).</summary>
    private const string DiscardPilePath = "res://images/packed/combat_ui/discard_pile.png";

    /// <summary>
    /// The game's art at a path as a texture, or null. Some of it is imported as a plain image rather than a texture (the
    /// cursors: cursor_inspect.png is an .image resource), which is made into a texture here.
    /// </summary>
    internal static Texture2D? LoadTexture(string path)
    {
        try
        {
            if (!ResourceLoader.Exists(path)) return null;
            return ResourceLoader.Load(path) switch
            {
                Texture2D texture => texture,
                Image image => ImageTexture.CreateFromImage(image),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static Texture2D? PileIcon(Node? button)
    {
        try
        {
            var icon = button?.GetNodeOrNull<Control>("Icon");
            return icon switch
            {
                TextureRect tr => tr.Texture,
                null => null,
                _ => FirstTexture(icon),
            };
        }
        catch
        {
            return null;
        }
    }

    private static Texture2D? FirstTexture(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is TextureRect tr && tr.Texture != null) return tr.Texture;
            if (child is Sprite2D sp && sp.Texture != null) return sp.Texture;
            if (FirstTexture(child) is { } t) return t;
        }
        return null;
    }

    /// <summary>
    /// A cached texture can be freed under us: enchantment icons come from the game's
    /// PreloadManager cache, which is unloaded between rooms/acts. Using a freed one throws
    /// ObjectDisposedException (0.16.0 hit this ~47,000 times in one session, leaving tags
    /// half-drawn with a stale enchantment icon). So every cached texture is checked first.
    /// </summary>
    public static bool Valid(Texture2D? tex) => tex != null && GodotObject.IsInstanceValid(tex);

    private static Texture2D? Cached(Type key, Func<Texture2D?> load)
    {
        if (Cache.TryGetValue(key, out var tex) && Valid(tex)) return tex;
        try { tex = load(); } catch { tex = null; }
        Cache[key] = tex;
        return tex;
    }
}
