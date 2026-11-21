using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Cards;
using SpireQoL.Combat;
using SpireQoL.Procs;
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
    private readonly ReuseCheck _reuse = new("Card Indicators");
    /// <summary>The board probe _last was worked out from.</summary>
    private int _lastProbe;

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        if (me == null) return;
        var settings = _ctx.Settings;
        var hand = Sts2State.Hand(me);

        // Nothing changed since last frame (decision #130): its results again, without asking the game's hooks for every card
        // (how many times it plays, whether it can be played). Dev mode works them out anyway every 30th such frame and logs a
        // difference (a change the probe missed).
        var combat = Sts2State.Combat;
        var reuse = _reuse.Decide(combat != null && _last.Count == hand.Count && _last.Select(cardEthereal => cardEthereal.Card).SequenceEqual(hand, ReferenceEqualityComparer.Instance)
                                  && BoardProbe.Hash(me, combat) == _lastProbe);
        if (reuse == FrameReuse.Reuse)
        {
            foreach (var (card, ethereal, tabs) in _last) Show(card, ethereal, tabs, settings);
            return;
        }

        var now = new List<(CardModel Card, bool Ethereal, List<TabSpec> Tabs)>(hand.Count);
        foreach (var card in hand)
        {
            var (ethereal, tabs) = Work(card);
            now.Add((card, ethereal, tabs));
            Show(card, ethereal, tabs, settings);
        }
        if (reuse == FrameReuse.WorkOutAndCheck)
        {
            var changed = now.Zip(_last).FirstOrDefault(pair => pair.First.Ethereal != pair.Second.Ethereal || !pair.First.Tabs.SequenceEqual(pair.Second.Tabs));
            _reuse.Compare(changed.First.Card == null, "a card's tabs changed while the board probe didn't",
                () => $"{changed.First.Card!.GetType().Name}: [{string.Join(", ", changed.Second.Tabs.Select(tab => tab.Key))}] → [{string.Join(", ", changed.First.Tabs.Select(tab => tab.Key))}]");
        }
        _last.Clear();
        _last.AddRange(now);
        if (combat != null) _lastProbe = BoardProbe.Hash(me, combat);
    }

    private void Show(CardModel card, bool ethereal, List<TabSpec> tabs, QolSettings settings)
    {
        _ctx.Decor.SeenInHand(card);
        var decor = _ctx.Decor.For(card);
        if (ethereal) decor.Opacity = Mathf.Clamp(settings.EtherealOpacity, 0.25f, 1f);
        foreach (var tab in tabs) decor.AddTab(tab);
    }

    /// <summary>A card's Ethereal fade and its tabs (Retain, Sly, replay, double-play), from the game.</summary>
    private static (bool Ethereal, List<TabSpec> Tabs) Work(CardModel card)
    {
        var tabs = new List<TabSpec>();
        var keywords = card.Keywords;
        // Only Ethereal that's on the card itself (KeywordSources.Local: the card's own keyword,
        // or Music Box's copy). Ethereal granted by Hex (the Hexed affliction, via
        // HexPower.TryModifyKeywordsInCombat) is a Global source and is skipped: when Hex hits,
        // every card is Ethereal and the game's own affliction overlay already shows it.
        var ethereal = card.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal);

        if (keywords.Contains(CardKeyword.Retain) || card.ShouldRetainThisTurn) tabs.Add(new TabSpec("retain", GameIcons.Enchantment<Steady>()));
        if (keywords.Contains(CardKeyword.Sly) || card.IsSlyThisTurn) tabs.Add(new TabSpec("sly", GameIcons.Power<MasterPlannerPower>()));

        // Replay on the card itself (Hidden Gem, Transfigure, Sword Sage on Sovereign Blade, or
        // an enchantment): no power or relic to show, so the tab reads "2×" (total plays).
        // A card that can't be played (curses, Unplayable statuses, blocked cards) plays nothing twice.
        if (!CardPlays.CanPlayThisTurn(card)) return (ethereal, tabs);
        var replays = card.GetEnchantedReplayCount();
        if (replays > 0) tabs.Add(new TabSpec("replay", Icon: null, Text: $"{replays + 1}×"));

        // One tag per source, each with that source's own icon (Burst, Echo Form,
        // Duplication, One-Two Punch, Signal Boost, Tag Team, Throwing Axe, or the card's
        // replay enchantment).
        foreach (var src in CardPlays.ReplaySources(card))
        {
            var icon = src switch
            {
                RelicModel relic => GameIcons.Relic(relic),
                PowerModel power => GameIcons.Power(power),
                EnchantmentModel enchantment => GameIcons.Enchantment(enchantment),
                _ => null,
            };
            tabs.Add(new TabSpec("double-play:" + src.GetType().Name, icon ?? GameIcons.Power<EchoFormPower>()));
        }
        return (ethereal, tabs);
    }
}
