using HarmonyLib;
using Sts2Mods.Core.Features;

namespace SpireQoL.Features;

/// <summary>
/// Makes mid-combat card modifiers visible at a glance. Spec: docs/FEATURES.md#card-indicators.
/// Work list: docs/TRACKER.md (Card modifiers).
///
///  - Keyword badges (top-center) read from CardModel.Keywords (local + global sources, so
///    Hex's granted Ethereal and Music Box's applied Ethereal both show), plus single-turn
///    state (ShouldRetainThisTurn, IsSlyThisTurn).
///  - Ethereal cards render translucent whenever they're Ethereal this combat, whatever the source.
///  - Afflictions already have game overlays (AfflictionModel.OverlayPath); we only add
///    "unplayable right now" state where the game doesn't show it (Bound, Ringing, Smog).
///  - Never reuse the game's highlight colors (NCardHighlight: cyan playable, gold, red) or
///    its cost colors (blue increased, green decreased, red can't afford).
/// </summary>
public sealed class CardIndicatorsFeature : IFeature
{
    public string Id => "card-indicators";
    public string DisplayName => "Card Indicators";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Not implemented yet: no patches applied.
    }
}
