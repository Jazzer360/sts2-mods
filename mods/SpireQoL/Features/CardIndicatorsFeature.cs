using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using SpireQoL.Cards;
using SpireQoL.Combat;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Mid-combat card modifiers made visible. Spec: docs/FEATURES.md#card-indicators.
/// Everything is an on-card tab (CardOverlayRenderer), except Ethereal translucency and dimming.
///  - Retain: Steady (the Retain enchantment). Sly: Master Planner (the Sly power).
///    (This-turn-only Retain/Sly look the same as permanent ones: tags are icon-only.)
///  - Replay on the card (Hidden Gem, Transfigure, Sword Sage, enchantments): a text tab "N×" (total plays).
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

    /// <summary>Last frame's result per hand card: Ethereal (faded) and its tabs, kept while the board probe sees nothing change.</summary>
    private readonly List<(CardModel Card, bool Ethereal, List<TabSpec> Tabs)> _last = new();
    private int _reusedSinceCheck;
    /// <summary>The board probe _last was worked out from.</summary>
    private int _lastProbe;

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        if (me == null) return;
        var s = _ctx.Settings;
        var hand = Sts2State.Hand(me);

        // Nothing changed since last frame (decision #130): its results again, without asking the game's hooks for every card
        // (how many times it plays, whether it can be played). Dev mode works them out anyway every 30th such frame and logs a
        // difference (a change the probe missed).
        var cs = Sts2State.Combat;
        var same = cs != null && _last.Count == hand.Count && _last.Select(x => x.Card).SequenceEqual(hand, ReferenceEqualityComparer.Instance)
                   && BoardProbe.Hash(me, cs) == _lastProbe;
        if (same && (!ModEntry.DevMode || ++_reusedSinceCheck < BoardProbe.CheckEvery))
        {
            foreach (var (card, ethereal, tabs) in _last) Show(card, ethereal, tabs, s);
            return;
        }
        if (same) _reusedSinceCheck = 0;

        var now = new List<(CardModel Card, bool Ethereal, List<TabSpec> Tabs)>(hand.Count);
        foreach (var card in hand)
        {
            var (ethereal, tabs) = Work(card);
            now.Add((card, ethereal, tabs));
            Show(card, ethereal, tabs, s);
        }
        if (same && now.Zip(_last).FirstOrDefault(x => x.First.Ethereal != x.Second.Ethereal || !x.First.Tabs.SequenceEqual(x.Second.Tabs)) is { First.Card: { } changed } d)
            BoardProbe.Missed("Card Indicators", "a card's tabs changed while the board probe didn't",
                $"{changed.GetType().Name}: [{string.Join(", ", d.Second.Tabs.Select(t => t.Key))}] → [{string.Join(", ", d.First.Tabs.Select(t => t.Key))}]");
        _last.Clear();
        _last.AddRange(now);
        if (cs != null) _lastProbe = BoardProbe.Hash(me, cs);
    }

    private void Show(CardModel card, bool ethereal, List<TabSpec> tabs, QolSettings s)
    {
        _ctx.Decor.SeenInHand(card);
        var d = _ctx.Decor.For(card);
        if (ethereal) d.Opacity = Mathf.Clamp(s.EtherealOpacity, 0.25f, 1f);
        foreach (var t in tabs) d.AddTab(t);
    }

    /// <summary>A card's Ethereal fade and its tabs (Retain, Sly, replay, double-play), from the game.</summary>
    private static (bool Ethereal, List<TabSpec> Tabs) Work(CardModel card)
    {
        var tabs = new List<TabSpec>();
        var kw = card.Keywords;
        // Only Ethereal that's on the card itself (KeywordSources.Local: the card's own keyword,
        // or Music Box's copy). Ethereal granted by Hex (the Hexed affliction, via
        // HexPower.TryModifyKeywordsInCombat) is a Global source and is skipped: when Hex hits,
        // every card is Ethereal and the game's own affliction overlay already shows it.
        var ethereal = card.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal);

        if (kw.Contains(CardKeyword.Retain) || card.ShouldRetainThisTurn) tabs.Add(new TabSpec("retain", GameIcons.Enchantment<Steady>()));
        if (kw.Contains(CardKeyword.Sly) || card.IsSlyThisTurn) tabs.Add(new TabSpec("sly", GameIcons.Power<MasterPlannerPower>()));

        // Replay on the card itself (Hidden Gem, Transfigure, Sword Sage on Sovereign Blade, or
        // an enchantment): no power or relic to show, so the tab reads "2×" (total plays).
        // A card that can't be played (curses, Unplayable statuses, blocked cards) plays nothing twice.
        if (!ProcIndicatorsFeature.CanEverPlayNow(card)) return (ethereal, tabs);
        var replays = card.GetEnchantedReplayCount();
        if (replays > 0) tabs.Add(new TabSpec("replay", null, Text: $"{replays + 1}×"));

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
            tabs.Add(new TabSpec("double-play:" + src.GetType().Name, icon ?? GameIcons.Power<EchoFormPower>()));
        }
        return (ethereal, tabs);
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
    /// <summary>
    /// While Decisions, Decisions asks you to pick a Skill from your hand (CardSelectCmd.FromHand: the
    /// hand in select mode, the card in your play pile), the pick is auto-played Repeat times for free
    /// (CardCmd.AutoPlay, each with its own replays). 0 when no such choice is open.
    /// </summary>
    public static int ChoiceAutoPlays(Player me)
    {
        try
        {
            if (NPlayerHand.Instance is not { IsInCardSelection: true }) return 0;
            var source = me.PlayerCombatState?.PlayPile.Cards.OfType<DecisionsDecisions>().FirstOrDefault();
            return source == null ? 0 : Math.Max(1, Sts2State.Var(source, "Repeat", 3));
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>The card would be auto-played by an open hand choice (it's in your hand while one is open).</summary>
    public static bool ChoiceAutoPlayed(CardModel card)
        => card.Owner is { } me && card.Pile?.Type == PileType.Hand && ChoiceAutoPlays(me) > 0;

    public static int PlayCount(CardModel card)
    {
        var cs = Sts2State.Combat;
        if (cs == null) return 1;
        var auto = card.Owner is { } me && card.Pile?.Type == PileType.Hand ? ChoiceAutoPlays(me) : 0;
        return OwnPlayCount(card, cs) * Math.Max(1, auto);
    }

    private static int OwnPlayCount(CardModel card, ICombatState cs)
    {
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
}
