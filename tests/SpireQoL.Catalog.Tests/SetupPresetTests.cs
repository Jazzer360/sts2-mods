using SpireQoL.Menu;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Quick setup (decision #147): card tab levels turn on only card tab features, each level holding the one before, the fullest being
/// the defaults; and Everything puts every display feature and option back to the defaults, so it can't drift from them.
/// </summary>
public class SetupPresetTests
{
    [Fact]
    public void CardTabLevelsGrowToTheDefaults()
    {
        foreach (var level in SettingsMenu.CardTabLevels)
            Assert.True(level.Features.IsSubsetOf(SettingsMenu.CardTabFeatures), $"{level.Name} turns on a feature that isn't a card tab feature");
        for (var i = 1; i < SettingsMenu.CardTabLevels.Length; i++)
            Assert.True(SettingsMenu.CardTabLevels[i - 1].Features.IsSubsetOf(SettingsMenu.CardTabLevels[i].Features),
                $"{SettingsMenu.CardTabLevels[i].Name} drops a feature {SettingsMenu.CardTabLevels[i - 1].Name} has");
        var full = SettingsMenu.CardTabLevels[^1];
        Assert.True(full.Features.SetEquals(SettingsMenu.CardTabFeatures));
        Assert.Equal(new QolSettings().ProcIndirect, full.ProcIndirect);
        Assert.Empty(SettingsMenu.CardTabLevels[0].Features);
    }

    [Fact]
    public void EverythingIsTheDefaults()
    {
        // Everything off first, then Everything: every display feature on, every option as a fresh settings file has it.
        var settings = new QolSettings
        {
            ProcNumbers = false, ProcIndirect = false, AimMarker = false, CardTabs = false, PlainTabs = false, CrossedTabs = false,
            Chances = false, DeathChance = false, IntentOdds = false, EventOdds = false, EventInspect = false,
        };
        foreach (var (id, _, _, _) in SettingsMenu.FeatureRows) settings.Features[id] = false;
        settings.Tabs[QolSettings.DrawChanceKey] = false;
        SettingsMenu.Everything(settings);
        var defaults = new QolSettings();
        foreach (var (id, _, _, where) in SettingsMenu.FeatureRows)
            if (where != SettingsMenu.FeaturePage.General) Assert.True(settings.Features[id], $"{id} isn't on after Everything");
        Assert.Equal(
            (defaults.ProcNumbers, defaults.ProcIndirect, defaults.AimMarker, defaults.CardTabs, defaults.PlainTabs, defaults.CrossedTabs,
                defaults.Chances, defaults.DeathChance, defaults.IntentOdds, defaults.EventOdds, defaults.EventInspect, true),
            (settings.ProcNumbers, settings.ProcIndirect, settings.AimMarker, settings.CardTabs, settings.PlainTabs, settings.CrossedTabs,
                settings.Chances, settings.DeathChance, settings.IntentOdds, settings.EventOdds, settings.EventInspect, settings.TabOwn(QolSettings.DrawChanceKey)));
    }
}
