using Godot;
using HarmonyLib;
using SpireQoL.Cards;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Card badges that appear when something in hand can reshuffle your discard pile mid-turn.
/// Cards still in your hand at that moment (and the card being played) stay out of the new
/// draw pile. No summary widget: the badges are the reminder. Spec: docs/FEATURES.md#shuffle-warning.
///  - SHUFFLE on draw cards whose draw is more than your draw pile.
///  - KEPT on the other hand cards while a shuffle is possible (from a card or a draw potion).
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

    private static readonly HashSet<string> DrawPotions = new()
    {
        "BottledPotential", "Clarity", "CureAll", "GlowwaterPotion", "SwiftPotion",
    };

    private static readonly BadgeSpec ShuffleBadge = new("SHUFFLE", new Color(0.15f, 0.45f, 0.35f, 0.95f), Colors.White);
    private static readonly BadgeSpec KeptBadge = new("KEPT", new Color(0.25f, 0.25f, 0.3f, 0.9f), new Color(0.85f, 0.85f, 0.9f));

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

        var shufflers = hand.Where(c => DrawCards.Contains(c.GetType().Name) && Sts2State.Var(c, "Cards") > draw).ToList();
        var potionCanShuffle = me.Potions.Any(p => DrawPotions.Contains(p.GetType().Name) && Sts2State.Var(p, "Cards") > draw);

        foreach (var c in shufflers) _ctx.Decor.For(c).Badges.Add(ShuffleBadge);

        if ((shufflers.Count > 0 || potionCanShuffle) && _ctx.Settings.ShuffleHandMarkers)
        {
            foreach (var c in hand)
            {
                if (!shufflers.Contains(c)) _ctx.Decor.For(c).Badges.Add(KeptBadge);
            }
        }
    }
}
