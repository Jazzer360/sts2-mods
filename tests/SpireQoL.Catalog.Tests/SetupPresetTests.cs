using SpireQoL.Menu;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The first-run setup's presets (decision #147): each turns on only display features (what shows in fights and outside them, never
/// the checks), each holds the one before it, and Everything is the mod's defaults, so "every default" and "Everything" can't drift.
/// </summary>
public class SetupPresetTests
{
    [Fact]
    public void PresetsTurnOnDisplayFeaturesOnlyAndGrow()
    {
        var display = SetupScreen.PresetFeatures.ToHashSet();
        Assert.Contains("incoming-damage", display);
        Assert.DoesNotContain("forecast-audit", display);
        Assert.DoesNotContain("update-notice", display);
        foreach (var preset in SetupScreen.Presets)
            Assert.True(preset.Features.IsSubsetOf(display), $"{preset.Name} names a feature that isn't on the In fights or Outside fights pages");
        for (var i = 1; i < SetupScreen.Presets.Length; i++)
            Assert.True(SetupScreen.Presets[i - 1].Features.IsSubsetOf(SetupScreen.Presets[i].Features), $"{SetupScreen.Presets[i].Name} drops a feature {SetupScreen.Presets[i - 1].Name} has");
    }

    [Fact]
    public void EverythingIsTheDefaults()
    {
        var everything = SetupScreen.Presets[^1];
        var defaults = new QolSettings();
        Assert.Equal("Everything", everything.Name);
        Assert.True(everything.Features.SetEquals(SetupScreen.PresetFeatures));
        Assert.Equal(new SetupScreen.PresetOptions(defaults.Chances, defaults.DeathChance, defaults.IntentOdds, defaults.ProcNumbers, defaults.ProcIndirect,
            defaults.EventOdds, defaults.EventInspect), everything.Options);
    }
}
