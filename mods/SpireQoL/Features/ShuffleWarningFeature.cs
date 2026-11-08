using HarmonyLib;
using Sts2Mods.Core.Features;

namespace SpireQoL.Features;

/// <summary>
/// Warns when a draw would empty the draw pile and reshuffle the discard. Cards still in
/// your hand at that moment stay out of the new draw pile. Spec: docs/FEATURES.md#shuffle-warning.
///
///  - Draw cards and potions in hand whose draw amount is more than the draw pile size get
///    a shuffle badge.
///  - A small marker by the draw pile shows how many cards remain before a shuffle.
///  - While a shuffle is possible, cards in hand get a subtle "stays out of the next draw
///    pile" marker, so you can choose what to keep in hand and what to reshuffle.
/// </summary>
public sealed class ShuffleWarningFeature : IFeature
{
    public string Id => "shuffle-warning";
    public string DisplayName => "Shuffle Warning";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Not implemented yet: no patches applied.
    }
}
