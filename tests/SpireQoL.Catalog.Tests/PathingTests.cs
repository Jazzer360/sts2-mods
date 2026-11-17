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
        .Where(t => !t.IsAbstract && (typeof(RelicModel).IsAssignableFrom(t) || typeof(CardModel).IsAssignableFrom(t))).ToList();

    [Fact]
    public void EveryRoomTestHasAMapRuleOrAReason()
    {
        var missing = Models()
            .Where(t => ChoiceReminders.For(t.Name) is not { Map: not PathRoom.None } && !ChoiceReminders.MapReviewed.ContainsKey(t.Name))
            .Select(t => (t.Name, Hooks: SpireQoL.Diagnostics.CoverageWatch.RoomTestingHooks(t)))
            .Where(x => x.Hooks.Count > 0)
            .Select(x => $"{x.Name}: {string.Join("; ", x.Hooks)}").ToList();
        Assert.True(missing.Count == 0, "Room tests with no Map rule or reason:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void TheScanFindsTheKnownOnes()
    {
        // An empty scan would pass silently: the rules came from these, each read a different way.
        var byName = Models().ToDictionary(t => t.Name);
        foreach (var name in new[] { "BlackStar", "MealTicket", "Planisphere", "EternalFeather", "FishingRod", "Girya", "JuzuBracelet", "Dowsing", "ByrdonisEgg", "MembershipCard" })
            Assert.NotEmpty(SpireQoL.Diagnostics.CoverageWatch.RoomTestingHooks(byName[name]));
    }

    [Fact]
    public void EveryMapRuleAndReasonNamesAModel()
    {
        var names = Models().Select(t => t.Name).ToHashSet();
        var unknown = ChoiceReminders.Rules.Where(r => r.Map != PathRoom.None).Select(r => r.Relic).Concat(ChoiceReminders.MapReviewed.Keys)
            .Where(n => !names.Contains(n)).ToList();
        Assert.Empty(unknown);
        Assert.All(ChoiceReminders.Rules, r => Assert.Equal(PathRoom.None, r.MapCrossed & ~r.Map));
    }
}
