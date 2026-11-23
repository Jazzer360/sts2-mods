using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

/// <summary>
/// Play Preview (decision #183): while you hold a card (aimed, for one that needs a target), Incoming Damage, the Death Skull's odds,
/// Teammate Incoming Damage and the enemies' intents show the turn as it would go after playing it there. Nothing new is worked out:
/// each card's simulation already searches its play, the end of your turn and the enemy turn at every aim (CardPredictions); this
/// picks the held card's outcome at its aim. The card tabs don't change.
/// </summary>
internal static class PlayPreview
{
    public const string FeatureId = "play-preview";

    /// <summary>NCard._previewTarget: the creature the card is aimed at while you target (NCardPlay.OnCreatureHover sets it).</summary>
    private static readonly FieldInfo? PreviewTarget = AccessTools.Field(typeof(NCard), "_previewTarget");

    /// <summary>The preview last built, kept while the held card's outcome is the same one (no model rebuilt every frame).</summary>
    private static Shown? _built;

    /// <summary>The held card and the outcome shown for it: the card, its aim's outcome, and the turn built from it.</summary>
    internal sealed record Shown(CardModel Card, SimOutcome Outcome, TurnEndModel Model, IReadOnlyDictionary<Creature, int> Ids);

    /// <summary>
    /// The turn after playing the held card at its aim, or null: no card held, one that needs a target not aimed yet, Play Preview
    /// switched off, or its outcome not worked out for the board now.
    /// </summary>
    public static Shown? Current(QolContext ctx)
    {
        if (!Sts2State.IsPlayerTurn || !(ctx.Settings.Features.TryGetValue(FeatureId, out var on) ? on : true)) return null;
        if (Sts2State.HeldCard() is not var (node, card)) return null;
        var target = PreviewTarget?.GetValue(node) as Creature;
        if (target == null && card.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly) return null;
        if (!ctx.Predictions.LastFacts.TryGetValue(card, out var facts) || facts.Sim is not { } inputs || facts.OutcomeByAim is not { } outcomes) return null;
        if (inputs.AimFor(target) is not { } aim || !outcomes.TryGetValue(aim, out var outcome)) return null;
        // Its outcome must be for the board now: the forecast's start this frame is the one the card's search started from.
        if (TurnEndModel.Begin() is not { } start || !ReferenceEquals(start.State, inputs.Start)) return null;
        if (_built is { } built && ReferenceEquals(built.Outcome, outcome) && ReferenceEquals(built.Card, card)) return built;
        return _built = new Shown(card, outcome, TurnEndModel.Preview(start, outcome), start.Ids);
    }

    /// <summary>What Incoming Damage, the Death Skull and Teammate Incoming Damage show: the held card's turn, else ending it now.</summary>
    public static TurnEndModel? Model(QolContext ctx) => Current(ctx)?.Model ?? TurnEndModel.Shown();

    /// <summary>The tint of what a preview changes (not the game's glow or cost colours): Incoming Damage, the new intent numbers.</summary>
    public static readonly Godot.Color Tint = new(0.82f, 0.76f, 1f);
}
