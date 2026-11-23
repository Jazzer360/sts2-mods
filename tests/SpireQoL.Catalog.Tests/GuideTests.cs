using System.Runtime.CompilerServices;
using Sts2Mods.Core.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The settings menu's Guide page (decision #56) explains every feature: each feature's name (or the Guide section that covers
/// it) appears in the Guide's source, so a new feature fails here until it has an entry.
/// </summary>
public class GuideTests
{
    /// <summary>Features the Guide covers under another name (none since decision #185: every feature by its own name).</summary>
    private static readonly Dictionary<string, string> CoveredAs = new();

    private static string GuideSource([CallerFilePath] string here = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
        return File.ReadAllText(Path.Combine(root, "mods", "SpireQoL", "Menu", "SettingsMenu.Guide.cs"));
    }

    [Fact]
    public void EveryFeatureIsInTheGuide()
    {
        var guide = GuideSource();
        var features = typeof(ModEntry).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IFeature).IsAssignableFrom(type))
            .Select(type => ((IFeature)RuntimeHelpers.GetUninitializedObject(type)).DisplayName)
            .Where(name => !name.EndsWith("(log only)", StringComparison.Ordinal)) // diagnostics: nothing on screen
            .ToList();
        Assert.True(features.Count >= 10, $"only {features.Count} features found");
        var missing = features.Where(name => !guide.Contains(CoveredAs.GetValueOrDefault(name, name))).ToList();
        Assert.True(missing.Count == 0, "Features with no Guide entry (Menu/SettingsMenu.Guide.cs):\n" + string.Join("\n", missing));
    }
}
