using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

/// <summary>
/// Play Preview (decision #183): while you hold a card (aimed, for one that needs a target), or select a potion's Drink or aim it (decision #314:
/// PotionPreview), Incoming Damage, the Death Skull's odds, Teammate Incoming Damage and the enemies' intents show the turn as it would go after
/// playing or using it there. Nothing new is worked out:
/// each card's simulation already searches its play, the end of your turn and the enemy turn at every aim (CardPredictions); this
/// picks the held card's outcome at its aim. The card tabs don't change.
/// </summary>
internal static class PlayPreview
{
    public const string FeatureId = "play-preview";

    /// <summary>NCard._previewTarget: the creature the card is aimed at while you target (NCardPlay.OnCreatureHover sets it).</summary>
    private static readonly FieldInfo? PreviewTarget = AccessTools.Field(typeof(NCard), "_previewTarget");

    /// <summary>NMouseCardPlay.IsCardInPlayZone: letting go of the mouse now plays the card (a read of the mouse's height, nothing else).</summary>
    private static readonly Func<NMouseCardPlay, bool>? InPlayZone =
        AccessTools.Method(typeof(NMouseCardPlay), "IsCardInPlayZone") is { } method ? AccessTools.MethodDelegate<Func<NMouseCardPlay, bool>>(method) : null;

    /// <summary>The preview last built, kept while the held card's outcome is the same one (no model rebuilt every frame).</summary>
    private static Shown? _built;

    /// <summary>The held card or the potion about to be used, and the outcome shown for it: its aim's outcome, and the turn built from it.</summary>
    internal sealed record Shown(AbstractModel Source, SimOutcome Outcome, TurnEndModel Model, IReadOnlyDictionary<Creature, int> Ids);

    /// <summary>
    /// The turn after playing the held card at its aim, or null: no card held, one that needs a target not aimed yet, Play Preview
    /// switched off, or its outcome not worked out for the board now.
    /// </summary>
    public static Shown? Current(QolContext ctx)
    {
        if (!Sts2State.IsPlayerTurn || !(ctx.Settings.Features.TryGetValue(FeatureId, out var on) ? on : true)) return null;
        // No card held: a potion about to be used (decision #314).
        if (Sts2State.HeldCard() is not var (node, card)) return Sts2State.Me is { } me ? PotionPreview.Current(me) : null;
        var target = PreviewTarget?.GetValue(node) as Creature;
        if (card.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly)
        {
            // Aimed: letting go plays it at its target.
            if (target == null) return null;
        }
        // Not aimed (every enemy, a random one, you): only where letting go plays it, the game's own test
        // (NMouseCardPlay.IsCardInPlayZone: above the play zone's line). A controller's (or keyboard's) play is ready from the
        // start: NControllerCardPlay.MultiCreatureTargeting plays it on Confirmed, and a card that can't be played cancels at once.
        else if (Sts2State.CurrentCardPlay() is not { } play || !(play is NMouseCardPlay mouse ? InPlayZone?.Invoke(mouse) == true : play is NControllerCardPlay)) return null;
        // Ready to play: from here on, a preview not shown says why in dev mode's log (once per card and reason).
        if (!ctx.Predictions.LastPredictions.TryGetValue(card, out var prediction)) return NotShown(card, NoPreview.NoPrediction);
        if (prediction.Sim is not { } inputs) return NotShown(card, NoPreview.NoSimulation);
        if (prediction.OutcomeByAim is not { } outcomes) return NotShown(card, NoPreview.NoOutcomes);
        if (inputs.AimFor(target) is not { } aim || !outcomes.TryGetValue(aim, out var outcome)) return NotShown(card, NoPreview.NoOutcomeAtTheAim);
        // Its outcome must be for the board now: the board the card's search started from is the forecast's this frame (by key: the same
        // board read again is a new state object with the same key).
        if (TurnEndModel.Begin() is not { } start) return NotShown(card, NoPreview.NoForecast);
        if (start.StateKey != inputs.StartKey) return NotShown(card, NoPreview.AnotherBoard);
        if (_built is { } built && ReferenceEquals(built.Outcome, outcome) && ReferenceEquals(built.Source, card)) return built;
        // The card's own result: its search keeps the totals the displays show (not the breakdown by source: TurnEndModel._breakdown).
        return _built = new Shown(card, outcome, TurnEndModel.Preview(start, outcome), start.Ids);
    }

    /// <summary>Why a card ready to play shows no preview (dev mode's log).</summary>
    private enum NoPreview { NoPrediction, NoSimulation, NoOutcomes, NoOutcomeAtTheAim, NoForecast, AnotherBoard }

    private static (CardModel? Card, NoPreview Why) _lastNotShown;

    /// <summary>
    /// No preview for a card ready to play: dev mode logs why, once per card and reason (a held card asks every frame; a log line only when
    /// the card or the reason changes, so a steady frame allocates nothing).
    /// </summary>
    private static Shown? NotShown(CardModel card, NoPreview why)
    {
        if (ReferenceEquals(_lastNotShown.Card, card) && _lastNotShown.Why == why) return null;
        _lastNotShown = (card, why);
        ModEntry.Log.Detail($"[play-preview] no preview for {card.GetType().Name}: {why}");
        return null;
    }

    /// <summary>What Incoming Damage, the Death Skull and Teammate Incoming Damage show: the held card's turn, else ending it now.</summary>
    public static TurnEndModel? Model(QolContext ctx) => Current(ctx)?.Model ?? TurnEndModel.Shown();

    /// <summary>The tint of what a preview changes (not the game's glow or cost colours): Incoming Damage, the new intent numbers.</summary>
    public static readonly Godot.Color Tint = new(0.82f, 0.76f, 1f);
}
