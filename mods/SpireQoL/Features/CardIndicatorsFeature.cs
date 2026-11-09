using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Cards;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Mid-combat card modifiers made visible. Spec: docs/FEATURES.md#card-indicators.
///  - Ethereal cards are translucent, whatever made them Ethereal (CardModel.Keywords
///    merges the card's own keywords with ones granted by powers like Hex).
///  - Retain and Sly as native-style tabs (copies of the enchantment tab) on the left edge;
///    badges above the card for +EXH when
///    Exhaust was added. INN/ETR optional.
///  - Cards a power or relic blocks from being played (Bound, Ringing, Smog, Velvet Choker)
///    are dimmed with the reason. Galvanized cards show the damage they'll deal you.
/// Colors avoid the game's glows (cyan/gold/red) and cost colors (blue/green/red).
/// </summary>
public sealed class CardIndicatorsFeature : IFeature, IFrameFeature
{
    private static readonly Color Dark = new(0.08f, 0.08f, 0.1f, 0.92f);
    private static Texture2D? _retainIcon;
    private static Texture2D? _slyIcon;
    private static bool _iconsLoaded;

    private static Texture2D? RetainIcon { get { LoadIcons(); return _retainIcon; } }
    private static Texture2D? SlyIcon { get { LoadIcons(); return _slyIcon; } }
    private static Texture2D? _doublePlayIcon;
    private static Texture2D? DoublePlayIcon { get { LoadIcons(); return _doublePlayIcon; } }

    /// <summary>
    /// Icons from the game's own models: Steady (the Retain enchantment), Master Planner (the Sly
    /// power), Echo Form (the iconic play-twice power).
    /// </summary>
    private static void LoadIcons()
    {
        if (_iconsLoaded) return;
        _iconsLoaded = true;
        try { _retainIcon = ModelDb.Enchantment<Steady>().Icon; } catch { _retainIcon = null; }
        try { _slyIcon = ModelDb.Power<MasterPlannerPower>().Icon; } catch { _slyIcon = null; }
        try { _doublePlayIcon = ModelDb.Power<EchoFormPower>().Icon; } catch { _doublePlayIcon = null; }
    }
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

            // Only Ethereal that's on the card itself (KeywordSources.Local: the card's own keyword,
            // or Music Box's copy). Ethereal granted by Hex (the Hexed affliction, via
            // HexPower.TryModifyKeywordsInCombat) is a Global source and is skipped: when Hex hits,
            // every card is Ethereal and the game's own affliction overlay already shows it.
            if (card.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal))
            {
                // Translucency alone marks Ethereal; no text badge.
                d.Opacity = Mathf.Clamp(s.EtherealOpacity, 0.25f, 1f);
            }
            // Retain and Sly as native-style tabs on the card's left edge (copies of the game's
            // enchantment tab): Steady's icon (the Retain enchantment) and Master Planner's icon
            // (the Sly power). "1" in the tab's number slot = this turn only.
            if (kw.Contains(CardKeyword.Retain)) d.Tabs.Add(new TabSpec("retain", RetainIcon, null));
            else if (card.ShouldRetainThisTurn) d.Tabs.Add(new TabSpec("retain", RetainIcon, "1"));

            if (kw.Contains(CardKeyword.Sly)) d.Tabs.Add(new TabSpec("sly", SlyIcon, null));
            else if (card.IsSlyThisTurn) d.Tabs.Add(new TabSpec("sly", SlyIcon, "1"));

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

            if (s.ShowDoublePlay)
            {
                var plays = PlayCount(card);
                if (plays > 1)
                {
                    // Echo Form icon on a native-style tab; the tab's number is the play count.
                    d.Tabs.Add(new TabSpec("double-play", DoublePlayIcon, plays.ToString()));
                }
            }
        }
    }

    /// <summary>
    /// How many times this card would play right now: the same calculation the game runs when
    /// you play it (CardModel.GeneratePlayCount): enchanted replays + 1, then Hook.ModifyCardPlayCount.
    /// That covers Echo Form, Burst, Duplication, One-Two Punch, Signal Boost, Tag Team, Throwing
    /// Axe and anything added later. The hook is a pure query; side effects live in
    /// AfterModifyingCardPlayCount, which we never call.
    /// Tag Team only doubles attacks aimed at the enemy that has it, so single-target cards are
    /// checked against each enemy and the best case is shown.
    /// </summary>
    private static int PlayCount(CardModel card)
    {
        var cs = Sts2State.Combat;
        if (cs == null) return 1;
        var baseCount = card.GetEnchantedReplayCount() + 1;
        var best = Hook.ModifyCardPlayCount(cs, card, baseCount, null, out _);
        if (card.TargetType == TargetType.AnyEnemy)
        {
            foreach (var enemy in cs.HittableEnemies)
            {
                best = Math.Max(best, Hook.ModifyCardPlayCount(cs, card, baseCount, enemy, out _));
            }
        }
        return best;
    }

    private static string? BlockedReason(CardModel card)
    {
        if (card.CanPlay(out var reason, out var preventer)) return null;
        if (!reason.HasFlag(UnplayableReason.BlockedByHook) || preventer == null) return null;
        return BlockReasons.TryGetValue(preventer.GetType().Name, out var text) ? text : null;
    }
}
