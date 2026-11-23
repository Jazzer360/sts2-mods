using HarmonyLib;
using SpireQoL.Combat;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// "Does something happen the moment I play this card that the card doesn't say?" One tab per power or relic that playing the
/// card sets off, with that source's icon. Spec: docs/FEATURES.md#proc-indicators.
///
///  - Trigger: plain icon ("this sets off that").
///  - Block / damage / summon / draw / energy: icon plus the amount (QolSettings.ProcNumbers; draws and energy: decision #132).
///  - Harmful (helps the enemy, hurts you, or breaks one of your relics): crossed.
///
/// Every tab comes from the outcome simulator (decision #48, docs/SIMULATOR.md): the card's play is simulated once per target,
/// and the models that fired during it (the play log) are its tabs, with the block or damage they caused. A good effect needs
/// every branch of some aim; a warning, one branch of any aim. Effects judged by the end of the turn (kills, stuns, waking an
/// enemy) compare the play's outcome with ending the turn now. The predictions are worked out in Combat/CardPredictions, which the audit (ProcAudit) checks against real plays.
/// </summary>
public sealed class ProcIndicatorsFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;

    public ProcIndicatorsFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "proc-indicators";
    public string DisplayName => "Proc Indicators";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven. The predictions remember the cards placed on top of your draw pile (observe-only; patched once).
        DrawPileMemory.Patch(harmony);
    }

    public void Attach(OverlayHost host)
    {
    }

    public void OnFrame(OverlayHost host)
    {
        if (_ctx.Predictions.Update() is not { } predicted) return;
        if (Sts2State.Me is { } me)
            foreach (var card in Sts2State.Hand(me)) _ctx.Decor.SeenInHand(card);
        // By index: a foreach through the list's interface makes an enumerator every frame (decision #177).
        for (var i = 0; i < predicted.Count; i++)
        {
            var (card, tabs) = predicted[i];
            var decor = _ctx.Decor.For(card);
            foreach (var tab in tabs) decor.AddTab(tab);
        }
    }
}
