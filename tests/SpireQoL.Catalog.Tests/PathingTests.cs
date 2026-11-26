using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Pathing (decisions #65, #374, docs/PATHING.md): every hook of a relic or card in the installed game that acts on a kind of room (a map hook, or code
/// that reads a room's type) is read by its room reading (Data/RoomReadings), which gives its row or says why it has none. The scan reads the compiled
/// code apart from the reading, so a hook new in a game update that the reading doesn't read fails here.
/// </summary>
[Collection(GameFightCollection.Name)]
public class PathingTests
{
    /// <summary>The relics' rules are read from their code with the game's cards made (Data/RelicReadings).</summary>
    public PathingTests() => GameModels.EnsureLoaded();

    private static List<Type> Models() => typeof(RelicModel).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && (typeof(RelicModel).IsAssignableFrom(type) || typeof(CardModel).IsAssignableFrom(type))).ToList();

    [Fact]
    public void EveryRoomTestIsReadByTheRoomReading()
    {
        var missing = Models()
            .Select(type => (type.Name, Hooks: SpireQoL.Diagnostics.CoverageWatch.RoomTestingHooks(type)
                .Where(hook => RoomReadings.For(type.Name) is not { } reading || !reading.Hooks.Contains(hook[..hook.IndexOf(':')])).ToList()))
            .Where(nameHooks => nameHooks.Hooks.Count > 0)
            .Select(nameHooks => $"{nameHooks.Name}: {string.Join("; ", nameHooks.Hooks)}").ToList();
        Assert.True(missing.Count == 0, "Room tests the room reading doesn't read:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void TheScanFindsTheKnownOnes()
    {
        // An empty scan would pass silently: the rules came from these, each read a different way.
        var byName = Models().ToDictionary(type => type.Name);
        foreach (var name in new[] { "BlackStar", "MealTicket", "Planisphere", "EternalFeather", "FishingRod", "Girya", "JuzuBracelet", "Dowsing", "ByrdonisEgg", "MembershipCard" })
            Assert.NotEmpty(SpireQoL.Diagnostics.CoverageWatch.RoomTestingHooks(byName[name]));
    }

    [Fact]
    public void EveryMapRuleAndReasonNamesAModel()
    {
        var names = Models().Select(type => type.Name).ToHashSet();
        var unknown = ChoiceReminders.Rules.Where(reminderRule => reminderRule.Map != PathRoom.None).Select(reminderRule => reminderRule.Relic).Concat(ChoiceReminders.MapReviewed.Keys)
            .Where(name => !names.Contains(name)).ToList();
        Assert.Empty(unknown);
        Assert.All(ChoiceReminders.Rules, reminderRule => Assert.Equal(PathRoom.None, reminderRule.MapCrossed & ~reminderRule.Map));
    }

    /// <summary>A reviewed reason is for a model whose reading gives a row it shouldn't show (Silver Crucible's chest): one with no row needs none.</summary>
    [Fact]
    public void EveryReviewedReasonHidesARow()
    {
        var idle = ChoiceReminders.MapReviewed.Keys.Where(name => RoomReadings.For(name) is not { Rooms: not PathRoom.None }).ToList();
        Assert.True(idle.Count == 0, "Reviewed map reasons for a model whose reading gives no row: " + string.Join(", ", idle));
    }
}
