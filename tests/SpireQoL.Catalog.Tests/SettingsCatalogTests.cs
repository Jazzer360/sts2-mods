using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The settings check (Diagnostics/CatalogCheck.cs) before every release: every proc rule, tracker row, double-play source,
/// Run Reminders rule and feature has its row in the settings menu (Menu/SettingsCatalog.cs, SettingsMenu.FeatureRows),
/// and every row has something behind it. It reads the compiled mod and the installed game, so it runs here rather than
/// in a player's game (decision #46).
/// </summary>
[Collection(GameFightCollection.Name)]
public class SettingsCatalogTests
{
    /// <summary>The relics' rules are read from their code with the game's cards made (Data/RelicReadings).</summary>
    public SettingsCatalogTests() => GameModels.EnsureLoaded();

    [Fact]
    public void EveryRuleHasASettingsRowAndEveryRowARule()
    {
        // The reminders' part is checked only with the game's cards known (in the game it can run before they are): here they always are.
        Assert.True(SpireQoL.Data.RelicReadings.CardsKnown, "the game's cards weren't read: the reminder rules wouldn't be checked");
        var problems = CatalogCheck.Problems();
        Assert.True(problems.Count == 0, "Settings catalog problems:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void FeatureClassesAreFound()
    {
        // The check reads the features from the mod's classes: make sure it finds them (an empty list would pass silently).
        var ids = CatalogCheck.FeatureIds().ToList();
        Assert.Contains("proc-indicators", ids);
        Assert.Contains("rest-preview", ids);
    }
}
