using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Map Reminders' rooms read from each relic's and card's own hooks (decision #374, Data/RoomReadings), on the installed game and on each branch's code:
/// the rows they show on, crossed or not, what ends them, against the table they were written as by hand and against Snapshots/room-readings.txt.
/// </summary>
[Collection(GameFightCollection.Name)]
public class RoomReadingsTests
{
    /// <summary>The crossed rows come from the relics' choice readings, read with the game's cards made.</summary>
    public RoomReadingsTests() => GameModels.EnsureLoaded();

    /// <summary>
    /// The map legend's rows as they were written by hand from each one's hook bodies (decision #65's table, ChoiceReminders.MapRules until decision
    /// #374), kept as the check that the reading gives each: a reading that drifts from what the code does, or a new relic with a row, shows here.
    /// </summary>
    private static readonly Dictionary<string, (PathRoom Rooms, PathRoom Crossed)> AsWritten = new()
    {
        // Shops: buying anything ends Maw Bank's gold per room (AfterItemPurchased sets HasItemBeenBought).
        ["MawBank"] = (PathRoom.Shop, PathRoom.Shop),
        // Rest-site heals.
        ["RegalPillow"] = (PathRoom.RestSite, PathRoom.None), ["StoneHumidifier"] = (PathRoom.RestSite, PathRoom.None),
        ["DreamCatcher"] = (PathRoom.RestSite, PathRoom.None), ["TinyMailbox"] = (PathRoom.RestSite, PathRoom.None),
        // Elites: TryModifyRewards, AfterRoomEntered, turn hooks, AfterCombatVictory on RoomType.Elite.
        ["BlackStar"] = (PathRoom.Elite, PathRoom.None), ["WhiteStar"] = (PathRoom.Elite, PathRoom.None), ["SlingOfCourage"] = (PathRoom.Elite, PathRoom.None),
        ["BoomingConch"] = (PathRoom.Elite, PathRoom.None), ["TormentousCollar"] = (PathRoom.Elite, PathRoom.None), ["SwordOfStone"] = (PathRoom.Elite, PathRoom.None),
        ["WarHammer"] = (PathRoom.Elite, PathRoom.None),
        // Monsters: an extra card reward (Prayer Wheel), an upgrade every few monster fights (Fishing Rod).
        ["PrayerWheel"] = (PathRoom.Monster, PathRoom.None), ["FishingRod"] = (PathRoom.Monster, PathRoom.None),
        // Rest sites: a heal on entering, energy next fight, rest-site options, Miniature Tent.
        ["EternalFeather"] = (PathRoom.RestSite, PathRoom.None), ["VenerableTeaSet"] = (PathRoom.RestSite, PathRoom.None),
        ["FakeVenerableTeaSet"] = (PathRoom.RestSite, PathRoom.None), ["Girya"] = (PathRoom.RestSite, PathRoom.None), ["Shovel"] = (PathRoom.RestSite, PathRoom.None),
        ["MeatCleaver"] = (PathRoom.RestSite, PathRoom.None), ["PaelsGrowth"] = (PathRoom.RestSite, PathRoom.None), ["PumpkinCandle"] = (PathRoom.RestSite, PathRoom.None),
        ["Wuppy"] = (PathRoom.RestSite, PathRoom.None), ["Byrdpip"] = (PathRoom.RestSite, PathRoom.None), ["MiniatureTent"] = (PathRoom.RestSite, PathRoom.None),
        ["ByrdonisEgg"] = (PathRoom.RestSite, PathRoom.None),
        // ? rooms: a heal on entering one (Planisphere), Dowsing's quest, what a ? can be.
        ["Planisphere"] = (PathRoom.Unknown, PathRoom.None), ["Dowsing"] = (PathRoom.Unknown, PathRoom.None), ["JuzuBracelet"] = (PathRoom.Unknown, PathRoom.None),
        ["GoldenCompass"] = (PathRoom.Unknown, PathRoom.None), ["LanternKey"] = (PathRoom.Unknown, PathRoom.None),
        // Shops: a heal on entering, buys everything on entering, cheaper prices.
        ["MealTicket"] = (PathRoom.Shop, PathRoom.None), ["LordsParasol"] = (PathRoom.Shop, PathRoom.None), ["MembershipCard"] = (PathRoom.Shop, PathRoom.None),
        ["TheCourier"] = (PathRoom.Shop, PathRoom.None),
    };

    private static List<Type> Models() => RoomReadings.ModelTypes(typeof(RelicModel).Assembly).OrderBy(type => type.Name, StringComparer.Ordinal).ToList();

