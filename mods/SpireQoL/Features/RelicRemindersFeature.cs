using HarmonyLib;
using Sts2Mods.Core.Features;

namespace SpireQoL.Features;

/// <summary>
/// Relic consequences shown on the cards in hand at the moment they matter.
///
/// Plan (see docs/ROADMAP.md):
///  - Card overlay (top corner): relic icon on affected cards; struck through if playing
///    the card breaks the relic's condition (Art of War on attacks).
///  - Event pop near the draw pile (Unceasing Top), relic pulse in the top bar.
///  - Rules are data (YAML/JSON), not code, using a small predicate vocabulary.
///    Relics outside the vocabulary get no overlay rather than a wrong one.
///  - First three rules: Art of War, Ripple Basin, Unceasing Top.
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
