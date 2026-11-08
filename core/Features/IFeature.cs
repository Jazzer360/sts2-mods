using HarmonyLib;

namespace Sts2Mods.Core.Features;

/// <summary>
/// One independently toggleable piece of a mod (e.g. "Keyword Badges").
/// A feature that fails to apply is disabled and logged; it never takes the game down.
/// </summary>
public interface IFeature
{
    /// <summary>Stable id, used for settings keys and log lines. lower-kebab-case.</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Whether the feature is on when the player hasn't chosen.</summary>
    bool EnabledByDefault { get; }

    /// <summary>
    /// Apply this feature's Harmony patches. Throwing is fine: the host catches it,
    /// unpatches anything this feature applied, and marks the feature failed.
    /// </summary>
    void Apply(Harmony harmony);
}
