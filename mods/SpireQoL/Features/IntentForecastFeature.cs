using HarmonyLib;
using Sts2Mods.Core.Features;

namespace SpireQoL.Features;

/// <summary>
/// Each enemy's upcoming moves shown above the current intent, shrunk and faded.
///
/// Plan (see docs/ROADMAP.md):
///  - Deterministic next moves: solid icons, 2-3 ahead.
///  - Random branches: side-by-side icons with odds, or "?" when not computable.
///  - Conditional moves: icon + condition marker, explained on hover.
///  - Damage numbers use current Strength and are marked approximate.
///  - Preferred source: walk the live monster AI's move graph at runtime.
///    Fallback: build-time extraction from the decompile.
/// </summary>
public sealed class IntentForecastFeature : IFeature
{
    public string Id => "intent-forecast";
    public string DisplayName => "Intent Forecast";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Not implemented yet: no patches applied.
    }
}
