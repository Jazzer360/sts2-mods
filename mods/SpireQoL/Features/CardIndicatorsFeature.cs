using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
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
///    (This-turn-only Retain/Sly look the same as permanent ones: tags are icon-only.)
///  - Added Exhaust: Goopy (the enchantment that adds Exhaust).
///  - Double play: the icon of whatever causes it (Burst, Echo Form, Throwing Axe, ...).
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
            else if (card.ShouldRetainThisTurn) d.AddTab("retain", GameIcons.Enchantment<Steady>());

            if (kw.Contains(CardKeyword.Sly)) d.AddTab("sly", GameIcons.Power<MasterPlannerPower>());
            else if (card.IsSlyThisTurn) d.AddTab("sly", GameIcons.Power<MasterPlannerPower>());

            if (kw.Contains(CardKeyword.Exhaust) && !canonical.Contains(CardKeyword.Exhaust))
            {
                d.AddTab("exhaust", GameIcons.Enchantment<Goopy>());
            }

            if (d.Enabled("blocked") && Blocker(card) is { } blocker)
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

            {
                // One tag per source, each with that source's own icon (Burst, Echo Form,
                // Duplication, One-Two Punch, Signal Boost, Tag Team, Throwing Axe, or the card's
                // replay enchantment).
                foreach (var src in ExtraPlaySources(card))
                {
                    var icon = src switch
                    {
                        RelicModel r => GameIcons.Relic(r),
                        PowerModel p => GameIcons.Power(p),
                        EnchantmentModel en => GameIcons.Enchantment(en),
                        _ => null,
                    };
                    d.AddTab("double-play:" + src.GetType().Name, icon ?? GameIcons.Power<EchoFormPower>());
                }
            }
        }
    }

    /// <summary>
    /// What would make this card play more than once right now: the same calculation the game
    /// runs when you play it (CardModel.GeneratePlayCount): enchanted replays + 1, then
    /// Hook.ModifyCardPlayCount, which reports the models that changed the count. That covers
    /// Echo Form, Burst, Duplication, One-Two Punch, Signal Boost, Tag Team, Throwing Axe and
    /// anything added later. The hook is a pure query; side effects live in
    /// AfterModifyingCardPlayCount, which we never call.
    /// Tag Team only doubles attacks aimed at the enemy that has it, so single-target cards are
    /// also checked against each enemy and every source that applies to any of them is shown.
    /// </summary>
    /// <summary>How many times the card would play right now (best case over targets).</summary>
    public static int PlayCount(CardModel card)
    {
        var cs = Sts2State.Combat;
        if (cs == null) return 1;
        var baseCount = card.GetEnchantedReplayCount() + 1;
        var best = Hook.ModifyCardPlayCount(cs, card, baseCount, null, out _);
        if (card.TargetType == TargetType.AnyEnemy)
        {
            foreach (var enemy in cs.HittableEnemies) best = Math.Max(best, Hook.ModifyCardPlayCount(cs, card, baseCount, enemy, out _));
        }
        return Math.Max(1, best);
    }

    private static List<AbstractModel> ExtraPlaySources(CardModel card)
    {
        var sources = new List<AbstractModel>();
        var cs = Sts2State.Combat;
        if (cs == null) return sources;
        var replays = card.GetEnchantedReplayCount();
        if (replays > 0 && card.Enchantment != null) sources.Add(card.Enchantment);

        void Add(Creature? target)
        {
            Hook.ModifyCardPlayCount(cs, card, replays + 1, target, out var models);
            foreach (var m in models)
            {
                if (!sources.Contains(m)) sources.Add(m);
            }
        }

        Add(null);
        if (card.TargetType == TargetType.AnyEnemy)
        {
            foreach (var enemy in cs.HittableEnemies) Add(enemy);
        }
        return sources;
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
