using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Cards;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// A discard-pile tab on cards in hand whose draw would run past the end of your draw pile and
/// reshuffle the discard pile mid-turn. Same on-card tab language as every other indicator.
/// Spec: docs/FEATURES.md#shuffle-warning.
/// </summary>
public sealed class ShuffleWarningFeature : IFeature, IFrameFeature
{
    // Cards that draw DynamicVars["Cards"] cards (from the v0.111.0 decompile, every CardPileCmd.Draw caller).
    private static readonly HashSet<string> DrawCards = new()
    {
        "Acrobatics", "Adrenaline", "Backflip", "BattleTrance", "BigBang", "BrightestFlame", "BurningPact",
        "Constellation", "Coolheaded", "DecisionsDecisions", "DrumOfBattle", "Expertise", "Fetch", "Finesse",
        "FlashOfSteel", "Ftl", "Glimmer", "Glow", "HuddleUp", "Impatience", "MasterOfStrategy", "MinionStrike",
        "Neurosurge", "Offering", "Overclock", "Parse", "PhotonCut", "PommelStrike", "Prepared", "Prophesize",
        "Reboot", "Reflex", "RocketPunch", "Scourge", "Scrape", "ShrugItOff", "Skim", "Slimed", "Soul",
        "SpoilsOfBattle", "SweepingBeam", "ThinkingAhead",
    };

    // Cards that always draw exactly one (Pillage draws at least one).
    private static readonly HashSet<string> DrawOne = new() { "DaggerThrow", "EscapePlan", "Pillage" };

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
        var hand = Sts2State.Hand(me);
        foreach (var c in hand)
        {
            if (CardsDrawn(c, hand.Count) > draw)
            {
                _ctx.Decor.For(c).AddTab("shuffle", GameIcons.DiscardPile());
            }
        }
    }

    /// <summary>How many cards playing this card would draw right now (0 if it doesn't draw).</summary>
    private static int CardsDrawn(CardModel c, int handCount)
    {
        var name = c.GetType().Name;
        if (DrawCards.Contains(name)) return Sts2State.Var(c, "Cards");
        if (DrawOne.Contains(name)) return 1;
        switch (c)
        {
            // Draws up to a full hand; Scrawl itself has left the hand by then.
            case Scrawl: return CardPile.MaxCardsInHand - (handCount - 1);
            // Draws one per distinct orb type channeled.
            case CompileDriver when c.DynamicVars.TryGetValue("CalculatedCards", out var v) && v is CalculatedVar cv:
                return (int)cv.Calculate(null);
            // Only draws when it's the only card in hand.
            case Restlessness: return handCount == 1 ? Sts2State.Var(c, "Cards") : 0;
            // Mad Science with the Wisdom rider (Tinker Time).
            case MadScience m when m.TinkerTimeRider.ToString() == "Wisdom": return Sts2State.Var(c, "WisdomCards");
        }
        return 0;
    }
}
