using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Pathing (decision #65, docs/PATHING.md): every relic and card in the installed game whose hooks act on a kind of room (a map
/// hook, or code that reads a room's type) has a Map rule (Data/ChoiceReminders) or a reviewed reason (ChoiceReminders.MapReviewed).
/// Read from the compiled code, so ones new in a game update fail here.
/// </summary>
public class PathingTests
{
    private static List<Type> Models() => typeof(RelicModel).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && (typeof(RelicModel).IsAssignableFrom(type) || typeof(CardModel).IsAssignableFrom(type))).ToList();

    [Fact]
    public void EveryRoomTestHasAMapRuleOrAReason()
    {
        var missing = Models()
            .Where(type => ChoiceReminders.For(type.Name) is not { Map: not PathRoom.None } && !ChoiceReminders.MapReviewed.ContainsKey(type.Name))
            .Select(type => (type.Name, Hooks: SpireQoL.Diagnostics.CoverageWatch.RoomTestingHooks(type)))
            .Where(x => x.Hooks.Count > 0)
            .Select(x => $"{x.Name}: {string.Join("; ", x.Hooks)}").ToList();
        Assert.True(missing.Count == 0, "Room tests with no Map rule or reason:\n" + string.Join("\n", missing));
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
            .Where(n => !names.Contains(n)).ToList();
        Assert.Empty(unknown);
        Assert.All(ChoiceReminders.Rules, reminderRule => Assert.Equal(PathRoom.None, reminderRule.MapCrossed & ~reminderRule.Map));
    }
}
