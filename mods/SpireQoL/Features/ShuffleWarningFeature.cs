using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Cards;
using SpireQoL.Combat;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Shows when a draw will reshuffle your discard pile. Cards in your hand at that moment (and
/// the card being played) stay out of the new draw pile. Spec: docs/FEATURES.md#shuffle-warning.
///  - Widget: draw/discard counts and draws left before a shuffle; potions that would shuffle.
///  - Draw cards in hand whose draw would run past the draw pile get a SHUFFLE badge.
///  - While a shuffle is possible, other hand cards get a KEPT badge.
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
    private MovableWidget? _widget;
    private Label? _headline;
    private Label? _detail;

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
        _widget = _ctx.Widget(Id, "Shuffle Warning", new WidgetLayout
        {
            Anchor = ScreenAnchor.BottomLeft, OffsetX = 30, OffsetY = -200,
        });
        _headline = UiKit.Label("", 18, new Color(0.6f, 1f, 0.85f));
        _detail = UiKit.Label("", 14, new Color(0.85f, 0.95f, 0.9f));
        _widget.Content.AddChild(_headline);
        _widget.Content.AddChild(_detail);
    }

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        if (me == null || !Sts2State.IsPlayerTurn)
        {
            _widget!.HasContent = false;
            return;
        }

        var draw = Sts2State.DrawPileCount(me);
        var discard = Sts2State.DiscardPileCount(me);
        var hand = Sts2State.Hand(me);

        var shufflers = hand.Where(c => DrawCards.Contains(c.GetType().Name) && Sts2State.Var(c, "Cards") > draw).ToList();
        var potionShufflers = me.Potions
            .Where(p => DrawPotions.Contains(p.GetType().Name) && Sts2State.Var(p, "Cards") > draw)
            .Select(TurnEndModel.SafeTitle)
            .ToList();
        // A mid-turn shuffle (the only kind where your current hand stays out) needs something
        // in hand or a potion that draws past the end of the draw pile.
        var shufflePossible = discard > 0 && (shufflers.Count > 0 || potionShufflers.Count > 0);

        _widget!.HasContent = discard > 0 && (draw <= 5 || shufflePossible);
        _headline!.Text = draw == 0
            ? "Next draw reshuffles"
            : $"Reshuffle after {draw} more draw{(draw == 1 ? "" : "s")}";
        var lines = new List<string> { $"Draw {draw} · Discard {discard}" };
        if (potionShufflers.Count > 0) lines.Add("Would shuffle: " + string.Join(", ", potionShufflers));
        if (shufflePossible && _ctx.Settings.ShuffleHandMarkers) lines.Add("KEPT cards stay out of the new draw pile");
        _detail!.Text = string.Join("\n", lines);

        if (discard == 0) return;
        foreach (var c in shufflers) _ctx.Decor.For(c).Badges.Add(ShuffleBadge);

        if (shufflePossible && _ctx.Settings.ShuffleHandMarkers)
        {
            foreach (var c in hand)
            {
                if (!shufflers.Contains(c)) _ctx.Decor.For(c).Badges.Add(KeptBadge);
            }
        }
    }
}
