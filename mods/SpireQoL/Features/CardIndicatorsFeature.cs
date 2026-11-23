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
/// Keyword Tags (id card-indicators, kept as the settings key: decision #185): mid-combat card modifiers made visible as on-card
/// tags (CardOverlayRenderer). Spec: docs/FEATURES.md#keyword-tags.
///  - Retain: Steady (the Retain enchantment). Sly: Master Planner (the Sly power).
///    (This-turn-only Retain/Sly look the same as permanent ones: tags are icon-only.)
///  - Replay on the card (Hidden Gem, Transfigure, Sword Sage, enchantments): a text tag "N×" (total plays).
///  - Double play: the icon of whatever causes it (Burst, Echo Form, Throwing Axe, ...).
/// Ethereal cards fading is Ethereal Fade's (EtherealFadeFeature): not a tag.
/// </summary>
public sealed class CardIndicatorsFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;

    public CardIndicatorsFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "card-indicators";
    public string DisplayName => "Keyword Tags";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
    }

    /// <summary>Last frame's tabs per hand card, kept while the board probe sees nothing change.</summary>
    private readonly List<(CardModel Card, List<TabSpec> Tabs)> _last = new();
    private readonly ReuseCheck _reuse = new("Keyword Tags");
    /// <summary>The board probe _last was worked out from.</summary>
    private int _lastProbe;

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        if (me == null) return;
        var hand = Sts2State.Hand(me);

        // Nothing changed since last frame (decision #130): its results again, without asking the game's hooks for every card
        // (how many times it plays, whether it can be played). Dev mode works them out anyway on such a frame once a second and logs a
        // difference (a change the probe missed).
        var combat = Sts2State.Combat;
        var reuse = _reuse.Decide(combat != null && SameHand(hand) && BoardProbe.Hash(me, combat) == _lastProbe);
        if (reuse == FrameReuse.Reuse)
        {
            foreach (var (card, tabs) in _last) Show(card, tabs);
            return;
        }

        var now = new List<(CardModel Card, List<TabSpec> Tabs)>(hand.Count);
        foreach (var card in hand)
        {
            var tabs = Work(card);
            now.Add((card, tabs));
            Show(card, tabs);
        }
        if (reuse == FrameReuse.WorkOutAndCheck)
        {
            var changed = now.Zip(_last).FirstOrDefault(pair => !pair.First.Tabs.SequenceEqual(pair.Second.Tabs));
            _reuse.Compare(changed.First.Card == null, "a card's tags changed while the board probe didn't",
                () => $"{changed.First.Card!.GetType().Name}: [{string.Join(", ", changed.Second.Tabs.Select(tab => tab.Key))}] → [{string.Join(", ", changed.First.Tabs.Select(tab => tab.Key))}]");
        }
        _last.Clear();
        _last.AddRange(now);
        if (combat != null) _lastProbe = BoardProbe.Hash(me, combat);
    }

    /// <summary>The hand holds the cards last worked out, in the same order (by index: this runs every frame).</summary>
    private bool SameHand(IReadOnlyList<CardModel> hand)
    {
        if (_last.Count != hand.Count) return false;
        for (var i = 0; i < hand.Count; i++)
            if (!ReferenceEquals(_last[i].Card, hand[i])) return false;
        return true;
    }

    private void Show(CardModel card, List<TabSpec> tabs)
    {
        _ctx.Decor.SeenInHand(card);
        if (tabs.Count == 0) return;
        var decor = _ctx.Decor.For(card);
        foreach (var tab in tabs) decor.AddTab(tab);
    }

    /// <summary>A card's tags (Retain, Sly, replay, double-play), from the game.</summary>
    private static List<TabSpec> Work(CardModel card)
    {
        var tabs = new List<TabSpec>();
        var keywords = card.Keywords;

        if (keywords.Contains(CardKeyword.Retain) || card.ShouldRetainThisTurn) tabs.Add(new TabSpec("retain", GameIcons.Enchantment<Steady>()));
        if (keywords.Contains(CardKeyword.Sly) || card.IsSlyThisTurn) tabs.Add(new TabSpec("sly", GameIcons.Power<MasterPlannerPower>()));

        // Replay on the card itself (Hidden Gem, Transfigure, Sword Sage on Sovereign Blade, or
        // an enchantment): no power or relic to show, so the tag reads "2×" (total plays).
        // A card that can't be played (curses, Unplayable statuses, blocked cards) plays nothing twice.
        if (!CardPlays.CanPlayThisTurn(card)) return tabs;
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
        return tabs;
    }
}
