using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Combat;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Ethereal Fade (docs/FEATURES.md "Ethereal Fade", decision #185): an Ethereal card in your hand is drawn see-through (the setting
/// EtherealOpacity), so the card behind it shows. Not a tag: the card itself fades (CardOverlayRenderer draws it through FlatFade,
/// decision #113). Only Ethereal that's on the card itself (KeywordSources.Local: the card's own keyword, or Music Box's copy).
/// Ethereal granted by Hex (the Hexed affliction, via HexPower.TryModifyKeywordsInCombat) is a Global source and is skipped: when Hex
/// hits, every card is Ethereal and the game's own affliction overlay already shows it.
/// </summary>
public sealed class EtherealFadeFeature : IFeature, IFrameFeature
{
    public const string FeatureId = "ethereal-fade";

    private readonly QolContext _ctx;

    public EtherealFadeFeature(QolContext ctx) => _ctx = ctx;

    public string Id => FeatureId;
    public string DisplayName => "Ethereal Fade";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
    }

    /// <summary>Last frame's answer per hand card, kept while the board probe sees nothing change (decision #130).</summary>
    private readonly List<(CardModel Card, bool Ethereal)> _last = new();
    private readonly List<(CardModel Card, bool Ethereal)> _now = new();
    private readonly ReuseCheck _reuse = new("Ethereal Fade");
    /// <summary>The board probe _last was worked out from.</summary>
    private int _lastProbe;

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        if (me == null) return;
        var opacity = Mathf.Clamp(_ctx.Settings.EtherealOpacity, 0.25f, 1f);
        var hand = Sts2State.Hand(me);
        var combat = Sts2State.Combat;
        var reuse = _reuse.Decide(combat != null && SameHand(hand) && BoardProbe.Hash(me, combat) == _lastProbe);
        if (reuse == FrameReuse.Reuse)
        {
            for (var i = 0; i < _last.Count; i++) Show(_last[i].Card, _last[i].Ethereal, opacity);
            return;
        }

        _now.Clear();
        for (var i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            var ethereal = card.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal);
            _now.Add((card, ethereal));
            Show(card, ethereal, opacity);
        }
        if (reuse == FrameReuse.WorkOutAndCheck)
        {
            var same = _now.Count == _last.Count;
            for (var i = 0; same && i < _now.Count; i++) same = ReferenceEquals(_now[i].Card, _last[i].Card) && _now[i].Ethereal == _last[i].Ethereal;
            _reuse.Compare(same, "a card's Ethereal fade changed while the board probe didn't");
        }
        _last.Clear();
        _last.AddRange(_now);
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

    private void Show(CardModel card, bool ethereal, float opacity)
    {
        _ctx.Decor.SeenInHand(card);
        if (ethereal) _ctx.Decor.For(card).Opacity = opacity;
    }
}
