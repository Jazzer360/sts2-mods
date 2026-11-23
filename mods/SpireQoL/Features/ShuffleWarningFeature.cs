using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Cards;
using SpireQoL.Combat;
using SpireQoL.Procs;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// A discard-pile tab on cards in hand whose draws (their own, plus the draws they set off) would run
/// past the end of your draw pile and reshuffle the discard pile mid-turn. Same on-card tab language as every other indicator.
/// Spec: docs/FEATURES.md#shuffle-tag (Shuffle Tag; id shuffle-warning, kept as the settings key: decision #185).
/// </summary>
public sealed class ShuffleWarningFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;

    public ShuffleWarningFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "shuffle-warning";
    public string DisplayName => "Shuffle Tag";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
    }

    private TabSpec? _shuffleTab;

    /// <summary>The tab, made once its icon is loaded: a new one for each card on every frame was garbage (decision #177).</summary>
    private TabSpec ShuffleTab()
    {
        if (_shuffleTab != null) return _shuffleTab;
        var tab = new TabSpec("shuffle", GameIcons.DiscardPile());
        if (tab.Icon != null) _shuffleTab = tab;
        return tab;
    }

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        if (me == null || !Sts2State.IsPlayerTurn) return;
        if (Sts2State.DiscardPileCount(me) == 0) return;

        var combat = Sts2State.Combat;
        if (combat == null) return;
        var hand = Sts2State.Hand(me);
        // Each card's answer is kept while the board probe stays the same (decision #130): worked out from the game, it was garbage on every
        // frame, most of all for a card Proc Indicators has no facts for (a Dazed in hand: a dev review).
        var probe = BoardProbe.Hash(me, combat);
        if (_kept.Count > 64) _kept.Clear();
        foreach (var card in hand)
        {
            if (_kept.TryGetValue(card, out var kept) && kept.Probe == probe)
            {
                if (kept.Shuffles) _ctx.Decor.For(card).AddTab(ShuffleTab());
                continue;
            }
            // A card you can't play draws nothing (a Dazed, a curse).
            if (!CardPlays.CanPlayThisTurn(card))
            {
                _kept[card] = (probe, false);
                continue;
            }
            // Every draw the play causes, including the ones it sets off (Dark Embrace per card
            // exhausted, Game Piece, Iron Club...), up to a full hand: PlayFacts.Draws.
            bool shuffles;
            try
            {
                // Proc Indicators' facts for this frame when it has them (it runs first), else worked out here.
                var facts = _ctx.Predictions.FactsThisFrame(card) ?? PlayFacts.For(card, me, combat, CardPlays.Count(card));
                shuffles = facts.Shuffles && !facts.ShufflesItself; // Reboot's text already says it shuffles
            }
            catch
            {
                shuffles = PlayFacts.CardsDrawnTotal(card, hand.Count, CardPlays.Count(card)) > Sts2State.DrawPileCount(me);
            }
            _kept[card] = (probe, shuffles);
            if (shuffles) _ctx.Decor.For(card).AddTab(ShuffleTab());
        }
    }

    private readonly Dictionary<CardModel, (int Probe, bool Shuffles)> _kept = new(ReferenceEqualityComparer.Instance);

}
