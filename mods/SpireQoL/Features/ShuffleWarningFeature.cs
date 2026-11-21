using HarmonyLib;
using SpireQoL.Cards;
using SpireQoL.Procs;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// A discard-pile tab on cards in hand whose draws (their own, plus the draws they set off) would run
/// past the end of your draw pile and reshuffle the discard pile mid-turn. Same on-card tab language as every other indicator.
/// Spec: docs/FEATURES.md#shuffle-warning.
/// </summary>
public sealed class ShuffleWarningFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;

    public ShuffleWarningFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "shuffle-warning";
    public string DisplayName => "Shuffle Warning";
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
        if (me == null || !Sts2State.IsPlayerTurn) return;
        if (Sts2State.DiscardPileCount(me) == 0) return;

        var combat = Sts2State.Combat;
        if (combat == null) return;
        var hand = Sts2State.Hand(me);
        foreach (var card in hand)
        {
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
            if (shuffles) _ctx.Decor.For(card).AddTab("shuffle", GameIcons.DiscardPile());
        }
    }

}
