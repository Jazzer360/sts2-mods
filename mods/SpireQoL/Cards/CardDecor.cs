using Godot;
using MegaCrit.Sts2.Core.Models;

namespace SpireQoL.Cards;

/// <summary>
/// One on-card indicator. Every on-card indicator uses this same form: a native-style tab (a copy
/// of the card's own enchantment tab) in a row along the card's top edge, with
///  - Icon: what's affecting the card (a relic, power, enchantment, the draw pile...),
///  - Label: the tab's small number, or null (play count, "1" = this turn only, damage...),
///  - Crossed: a red slash = playing this card breaks or blocks something.
/// Key de-duplicates (one tab per source per card).
/// </summary>
public sealed record TabSpec(string Key, Texture2D? Icon, string? Label = null, bool Crossed = false);

/// <summary>
/// What should be drawn on one card this frame. Features fill it in; CardOverlayRenderer draws it.
/// Collecting first and drawing once keeps features from fighting over the same card.
/// </summary>
public sealed class CardDecor
{
    public List<TabSpec> Tabs { get; } = new();

    /// <summary>Whole-card opacity (Ethereal). Null = untouched.</summary>
    public float? Opacity { get; set; }

    /// <summary>Darken the card: it can't be played right now (the reason is a crossed tab).</summary>
    public bool Dim { get; set; }

    public bool IsEmpty => Tabs.Count == 0 && Opacity == null && !Dim;

    public void AddTab(string key, Texture2D? icon, string? label = null, bool crossed = false)
    {
        if (Tabs.Any(t => t.Key == key)) return;
        Tabs.Add(new TabSpec(key, icon, label, crossed));
    }
}

/// <summary>Per-frame collection of decorations, keyed by card model.</summary>
public sealed class CardDecorFrame
{
    private readonly Dictionary<CardModel, CardDecor> _decor = new(ReferenceEqualityComparer.Instance);

    public CardDecor For(CardModel card)
    {
        if (!_decor.TryGetValue(card, out var d))
        {
            d = new CardDecor();
            _decor[card] = d;
        }
        return d;
    }

    public CardDecor? Get(CardModel card) => _decor.TryGetValue(card, out var d) ? d : null;

    public void Clear() => _decor.Clear();
}
