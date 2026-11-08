using HarmonyLib;
using Sts2Mods.Core.Features;

namespace SpireQoL.Features;

/// <summary>
/// Shows block you'll gain automatically at end of turn (Plating, Frost orb passives,
/// Orichalcum, Cloak Clasp, Ripple Basin...) next to your current block, so you don't
/// overblock. Spec: docs/FEATURES.md#block-forecast. Sources: docs/TRACKER.md (Block sources).
/// </summary>
public sealed class BlockForecastFeature : IFeature
{
    public string Id => "block-forecast";
    public string DisplayName => "Block Forecast";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Not implemented yet: no patches applied.
    }
}
