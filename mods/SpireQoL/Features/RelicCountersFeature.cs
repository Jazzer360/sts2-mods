using HarmonyLib;
using Sts2Mods.Core.Features;

namespace SpireQoL.Features;

/// <summary>
/// A tray above the hand with one pip row per counter relic (Pen Nib, Nunchaku, Kunai,
/// Stone Calendar...). Pips fill as the counter advances; the row pulses when one step from
/// firing and blinks when the next card will fire it. Spec: docs/FEATURES.md#relic-counters.
///
/// Generic: reads RelicModel.ShowCounter / DisplayAmount / Status (the game sets Status =
/// Active one step before firing), plus a small table in this mod for each relic's threshold
/// and which card type advances it (docs/TRACKER.md).
/// </summary>
public sealed class RelicCountersFeature : IFeature
{
    public string Id => "relic-counters";
    public string DisplayName => "Relic Counters";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Not implemented yet: no patches applied.
    }
}
