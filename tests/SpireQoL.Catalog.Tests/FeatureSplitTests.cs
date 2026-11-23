using SpireQoL.Data;
using SpireQoL.Diagnostics;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #185: Ethereal Fade came out of Keyword Tags (card-indicators), and the four reminders outside fights out of run-reminders.
/// On the first load, a new feature with no saved switch starts as the one it came out of was; each place's old group switch becomes
/// its relics' switches.
/// </summary>
public class FeatureSplitTests
{
    private static readonly string[] Reminders =
    {
        RewardTagsFeature.FeatureId, EventRemindersFeature.FeatureId, ShopRemindersFeature.FeatureId, MapRemindersFeature.FeatureId,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EtherealFadeStartsAsKeywordTagsWas(bool keywordTags)
    {
        var features = new Dictionary<string, bool> { ["card-indicators"] = keywordTags };
        QolSettings.CarryOverSplitFeatures(features);
        Assert.Equal(keywordTags, features[EtherealFadeFeature.FeatureId]);
        Assert.Equal(keywordTags, features["card-indicators"]);
    }

    [Fact]
    public void EveryReminderStartsAsRunRemindersWasAndTheOldSwitchGoes()
    {
        var features = new Dictionary<string, bool> { [QolSettings.RetiredRunReminders] = false };
        QolSettings.CarryOverSplitFeatures(features);
        foreach (var id in Reminders) Assert.False(features[id], id);
        Assert.False(features.ContainsKey(QolSettings.RetiredRunReminders));
    }

    [Fact]
    public void ASavedSwitchIsKept()
    {
        var features = new Dictionary<string, bool>
        {
            ["card-indicators"] = false, [EtherealFadeFeature.FeatureId] = true,
            [QolSettings.RetiredRunReminders] = false, [MapRemindersFeature.FeatureId] = true,
        };
        QolSettings.CarryOverSplitFeatures(features);
        Assert.True(features[EtherealFadeFeature.FeatureId]);
        Assert.True(features[MapRemindersFeature.FeatureId]);
        Assert.False(features[ShopRemindersFeature.FeatureId]);
    }

    [Fact]
    public void NothingSavedLeavesTheDefaults()
    {
        // A new player: no switch saved for the old features, so the new ones aren't written either (each starts at its default).
        var features = new Dictionary<string, bool> { ["death-skull"] = false };
        QolSettings.CarryOverSplitFeatures(features);
        Assert.Equal(new[] { "death-skull" }, features.Keys);
    }

    [Fact]
    public void EachSplitIsAFeatureAndComesOutOfOneOrTheRetiredId()
    {
        var ids = CatalogCheck.FeatureIds().ToHashSet();
        Assert.DoesNotContain(QolSettings.RetiredRunReminders, ids);
        foreach (var (id, from) in QolSettings.SplitFeatures)
        {
            Assert.Contains(id, ids);
            Assert.True(ids.Contains(from) || from == QolSettings.RetiredRunReminders, $"{id} comes out of {from}, which is neither a feature nor retired");
        }
        Assert.Equal(Reminders.Order(), QolSettings.SplitFeatures.Where(split => split.From == QolSettings.RetiredRunReminders).Select(split => split.Id).Order());
    }

    [Fact]
    public void APlaceSwitchedOffBecomesItsRelicsSwitchedOff()
    {
        var settings = new QolSettings();
        settings.Tabs[ChoiceReminders.Key(ReminderPlace.Event, "*")] = false;
        settings.Tabs[ChoiceReminders.Key(ReminderPlace.Shop, "*")] = true;
        settings.Tabs[ChoiceReminders.Key(ReminderPlace.Reward, "BingBong")] = true;
        settings.FoldReminderPlaceSwitches(place => place == ReminderPlace.Event ? new[] { "BingBong", "Sozu" } : new[] { "BingBong" });
        Assert.False(settings.Tabs[ChoiceReminders.Key(ReminderPlace.Event, "BingBong")]);
        Assert.False(settings.Tabs[ChoiceReminders.Key(ReminderPlace.Event, "Sozu")]);
        Assert.True(settings.Tabs[ChoiceReminders.Key(ReminderPlace.Reward, "BingBong")]);
        Assert.False(settings.Tabs.ContainsKey(ChoiceReminders.Key(ReminderPlace.Shop, "BingBong")));
        foreach (var place in Enum.GetValues<ReminderPlace>()) Assert.False(settings.Tabs.ContainsKey(ChoiceReminders.Key(place, "*")), place.ToString());
    }
}
