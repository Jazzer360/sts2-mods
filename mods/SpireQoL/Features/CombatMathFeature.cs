using HarmonyLib;
using Sts2Mods.Core.Features;

namespace SpireQoL.Features;

/// <summary>
/// Incoming damage vs. current block, and the net damage you'll take, with a per-enemy
/// breakdown on hover. Successor to StS1's Block Reminder (plus whichever Mintyspire
/// damage features turn out to be missed).
/// </summary>
public sealed class CombatMathFeature : IFeature
{
    public string Id => "combat-math";
    public string DisplayName => "Combat Math";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Not implemented yet: no patches applied.
    }
}
