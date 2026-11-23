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
            if (SettingsMenu.PageOf(where) != SettingsMenu.FeaturePage.General) Assert.True(settings.Features[id], $"{id} isn't on after Everything");
        Assert.Equal(
            (defaults.ProcNumbers, defaults.ProcIndirect, defaults.AimMarker, defaults.CardTabs, defaults.PlainTabs, defaults.CrossedTabs,
                defaults.Chances, defaults.DeathChance, defaults.IntentOdds, defaults.EventOdds, defaults.EventInspect, true),
            (settings.ProcNumbers, settings.ProcIndirect, settings.AimMarker, settings.CardTabs, settings.PlainTabs, settings.CrossedTabs,
                settings.Chances, settings.DeathChance, settings.IntentOdds, settings.EventOdds, settings.EventInspect, settings.TabOwn(QolSettings.DrawChanceKey)));
    }

    [Fact]
    public void QuickSetupHasEveryShownFeatureOnceInAGroupOnItsPage()
    {
        // Decision #186: Quick setup is organized like the pages: each group's features on one page, each shown feature in the card tag
        // levels or one group. Prediction Progress shows only while a prediction is still working, so it has no switch there.
        var grouped = SettingsMenu.SetupGroups.SelectMany(group => group.Features).ToList();
        Assert.Equal(grouped.Count, grouped.Distinct().Count());
        Assert.Empty(grouped.Intersect(SettingsMenu.CardTabFeatures));
        var where = SettingsMenu.FeatureRows.ToDictionary(featureRow => featureRow.Id, featureRow => featureRow.Where);
        foreach (var group in SettingsMenu.SetupGroups)
            Assert.Single(group.Features.Select(id => SettingsMenu.PageOf(where[id])).Distinct());
        var shown = SettingsMenu.FeatureRows.Where(featureRow => SettingsMenu.PageOf(featureRow.Where) != SettingsMenu.FeaturePage.General).Select(featureRow => featureRow.Id);
        Assert.Equal(new[] { "exploration-progress" }, shown.Except(grouped).Except(SettingsMenu.CardTabFeatures));
    }

    [Fact]
    public void EachSectionsFeaturesAreTogetherInPageOrder()
    {
        // The menu and the Guide follow FeatureRows: a section's features next to each other, the sections in the order of the pages.
        var sections = SettingsMenu.FeatureRows.Select(featureRow => featureRow.Where).ToList();
        for (var i = 1; i < sections.Count; i++) Assert.True(sections[i] >= sections[i - 1], $"{SettingsMenu.FeatureRows[i].Id} is out of its section's place");
    }
}
