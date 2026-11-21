using Godot;
using MegaCrit.Sts2.Core.Models;

namespace SpireQoL.Cards;

/// <summary>
/// One on-card indicator. Every on-card indicator uses this same form: a native-style tab (a copy
/// of the card's own enchantment tab) in a row along the card's top edge, with
///  - Icon: what's affecting the card (a relic, power, enchantment, the draw pile...),
///  - Crossed: a red slash = playing this card breaks or blocks something, or helps the enemy.
///  - Number: only on quantified proc tabs (block gained, damage dealt, summon, cards drawn, energy gained), else null.
/// Key de-duplicates (one tab per source per card) and is the tab's settings key.
/// Kind: what Amount counts, for a tab with an amount (null for the rest).
/// Amount is the proc amount even when the number isn't displayed (ProcNumbers off), for the audit.
/// AuditMin/AuditMax: for damage procs, the total damage range the audit expects (all targets).
/// Text: shown instead of an icon (replay count, e.g. "2×"); null for icon tabs.
/// Card: a card drawn as the icon (the game's tiny card, decision #42: a card that comes back to your hand); null for the rest.
/// NumberText: shown where the number goes when the amount is hidden (a free card's "?").
/// Aimed: it happens only at some of the targets you could aim at (decision #55): the game's targeting arrow head in the
/// icon's top-left corner.
/// Marker: another picture in that corner instead (decision #83: the heart on the card reward pick that makes Book of Five Rings
/// heal); null for none.
/// <summary>What a tab's amount counts (TabSpec.Amount): the block, damage, summon, draw and energy tabs (decision #132), and a free card's saving.</summary>
public enum TabAmount { Block, Damage, Summon, CardsDrawn, EnergyGained, EnergySaved }

public sealed record TabSpec(string Key, Texture2D? Icon, bool Crossed = false, int? Number = null, TabAmount? Kind = null, int Amount = 0,
    int AuditMin = 0, int AuditMax = 0, string? Text = null, CardModel? Card = null, string? NumberText = null, bool Aimed = false,
    Texture2D? Marker = null);

/// <summary>
/// What should be drawn on one card this frame. Features fill it in; CardOverlayRenderer draws it.
/// Collecting first and drawing once keeps features from fighting over the same card.
/// </summary>
public sealed class CardDecor
{
    public List<TabSpec> Tabs { get; } = new();

    /// <summary>
    /// Every tab the features predicted, including ones the settings hide. The audit checks against
    /// these, so switching a tab off doesn't make its proc look unexplained.
    /// </summary>
    public List<TabSpec> Predicted { get; } = new();

    /// <summary>Whole-card opacity (Ethereal). Null = untouched.</summary>
    public float? Opacity { get; set; }

    public bool IsEmpty => Tabs.Count == 0 && Opacity == null;

    /// <summary>Settings filter (QolSettings.TabEnabled); null = everything shown.</summary>
    internal Func<string, bool>? Filter { get; init; }

    /// <summary>The settings by kind (QolSettings.Shown): null hides the tab; else the tab as shown. Null = as given.</summary>
    internal Func<TabSpec, TabSpec?>? Shape { get; init; }

    /// <returns>Whether the tab was added (false if a tab with this key exists or it's turned off).</returns>
    /// <param name="kind">What <paramref name="amount"/> counts, for a tab with an amount; else null.</param>
    public bool AddTab(string key, Texture2D? icon, bool crossed = false, int? number = null, TabAmount? kind = null, int amount = 0,
        int auditMin = 0, int auditMax = 0, string? text = null, CardModel? card = null)
    {
        if (Predicted.Any(t => t.Key == key)) return false;
        return AddTab(new TabSpec(key, icon, crossed, number, kind, amount, auditMin, auditMax, text, card));
    }

    /// <summary>Adds a tab with every field of <paramref name="spec"/> (number text, aim marker...).</summary>
    /// <returns>Whether the tab was added (false if a tab with this key exists or it's turned off).</returns>
    public bool AddTab(TabSpec spec)
    {
        if (Predicted.Any(t => t.Key == spec.Key)) return false;
        Predicted.Add(spec);
        if (Filter != null && !Filter(spec.Key)) return false;
        var shown = Shape == null ? spec : Shape(spec);
        if (shown == null) return false;
        Tabs.Add(shown);
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

    /// <summary>The settings by kind applied to every tab (CardDecor.Shape).</summary>
    public Func<TabSpec, TabSpec?>? Shape { get; set; }

    public CardDecor For(CardModel card)
    {
        if (!_decor.TryGetValue(card, out var d))
        {
            d = new CardDecor { Filter = Filter, Shape = Shape };
            _decor[card] = d;
        }
        return d;
    }

    public CardDecor? Get(CardModel card) => _decor.TryGetValue(card, out var d) ? d : null;

    /// <summary>Last frame's tabs per card (what was predicted just before a card was played; used by ProcAudit).</summary>
    public IReadOnlyDictionary<CardModel, IReadOnlyList<TabSpec>> Previous { get; private set; }
        = new Dictionary<CardModel, IReadOnlyList<TabSpec>>(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Each card's tabs from the last frame it was in your hand (empty if it had none). Kept after
    /// the card leaves the hand, since a play can start several frames later (ProcAudit).
    /// </summary>
    public IReadOnlyDictionary<CardModel, IReadOnlyList<TabSpec>> LastInHand => _lastInHand;

    private readonly Dictionary<CardModel, IReadOnlyList<TabSpec>> _lastInHand = new(ReferenceEqualityComparer.Instance);

    /// <summary>Called by the decorating features for every card in hand they looked at this frame.</summary>
    public void SeenInHand(CardModel card) => _seenThisFrame.Add(card);

    private readonly HashSet<CardModel> _seenThisFrame = new(ReferenceEqualityComparer.Instance);

    /// <summary>Frames rendered so far, and the frame each card was last seen in hand.</summary>
    public long Frame { get; private set; }

    public IReadOnlyDictionary<CardModel, long> LastSeenFrame => _lastSeenFrame;

    private readonly Dictionary<CardModel, long> _lastSeenFrame = new(ReferenceEqualityComparer.Instance);

    public void Clear()
    {
        var snap = new Dictionary<CardModel, IReadOnlyList<TabSpec>>(ReferenceEqualityComparer.Instance);
        foreach (var (card, d) in _decor)
        {
            if (d.Predicted.Count > 0) snap[card] = d.Predicted.ToList();
        }
        Previous = snap;
        foreach (var card in _seenThisFrame)
        {
            _lastInHand[card] = snap.TryGetValue(card, out var tabs) ? tabs : Array.Empty<TabSpec>();
            _lastSeenFrame[card] = Frame;
        }
        if (_lastInHand.Count > 300)
        {
            _lastInHand.Clear(); // cards from long-gone turns
            _lastSeenFrame.Clear();
        }
        Frame++;
        _seenThisFrame.Clear();
        _decor.Clear();
    }
}
