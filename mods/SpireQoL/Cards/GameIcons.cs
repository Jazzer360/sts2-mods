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

    /// <summary>The skull from the game's emote wheel (the lethal tab).</summary>
    public static Texture2D? Skull()
    {
        if (Valid(_skull)) return _skull;
        _skull = LoadTexture("res://images/ui/emote/skull.png");
        return _skull;
    }

    private static Texture2D? _skull;

    private static Texture2D? _discardPile;

    /// <summary>The discard pile art outside combat (the game's own path, NetScreenTypeExtensions CardPile).</summary>
    private const string DiscardPilePath = "res://images/packed/combat_ui/discard_pile.png";

    private static Texture2D? LoadTexture(string path)
    {
        try
        {
            return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
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
