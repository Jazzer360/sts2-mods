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
    /// The clock beside the run timer in the top bar (packed/common_ui/top_bar/timer_icon.png, the picture NRunTimer's scene uses):
    /// loaded by its path, so it's the same on the main menu as in a run. The time-limit power's icon stands in if it can't load.
    /// </summary>
    public static Texture2D? Clock()
    {
        if (Valid(_clock)) return _clock;
        _clock = LoadTexture("res://images/packed/common_ui/top_bar/timer_icon.png");
        return Valid(_clock) ? _clock : Power<MegaCrit.Sts2.Core.Models.Powers.BattlewornDummyTimeLimitPower>();
    }

    private static Texture2D? _clock;

    /// <summary>The head of the game's targeting arrow (NTargetingArrow: ui/combat/targeting_arrow_head.png): the aim marker.</summary>
    public static Texture2D? TargetArrow()
    {
        if (Valid(_targetArrow)) return _targetArrow;
        _targetArrow = LoadTexture("res://images/ui/combat/targeting_arrow_head.png");
        return _targetArrow;
    }

    private static Texture2D? _targetArrow;

    /// <summary>A heal: Regen's green plus (RegenPower, the Regen Potion's buff). Book of Five Rings' marker on the pick that heals.</summary>
    public static Texture2D? Heal() => Power<MegaCrit.Sts2.Core.Models.Powers.RegenPower>();

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
    private static readonly Dictionary<string, Texture2D?> ByPath = new();

    /// <summary>The game's art by the path its own code uses (ImageHelper.GetImagePath), loaded once.</summary>
    public static Texture2D? Image(string innerPath)
    {
        if (ByPath.TryGetValue(innerPath, out var cached) && Valid(cached)) return cached;
        Texture2D? texture;
        try { texture = LoadTexture(MegaCrit.Sts2.Core.Helpers.ImageHelper.GetImagePath(innerPath)); } catch { texture = null; }
        ByPath[innerPath] = texture;
        return texture;
    }

    /// <summary>The card reward's icon after a fight (CardReward.RewardIcon: the common one).</summary>
    public static Texture2D? CardReward() => Image("ui/reward_screen/reward_icon_card.png");

    /// <summary>The map button in the top bar (NTopBar: top_bar/top_bar_map).</summary>
    public static Texture2D? MapButton() => Image("atlases/ui_atlas.sprites/top_bar/top_bar_map.tres");

    /// <summary>A room's icon, as the run history shows it (ImageHelper.GetRoomIconPath: event, shop...).</summary>
    public static Texture2D? Room(MegaCrit.Sts2.Core.Map.MapPointType point, MegaCrit.Sts2.Core.Rooms.RoomType room)
    {
        try { return MegaCrit.Sts2.Core.Helpers.ImageHelper.GetRoomIconPath(point, room, null) is { } path ? LoadTexture(path) : null; } catch { return null; }
    }

    /// <summary>An intent's icon by its kind (AbstractIntent.SpritePath: "buff", "unknown"...).</summary>
    public static Texture2D? Intent(string kind) => Image($"atlases/intent_atlas.sprites/intent_{kind}.tres");

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
                TextureRect otherIcon => otherIcon.Texture,
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
            if (child is TextureRect icon && icon.Texture != null) return icon.Texture;
            if (child is Sprite2D sprite2D && sprite2D.Texture != null) return sprite2D.Texture;
            if (FirstTexture(child) is { } texture) return texture;
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
