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
        if (_discardPile != null) return _discardPile;
        _discardPile = PileIcon(NCombatRoom.Instance?.Ui?.DiscardPile);
        return _discardPile;
    }

    /// <summary>The draw pile button's icon (NCombatUi.DrawPile → "Icon").</summary>
    public static Texture2D? DrawPile()
    {
        if (_drawPile != null) return _drawPile;
        _drawPile = PileIcon(NCombatRoom.Instance?.Ui?.DrawPile);
        return _drawPile;
    }

    private static Texture2D? _discardPile;

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

    private static Texture2D? Cached(Type key, Func<Texture2D?> load)
    {
        if (Cache.TryGetValue(key, out var tex)) return tex;
        try { tex = load(); } catch { tex = null; }
        Cache[key] = tex;
        return tex;
    }
}
