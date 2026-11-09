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
/// Everything is an on-card tab (CardOverlayRenderer), except Ethereal translucency and dimming.
///  - Retain: Steady (the Retain enchantment). Sly: Master Planner (the Sly power).
///    "1" in the tab = this turn only.
///  - Added Exhaust: Goopy (the enchantment that adds Exhaust). Optional Innate: Royally Approved;
///    Eternal: Tezcatara's Ember (the enchantments that add them).
///  - Double play: Echo Form, with the play count.
///  - Galvanized: Galvanic power icon, with the damage it deals you.
///  - Blocked by Bound / Ringing / Smog / Velvet Choker: card dimmed + crossed tab with the
///    blocking power's or relic's icon.
///  - Ethereal (on the card itself, not from Hex): translucent.
/// </summary>
public sealed class CardIndicatorsFeature : IFeature, IFrameFeature
{
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
                d.Opacity = Mathf.Clamp(s.EtherealOpacity, 0.25f, 1f);
            }

            if (kw.Contains(CardKeyword.Retain)) d.AddTab("retain", GameIcons.Enchantment<Steady>());
            else if (card.ShouldRetainThisTurn) d.AddTab("retain", GameIcons.Enchantment<Steady>(), "1");

            if (kw.Contains(CardKeyword.Sly)) d.AddTab("sly", GameIcons.Power<MasterPlannerPower>());
            else if (card.IsSlyThisTurn) d.AddTab("sly", GameIcons.Power<MasterPlannerPower>(), "1");

            if (kw.Contains(CardKeyword.Exhaust) && !canonical.Contains(CardKeyword.Exhaust))
            {
                d.AddTab("exhaust", GameIcons.Enchantment<Goopy>());
            }

            if (s.ShowInnateEternalBadges)
            {
                if (kw.Contains(CardKeyword.Innate)) d.AddTab("innate", GameIcons.Enchantment<RoyallyApproved>());
                if (kw.Contains(CardKeyword.Eternal)) d.AddTab("eternal", GameIcons.Enchantment<TezcatarasEmber>());
            }

            if (card.Affliction?.GetType().Name == "Galvanized" && card.Affliction.Amount > 0)
            {
                d.AddTab("galvanized", GameIcons.Power<GalvanicPower>(), card.Affliction.Amount.ToString());
            }

            if (s.DimBlockedCards && Blocker(card) is { } blocker)
            {
                d.Dim = true;
                var icon = blocker switch
                {
                    RelicModel r => GameIcons.Relic(r),
                    PowerModel p => GameIcons.Power(p),
                    _ => null,
                };
                d.AddTab("blocked", icon, crossed: true);
            }

            if (s.ShowDoublePlay)
            {
                var plays = PlayCount(card);
                if (plays > 1) d.AddTab("double-play", GameIcons.Power<EchoFormPower>(), plays.ToString());
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

    private static readonly HashSet<string> Blockers = new() { "ChainsOfBindingPower", "RingingPower", "SmoggyPower", "VelvetChoker" };

    /// <summary>The power or relic stopping this card from being played (game's own CanPlay), if it's one we show.</summary>
    private static AbstractModel? Blocker(CardModel card)
    {
        if (card.CanPlay(out var reason, out var preventer)) return null;
        if (!reason.HasFlag(UnplayableReason.BlockedByHook) || preventer == null) return null;
        return Blockers.Contains(preventer.GetType().Name) ? preventer : null;
    }
}
