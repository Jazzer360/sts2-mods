using HarmonyLib;
using SpireQoL.Cards;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// A draw-pile tab on cards in hand whose draw would run past the end of your draw pile and
/// reshuffle the discard pile mid-turn. Same on-card tab language as every other indicator.
/// Spec: docs/FEATURES.md#shuffle-warning.
/// </summary>
public sealed class ShuffleWarningFeature : IFeature, IFrameFeature
{
    // Cards that draw DynamicVars["Cards"] cards (from the v0.111.0 decompile).
    private static readonly HashSet<string> DrawCards = new()
    {
        "Acrobatics", "Adrenaline", "Backflip", "BattleTrance", "BigBang", "BrightestFlame", "BurningPact",
        "Constellation", "Coolheaded", "DecisionsDecisions", "DrumOfBattle", "Expertise", "Fetch", "Finesse",
        "FlashOfSteel", "Ftl", "Glimmer", "Glow", "Impatience", "MasterOfStrategy", "MinionStrike", "Neurosurge",
        "Offering", "Overclock", "Parse", "PhotonCut", "PommelStrike", "Prophesize", "Reboot", "Reflex",
        "RocketPunch", "Scourge", "Scrape", "ShrugItOff", "Skim", "Slimed", "Soul", "SpoilsOfBattle",
        "SweepingBeam", "ThinkingAhead",
    };

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

        var draw = Sts2State.DrawPileCount(me);
        foreach (var c in Sts2State.Hand(me))
        {
            if (DrawCards.Contains(c.GetType().Name) && Sts2State.Var(c, "Cards") > draw)
            {
                _ctx.Decor.For(c).AddTab("shuffle", GameIcons.DrawPile());
            }
        }
    }
}
