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

    /// <summary>
    /// The tab, made once its icon is loaded: a new one for each card on every frame was garbage (decision #177). Kept while its icon is: the
    /// discard pile button's texture is freed with the room's art, and a tab kept past that drew blank in every later fight (a dev review).
    /// </summary>
    private TabSpec ShuffleTab()
    {
        if (_shuffleTab != null && GameIcons.Valid(_shuffleTab.Icon)) return _shuffleTab;
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
        // frame, most of all for a card Proc Indicators has no prediction for (a Dazed in hand: a dev review).
        var probe = BoardProbe.Hash(me, combat);
        if (_kept.Count > 64) _kept.Clear();
        foreach (var card in hand)
        {
            if (_kept.TryGetValue(card, out var kept) && kept.Probe == probe)
            {
                if (kept.Shuffles) _ctx.Decor.For(card).AddTab(ShuffleTab());
                continue;
            }
            // A card you can't play draws nothing (a Dazed, a curse); one whose own code shuffles (Reboot, Ki Chant) says so in its text.
            if (!CardPlays.CanPlayThisTurn(card) || Procs.Programs.CardEffects.ShufflesDiscardPileIn(card.GetType()) || Procs.Programs.CardEffects.ShufflesWhenShort(card.GetType()))
            {
                _kept[card] = (probe, false);
                continue;
            }
            // Every draw the play causes, the ones it sets off included (Dark Embrace per card exhausted, Game Piece, Iron Club...), up to a
            // full hand, each play on the board the one before it left: its simulation's log (decision #189), at every target it can be aimed
            // at. A shuffle the card's own code makes (Reboot, Ki Chant) isn't a draw's: its text says so.
            bool shuffles;
            try
            {
                // Proc Indicators' prediction for this frame (it runs first), with the simulation's outcomes.
                if (_ctx.Predictions.PredictionThisFrame(card) is not { OutcomeByAim: { Count: > 0 } outcomes })
                {
                    // No finished simulation yet (it's still running, or Proc Tags is off): the card's own draws against the draw pile, not
                    // kept, so the simulation's answer replaces it when it's there.
                    if (CardDrawsPastTheDrawPile(card, hand.Count, me)) _ctx.Decor.For(card).AddTab(ShuffleTab());
                    continue;
                }
                shuffles = outcomes.Values.All(outcome => outcome.PlayFiredEvery[SpireQoL.Sim.SimKeys.PlayEvent(SpireQoL.Sim.CardEvent.Shuffled)] > 0);
            }
            catch
            {
                shuffles = CardDrawsPastTheDrawPile(card, hand.Count, me);
            }
            _kept[card] = (probe, shuffles);
            if (shuffles) _ctx.Decor.For(card).AddTab(ShuffleTab());
        }
    }

    private readonly Dictionary<CardModel, (int Probe, bool Shuffles)> _kept = new(ReferenceEqualityComparer.Instance);

    /// <summary>The card's own draws, every play's, run past your draw pile (with a discard pile to shuffle in): before its simulation's answer.</summary>
    private static bool CardDrawsPastTheDrawPile(CardModel card, int handCount, MegaCrit.Sts2.Core.Entities.Players.Player me) =>
        Procs.Programs.BoardCounts.CardsDrawnTotal(card, handCount, CardPlays.Count(card)) > Sts2State.DrawPileCount(me);

}
