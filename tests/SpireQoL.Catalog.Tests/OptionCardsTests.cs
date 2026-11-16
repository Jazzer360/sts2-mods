using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Run Reminders reads which cards an event option can give from its code (docs/CARD_ODDS.md). Read against every event in
/// the installed game: rewards whose pool isn't in their code are the reviewed ones, and the known filters are found.
/// </summary>
public class OptionCardsTests
{
    private static RunRemindersFeature.OptionCards Read(Type eventType)
    {
        var cards = new RunRemindersFeature.OptionCards();
        var types = new[] { eventType }.Concat(eventType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
        foreach (var t in types)
        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (m.IsAbstract) continue;
            try { cards.Read(PatchProcessor.GetOriginalInstructions(m)); } catch { /* no body */ }
        }
        return cards;
    }

    private static IEnumerable<Type> Events() => typeof(EventModel).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(EventModel).IsAssignableFrom(t));

    [Fact]
    public void OnlyReviewedEventsTakeTheirPoolFromTheOption()
    {
        // A reward with no pool in its code takes the CardPoolModel the option's delegate holds. That's how Colorful
        // Philosophers passes each character's pool; a new event doing it needs a look (does its delegate hold the pool?).
        var reviewed = new[] { "ColorfulPhilosophers" };
        var found = Events().Where(t => Read(t).Summary.PoolFromOption > 0).Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(reviewed, found);
    }

    [Fact]
    public void KnownEventsAreReadAsTheyAre()
    {
        var byName = Events().ToDictionary(t => t.Name);
        // Study (Powers) and Touch Core (a cost filter): two rewards from your pool, each with its filter.
        var automaton = Read(byName["InfestedAutomaton"]).Summary;
        Assert.Equal(2, automaton.Rewards);
        Assert.Equal(2, automaton.Filters);
        // Gorge: commons from your pool.
        Assert.True(Read(byName["RoomFullOfCheese"]).Summary.Filters >= 1);
        // Colorful Philosophers: a common, an uncommon and a rare reward from the pool the option passes in.
        var philosophers = Read(byName["ColorfulPhilosophers"]).Summary;
        Assert.Equal(3, philosophers.Rewards);
        Assert.Equal(3, philosophers.Filters);
        // Wuppy in Trouble: an Injury, a fixed card.
        Assert.True(Read(byName["WuppyInTrouble"]).Summary.Fixed >= 1);
        // Some event transforms a card at random.
        Assert.Contains(Events(), t => Read(t).Summary.Transform);
    }
}