    [Fact]
    public void EveryMapRowIsTheOneWrittenFromItsCode()
    {
        var wrong = new List<string>();
        foreach (var type in Models())
        {
            var rule = ChoiceReminders.For(type.Name);
            var read = (rule?.Map ?? PathRoom.None, rule?.MapCrossed ?? PathRoom.None);
            var written = AsWritten.TryGetValue(type.Name, out var known) ? known : (PathRoom.None, PathRoom.None);
            if (read != written) wrong.Add($"{type.Name}: read {read}, written {written} ({string.Join("; ", RoomReadings.For(type.Name)?.Notes ?? Array.Empty<string>())})");
        }
        Assert.True(wrong.Count == 0, "Map rows that aren't the ones written from the code:\n" + string.Join("\n", wrong));
        Assert.Equal(AsWritten.Keys.Order(StringComparer.Ordinal), ChoiceReminders.Rules.Where(rule => rule.Map != PathRoom.None).Select(rule => rule.Relic).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// What ends a row and when a ? row shows, read from the code: Maw Bank's until its HasItemBeenBought is set (its gold per room needs it unset, and a
    /// purchase sets it); Golden Compass, Juzu Bracelet and Lantern Key only change what a ? can be (shown while that changes); Dowsing also counts the ?
    /// rooms you enter (always shown); Planisphere heals on entering one.
    /// </summary>
    [Fact]
    public void WhatEndsARowIsReadFromTheCode()
    {
        Assert.Equal(new[] { "HasItemBeenBought" }, RoomReadings.For("MawBank")!.Until);
        foreach (var model in new[] { "GoldenCompass", "JuzuBracelet", "LanternKey" }) Assert.True(RoomReadings.For(model)!.WhileChanging, model);
        foreach (var model in new[] { "Dowsing", "Planisphere" }) Assert.False(RoomReadings.For(model)!.WhileChanging, model);
        Assert.Empty(RoomReadings.For("BlackStar")!.Until);
    }

    /// <summary>
    /// Why a hook that tests a room gives no row, read from it: Bronze Scales acts in every fight, Lava Rock and Amethyst Aubergine on the boss's rewards
    /// or every fight's, Fur Coat and Winged Boots change what the map shows, Fresnel Lens changes shop cards as every card you get, The Hunt's is its play.
    /// </summary>
    [Theory]
    [InlineData("BronzeScales", "every fight")]
    [InlineData("LavaRock", "the boss")]
    [InlineData("AmethystAubergine", "every fight")]
    [InlineData("WingedBoots", "the map shows it")]
    [InlineData("FresnelLens", "like every card you get")]
    [InlineData("TheHunt", "its play")]
    public void AHookThatTestsARoomButGivesNoRowSaysWhy(string model, string why)
    {
        var reading = RoomReadings.For(model)!;
        Assert.Equal(PathRoom.None, reading.Rooms);
        Assert.Contains(reading.Notes, note => note.Contains(why, StringComparison.Ordinal));
    }

    /// <summary>A rest site's smith (AfterRestSiteSmith, decision #371): a relic that reacts to it acts at rest sites, its hook read as the setting table's.</summary>
    [Fact]
    public void ASmithsReactionIsARestSite()
    {
        var reading = RoomReadings.Read(typeof(TestRelics.SmithsToken), SpireQoL.Procs.Programs.RuntimeCode.Read, _ => null);
        Assert.Equal(PathRoom.RestSite, reading.Rooms);
    }

    public static TheoryData<string> Branches() => new() { "beta", "main" };

    /// <summary>Every relic's and card's room reading on a branch's code, for every model with a hook it reads.</summary>
    private static List<string> Readings(BranchCode code)
    {
        var cards = ModelDb.AllCards.ToList();
        var relics = RelicReadings.RelicTypes(code.Assembly).GroupBy(type => type.Name).ToDictionary(group => group.Key, group => group.First());
        var choices = new Dictionary<string, RelicReading?>();
        RelicReading? Choices(string name)
        {
            if (!choices.TryGetValue(name, out var reading))
                choices[name] = reading = relics.TryGetValue(name, out var type) ? RelicReadings.Read(type, code.Read, () => cards, key => RelicReadings.LoadedValue(name, key)) : null;
            return reading;
        }
        var lines = new List<string>();
        foreach (var type in RoomReadings.ModelTypes(code.Assembly).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            var reading = RoomReadings.Read(type, code.Read, Choices);
            if (reading.Hooks.Count == 0) continue;
            var rule = new List<string>();
            if (reading.Rooms != PathRoom.None) rule.Add("rooms " + reading.Rooms);
            if (reading.Crossed != PathRoom.None) rule.Add("crossed " + reading.Crossed);
            if (reading.Until.Count > 0) rule.Add("until " + string.Join(", ", reading.Until));
            if (reading.WhileChanging) rule.Add("while it changes a ?");
            if (ChoiceReminders.MapReviewed.TryGetValue(type.Name, out var reason)) rule.Add("reviewed: " + reason);
            lines.Add($"{type.Name}: {(rule.Count == 0 ? "no row" : string.Join(" | ", rule))} || {string.Join(" | ", reading.Notes)}");
        }
        return lines;
    }

    /// <summary>
    /// Every room reading on each branch's code, against Snapshots/room-readings.txt (beta) and room-readings-main.txt (main): what each read hook came to,
    /// so a game update or a reader change that reads a room differently shows here.
    /// </summary>
    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryRoomReadingIsTheSnapshot(string branch)
    {
        var assembly = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(assembly)) return; // no decompile of this branch here
        var lines = Readings(BranchCode.Of(assembly));
        var path = Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Catalog.Tests", "Snapshots", branch == "main" ? "room-readings-main.txt" : "room-readings.txt");
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            return;
        }
        Assert.True(File.Exists(path), $"missing {path}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd().Split('\n');
        var changed = lines.Except(expected).Concat(expected.Except(lines).Select(line => "was: " + line)).Take(10).ToList();
        Assert.True(changed.Count == 0, "room readings changed (regenerate with SPIREQOL_UPDATE_SNAPSHOT=1 and explain):\n" + string.Join("\n", changed));
    }

    /// <summary>Nothing a room hook does is left unread: a hook the program reader can't read is a gap to close.</summary>
    [Fact]
    public void NoRoomReadingHasAGap()
    {
        var gaps = Models().Select(type => RoomReadings.For(type.Name)).OfType<RoomReading>()
            .SelectMany(reading => reading.Notes.Where(note => note.Contains("? ", StringComparison.Ordinal)).Select(note => reading.Model + ": " + note)).ToList();
        Assert.True(gaps.Count == 0, "Room readings with something unread:\n" + string.Join("\n", gaps));
    }
}
