using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Cards;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Mid-combat card modifiers made visible. Spec: docs/FEATURES.md#card-indicators.
///  - Ethereal cards are translucent, whatever made them Ethereal (CardModel.Keywords
///    merges the card's own keywords with ones granted by powers like Hex).
///  - Keyword badges above the card: RET (RET·1 for this turn only), SLY, +EXH when
///    Exhaust was added. INN/ETR optional.
///  - Cards a power or relic blocks from being played (Bound, Ringing, Smog, Velvet Choker)
///    are dimmed with the reason. Galvanized cards show the damage they'll deal you.
/// Colors avoid the game's glows (cyan/gold/red) and cost colors (blue/green/red).
/// </summary>
public sealed class CardIndicatorsFeature : IFeature, IFrameFeature
{
    private static readonly Color Dark = new(0.08f, 0.08f, 0.1f, 0.92f);
    private static readonly BadgeSpec Retain = new("RET", new Color(0.2f, 0.65f, 0.55f, 0.95f), Colors.White);
    private static readonly BadgeSpec RetainOnce = new("RET·1", new Color(0.2f, 0.65f, 0.55f, 0.95f), Colors.White);
    private static readonly BadgeSpec Sly = new("SLY", new Color(0.55f, 0.3f, 0.6f, 0.95f), Colors.White);
    private static readonly BadgeSpec SlyOnce = new("SLY·1", new Color(0.55f, 0.3f, 0.6f, 0.95f), Colors.White);
    private static readonly BadgeSpec AddedExhaust = new("+EXH", new Color(0.85f, 0.45f, 0.15f, 0.95f), Colors.White);
    private static readonly BadgeSpec Innate = new("INN", Dark, new Color(0.9f, 0.9f, 0.9f));
    private static readonly BadgeSpec Eternal = new("ETR", Dark, new Color(0.9f, 0.9f, 0.9f));

    private static readonly Dictionary<string, string> BlockReasons = new()
    {
        ["ChainsOfBindingPower"] = "BOUND",
        ["RingingPower"] = "RINGING",
        ["SmoggyPower"] = "SMOG",
        ["VelvetChoker"] = "CARD LIMIT",
    };

    private readonly QolContext _ctx;

    public CardIndicatorsFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "card-indicators";
    public string DisplayName => "Card Indicators";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
    }

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        if (me == null) return;
        var s = _ctx.Settings;

        foreach (var card in Sts2State.Hand(me))
        {
            var kw = card.Keywords;
            var canonical = card.CanonicalKeywords.ToHashSet();
            var d = _ctx.Decor.For(card);

            if (kw.Contains(CardKeyword.Ethereal))
            {
                // Translucency alone marks Ethereal; no text badge.
                d.Opacity = Mathf.Clamp(s.EtherealOpacity, 0.25f, 1f);
            }
            if (kw.Contains(CardKeyword.Retain)) d.Badges.Add(Retain);
            else if (card.ShouldRetainThisTurn) d.Badges.Add(RetainOnce);

            if (kw.Contains(CardKeyword.Sly)) d.Badges.Add(Sly);
            else if (card.IsSlyThisTurn) d.Badges.Add(SlyOnce);

            if (kw.Contains(CardKeyword.Exhaust) && !canonical.Contains(CardKeyword.Exhaust)) d.Badges.Add(AddedExhaust);

            if (s.ShowInnateEternalBadges)
            {
                if (kw.Contains(CardKeyword.Innate)) d.Badges.Add(Innate);
                if (kw.Contains(CardKeyword.Eternal)) d.Badges.Add(Eternal);
            }

            if (card.Affliction?.GetType().Name == "Galvanized" && card.Affliction.Amount > 0)
            {
                d.Badges.Add(new BadgeSpec($"−{card.Affliction.Amount} HP", new Color(0.7f, 0.15f, 0.2f, 0.95f), Colors.White));
            }

            if (s.DimBlockedCards && BlockedReason(card) is { } reason)
            {
                d.DimReason = reason;
            }
        }
    }

    private static string? BlockedReason(CardModel card)
    {
        if (card.CanPlay(out var reason, out var preventer)) return null;
        if (!reason.HasFlag(UnplayableReason.BlockedByHook) || preventer == null) return null;
        return BlockReasons.TryGetValue(preventer.GetType().Name, out var text) ? text : null;
    }
}
