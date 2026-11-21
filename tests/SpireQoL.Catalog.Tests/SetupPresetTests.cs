using SpireQoL.Menu;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The first-run setup's two choices (decision #147): how much on screen turns on only display features (what shows in fights and
/// outside them, never the checks), each level holding the one before; the chances run from none to all; and the fullest of each is
/// the mod's defaults, so "every default" and the dev setup can't drift apart.
/// </summary>
public class SetupPresetTests
{
    [Fact]
    public void OnScreenLevelsTurnOnDisplayFeaturesOnlyAndGrow()
    {
        var display = SetupScreen.PresetFeatures.ToHashSet();
        Assert.Contains("incoming-damage", display);
        Assert.DoesNotContain("forecast-audit", display);
        Assert.DoesNotContain("update-notice", display);
        foreach (var density in SetupScreen.Densities)
            Assert.True(density.Features.IsSubsetOf(display), $"{density.Name} names a feature that isn't on the In fights or Outside fights pages");
        for (var i = 1; i < SetupScreen.Densities.Length; i++)
            Assert.True(SetupScreen.Densities[i - 1].Features.IsSubsetOf(SetupScreen.Densities[i].Features), $"{SetupScreen.Densities[i].Name} drops a feature {SetupScreen.Densities[i - 1].Name} has");
    }

    [Fact]
    public void TheFullestOfEachIsTheDefaults()
    {
        var defaults = new QolSettings();
        var full = SetupScreen.Densities[^1];
        Assert.True(full.Features.SetEquals(SetupScreen.PresetFeatures));
        Assert.Equal((defaults.ProcIndirect, defaults.EventInspect), (full.ProcIndirect, full.EventInspect));
        var all = SetupScreen.ChanceLevels[^1];
        Assert.Equal((defaults.Chances, defaults.DeathChance, defaults.IntentOdds, defaults.EventOdds, defaults.TabOwn(QolSettings.DrawChanceKey)),
            (all.Chances, all.DeathChance, all.IntentOdds, all.EventOdds, all.DrawChance));
        Assert.False(SetupScreen.ChanceLevels[0].Chances); // none: only what's certain
    }
}
