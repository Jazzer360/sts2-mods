using Godot;
using MegaCrit.Sts2.Core.Models;

namespace SpireQoL.Cards;

public sealed record BadgeSpec(string Text, Color Bg, Color Fg);

public sealed record RelicMark(Texture2D? Icon, string Name, bool Crossed);

/// <summary>
/// What should be drawn on one card this frame. Features fill it in; CardOverlayRenderer draws it.
/// Collecting first and drawing once keeps features from fighting over the same card.
/// </summary>
public sealed class CardDecor
{
    public List<BadgeSpec> Badges { get; } = new();
    public List<RelicMark> Relics { get; } = new();

    /// <summary>Whole-card opacity (Ethereal). Null = untouched.</summary>
    public float? Opacity { get; set; }

    /// <summary>Darken the card and show a reason (e.g. "BOUND").</summary>
    public string? DimReason { get; set; }

    /// <summary>Pulsing border color (Pen Nib ready). Null = none.</summary>
    public Color? Border { get; set; }

    /// <summary>How many times the card will play (2+ draws echo ripples). 1 = normal.</summary>
    public int PlayCount { get; set; } = 1;

    public bool IsEmpty => Badges.Count == 0 && Relics.Count == 0 && Opacity == null && DimReason == null && Border == null && PlayCount <= 1;

    /// <summary>Changes only when the badge/relic row needs rebuilding.</summary>
    public string RowSignature()
        => string.Join("|", Badges.Select(b => b.Text)) + "#" + string.Join("|", Relics.Select(r => r.Name + (r.Crossed ? "x" : "")));
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
