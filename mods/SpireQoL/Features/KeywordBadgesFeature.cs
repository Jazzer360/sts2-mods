using HarmonyLib;
using Sts2Mods.Core.Features;

namespace SpireQoL.Features;

/// <summary>
/// Small glyph at the top-center of a card for Retain and Ethereal, readable without hovering.
///
/// Plan (see docs/ROADMAP.md):
///  - Read keywords from the card's live runtime state, not its base definition, so
///    Ethereal added mid-combat and generated cards are covered.
///  - Show wherever a card renders: hand, piles, rewards, deck view.
///  - Shape + color, not color alone. Default set: Retain, Ethereal.
///
/// To confirm in the decompile first: where live keywords live on the card object, and
/// which card-visual node to hang a child badge on (SpireTracker does similar via Harmony).
/// </summary>
public sealed class KeywordBadgesFeature : IFeature
{
    public string Id => "keyword-badges";
    public string DisplayName => "Keyword Badges";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Not implemented yet: no patches applied.
    }
}
