using SpireQoL.Menu;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Parent and child in the settings menu (decision #187): every option that only means something while another switch is on names
/// that switch, so it sits indented under it and greys out while it's off (the menu reads both from the option's entry); every family
/// switch heads its sources on Tags by source.
/// </summary>
public class SettingsOptionTests
{
    private static readonly HashSet<string> FeatureIds = SettingsMenu.FeatureRows.Select(featureRow => featureRow.Id).ToHashSet();

    /// <summary>Every MenuOption field on the menu (the table the menu builds its option rows from).</summary>
    private static List<MenuOption> OptionFields() => typeof(SettingsMenu)
        .GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
        .Where(field => field.FieldType == typeof(MenuOption))
        .Select(field => (MenuOption)field.GetValue(null)!)
        .ToList();

    [Fact]
    public void EveryOptionHasItsOwnId()
    {
        var options = OptionFields();
        Assert.True(options.Count >= 15, $"only {options.Count} options found"); // the reflection found the table
        Assert.Equal(options.Count, SettingsMenu.Options.Count);
        var clash = options.Where(option => FeatureIds.Contains(option.Id)).Select(option => option.Id).ToList();
        Assert.True(clash.Count == 0, "Option ids that are also feature ids: " + string.Join(", ", clash));
    }

    [Fact]
    public void EveryNeedIsASwitch()
    {
        // A need is a feature or an option with an on/off value (not a slider), and never the option itself.
        var problems = new List<string>();
        foreach (var option in SettingsMenu.Options.Values)
        {
            foreach (var id in option.Needs.SelectMany(group => group))
            {
                if (id == option.Id) problems.Add($"{option.Name} needs itself");
                else if (!FeatureIds.Contains(id) && !(SettingsMenu.Options.TryGetValue(id, out var parent) && parent.Value != null))
                    problems.Add($"{option.Name} needs {id}, which is no feature or switch");
            }
            if (option.Needs.Any(group => group.Length == 0)) problems.Add($"{option.Name} has an empty group of needs");
            if (option.IsMaster && option.Value == null) problems.Add($"{option.Name} is a slider with nothing it depends on");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void NeedsHaveNoLoops()
    {
        foreach (var option in SettingsMenu.Options.Values)
        {
            var seen = new HashSet<string>();
            var next = new Queue<string>(new[] { option.Id });
            while (next.Count > 0)
            {
                var id = next.Dequeue();
                if (!SettingsMenu.Options.TryGetValue(id, out var current)) continue;
                foreach (var need in current.Needs.SelectMany(group => group))
                {
                    Assert.False(need == option.Id, $"{option.Name} needs itself through {current.Name}");
                    if (seen.Add(need)) next.Enqueue(need);
                }
            }
        }
    }

    /// <summary>Whether an option needs every one of <paramref name="switchIds"/> (each in some group of its needs).</summary>
    private static bool NeedsAll(MenuOption option, params string[] switchIds) =>
        switchIds.All(id => option.Needs.Any(group => group.Contains(id)));

    [Fact]
    public void OptionsSitUnderWhatTheyNeed()
    {
        // The owner's list (decision #187): each of these only means something while these switches are on.
        var tags = SettingsMenu.TagsOnCards.Id;
        var chances = SettingsMenu.ShowChances.Id;
        Assert.True(NeedsAll(SettingsMenu.AmountsOnProcTags, tags, "proc-indicators"));
        Assert.True(NeedsAll(SettingsMenu.IndirectProcs, tags, "proc-indicators"));
        Assert.True(NeedsAll(SettingsMenu.InspectEventCards, Features.EventRemindersFeature.FeatureId));
        Assert.True(NeedsAll(SettingsMenu.EventChance, chances, Features.EventRemindersFeature.FeatureId));
        Assert.True(NeedsAll(SettingsMenu.EtherealOpacity, Features.EtherealFadeFeature.FeatureId));
        Assert.True(NeedsAll(SettingsMenu.TrackerFlashesEarly, "relic-counters"));
        Assert.True(NeedsAll(SettingsMenu.TrackerFlashSpeed, "relic-counters"));
        Assert.True(NeedsAll(SettingsMenu.TeammateHands, SettingsMenu.MultiplayerPredictions.Id));
        Assert.True(NeedsAll(SettingsMenu.TargetMarker, tags));
        // Plain and crossed choose which kinds show on every feature with tags: under Tags on cards, and on while potion tags show.
        foreach (var kind in new[] { SettingsMenu.PlainTags, SettingsMenu.CrossedTags })
            Assert.Contains(kind.Needs, group => group.Contains(tags) && group.Contains("potion-tabs"));
        // Every chance needs Show chances.
        foreach (var chance in new[] { SettingsMenu.DeathChances, SettingsMenu.MostLikely, SettingsMenu.IntentRandomMoves, SettingsMenu.EventChance })
            Assert.True(NeedsAll(chance, chances), $"{chance.Name} doesn't need Show chances");
        // The masters stand alone.
        foreach (var master in new[] { SettingsMenu.TagsOnCards, SettingsMenu.ShowChances, SettingsMenu.MultiplayerPredictions, SettingsMenu.SendErrorReports, SettingsMenu.DevMode })
            Assert.True(master.IsMaster, $"{master.Name} isn't a master switch");
    }

    [Fact]
    public void EveryKindOfTagNeedsWhatShowsIt()
    {
        foreach (var tabEntry in SettingsCatalog.CardTabs.Concat(SettingsCatalog.TabGroups))
        {
            var needs = SettingsMenu.TagNeeds(tabEntry);
            Assert.Contains(needs, group => group.Contains(tabEntry.Feature));
            Assert.Equal(tabEntry.OnCards, needs.Any(group => group.Contains(SettingsMenu.TagsOnCards.Id)));
            Assert.Equal(tabEntry.Chance, needs.Any(group => group.Contains(SettingsMenu.ShowChances.Id)));
        }
        Assert.True(SettingsCatalog.CardTabs.Single(tabEntry => tabEntry.Key == QolSettings.DrawChanceKey).Chance, "the draw chance tag isn't a chance");
    }

    [Fact]
    public void EveryTagIsInAFamilyWithItsSwitchOnItsList()
    {
        // Each source's tag sits under its family's switch on Tags by source; each family has sources, and a family over several lists
        // (proc tags: relics, your powers, enemy powers) heads each of them.
        var tagged = SettingsCatalog.Sources.Where(sourceEntry => sourceEntry.TagKey != null).ToList();
        var orphans = tagged.Where(sourceEntry => SettingsCatalog.FamilyOf(sourceEntry) == null).Select(sourceEntry => sourceEntry.Type).ToList();
        Assert.True(orphans.Count == 0, "Tags with no family switch: " + string.Join(", ", orphans));
        foreach (var family in SettingsCatalog.TabGroups)
            Assert.True(tagged.Any(sourceEntry => SettingsCatalog.FamilyOf(sourceEntry) == family), $"{family.Name} has no sources");
        var procLists = tagged.Where(sourceEntry => SettingsCatalog.FamilyOf(sourceEntry)?.Key == "proc:*").Select(sourceEntry => sourceEntry.Group).ToHashSet();
        Assert.Superset(new HashSet<SourceGroup> { SourceGroup.Relic, SourceGroup.YourPower, SourceGroup.EnemyPower }, procLists);
        Assert.False(SettingsCatalog.TabGroups.Single(family => family.Key == "potion:*").OnCards);
    }
}
