using HarmonyLib;
using Sts2Mods.Core.Features;

namespace SpireQoL.Features;

/// <summary>
/// Relic consequences shown on the cards in hand. Spec: docs/FEATURES.md#relic-reminders.
///
///  - Card overlay (top corner): the relic's icon on each affected card. Struck through if
///    playing the card breaks the relic's condition (Art of War, Ripple Basin on attacks).
///  - Pen Nib ready: attacks in hand get a pulsing border in a color the game doesn't use.
///  - Rules are data, built from a small predicate vocabulary. Relics outside it get no
///    overlay rather than a wrong one.
/// </summary>
public sealed class RelicRemindersFeature : IFeature
{
    public string Id => "relic-reminders";
    public string DisplayName => "Relic Reminders";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Not implemented yet: no patches applied.
    }
}
