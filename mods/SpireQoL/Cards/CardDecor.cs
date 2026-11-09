using Godot;
using MegaCrit.Sts2.Core.Models;

namespace SpireQoL.Cards;

/// <summary>
/// One on-card indicator. Every on-card indicator uses this same form: a native-style tab (a copy
/// of the card's own enchantment tab) in a row along the card's top edge, with
///  - Icon: what's affecting the card (a relic, power, enchantment, the draw pile...),
///  - Crossed: a red slash = playing this card breaks or blocks something, or helps the enemy.
///  - Number: only on quantified proc tabs (block gained, damage dealt, summon), else null.
/// Key de-duplicates (one tab per source per card) and is the tab's settings key.
/// </summary>
/// Amount is the proc amount even when the number isn't displayed (ProcNumbers off), for the audit.
/// AuditMin/AuditMax: for damage procs, the total damage range the audit expects (all targets).
/// Text: shown instead of an icon (replay count, e.g. "2×"); null for icon tabs.
public sealed record TabSpec(string Key, Texture2D? Icon, bool Crossed = false, int? Number = null, string? Kind = null, int Amount = 0,
    int AuditMin = 0, int AuditMax = 0, string? Text = null);

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

    /// <summary>Settings filter (QolSettings.TabEnabled); null = everything shown.</summary>
    internal Func<string, bool>? Filter { get; init; }

    /// <returns>Whether the tab was added (false if a tab with this key exists or it's turned off).</returns>
    /// <param name="kind">"block", "damage" or "summon" for quantified procs (with <paramref name="amount"/>), else null.</param>
    public bool AddTab(string key, Texture2D? icon, bool crossed = false, int? number = null, string? kind = null, int amount = 0,
        int auditMin = 0, int auditMax = 0, string? text = null)
    {
        if (Tabs.Any(t => t.Key == key)) return false;
        if (Filter != null && !Filter(key)) return false;
        Tabs.Add(new TabSpec(key, icon, crossed, number, kind, amount, auditMin, auditMax, text));
        return true;
    }

    /// <summary>Whether a tab kind is turned on in settings (for things that aren't tabs, like dimming).</summary>
    public bool Enabled(string key) => Filter == null || Filter(key);
}

/// <summary>Per-frame collection of decorations, keyed by card model.</summary>
public sealed class CardDecorFrame
{
    private readonly Dictionary<CardModel, CardDecor> _decor = new(ReferenceEqualityComparer.Instance);

    /// <summary>Settings filter applied to every tab (set once settings are loaded).</summary>
    public Func<string, bool>? Filter { get; set; }

    public CardDecor For(CardModel card)
    {
        if (!_decor.TryGetValue(card, out var d))
        {
            d = new CardDecor { Filter = Filter };
            _decor[card] = d;
        }
        return d;
    }

    public CardDecor? Get(CardModel card) => _decor.TryGetValue(card, out var d) ? d : null;

    /// <summary>Last frame's tabs per card (what was predicted just before a card was played; used by ProcAudit).</summary>
    public IReadOnlyDictionary<CardModel, IReadOnlyList<TabSpec>> Previous { get; private set; }
        = new Dictionary<CardModel, IReadOnlyList<TabSpec>>(ReferenceEqualityComparer.Instance);

    public void Clear()
    {
        var snap = new Dictionary<CardModel, IReadOnlyList<TabSpec>>(ReferenceEqualityComparer.Instance);
        foreach (var (card, d) in _decor)
        {
            if (d.Tabs.Count > 0) snap[card] = d.Tabs.ToList();
        }
        Previous = snap;
        _decor.Clear();
    }
}
