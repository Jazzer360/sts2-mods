using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The Run Reminders rules of every relic in the installed game (Data/ChoiceReminders, Data/CardAffinity), against Snapshots/reminder-rules.txt
/// (regenerated with SPIREQOL_UPDATE_SNAPSHOT=1): where each relic's icon shows, plain or crossed, what the check expects of it, and which of the
/// game's cards (as you'd get them) its card rule applies to. A change to a rule, or to what a rule finds in a game update, shows here relic by relic.
/// </summary>
[Collection(GameFightCollection.Name)]
public class ReminderRulesTests
{
    private static List<Type> Relics() => typeof(RelicModel).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && typeof(RelicModel).IsAssignableFrom(type)).OrderBy(type => type.Name, StringComparer.Ordinal).ToList();

    private static string Cards(IEnumerable<CardModel> cards, Func<CardModel, bool> test)
    {
        var names = cards.Where(test).Select(card => card.GetType().Name).Order(StringComparer.Ordinal).ToList();
        return names.Count + (names.Count > 0 ? ": " + string.Join(", ", names) : "");
    }

    /// <summary>Each relic's rule as one line: what the snapshot holds.</summary>
    internal static List<string> Lines()
    {
        GameModels.EnsureLoaded();
        var cards = ModelDb.AllCards.ToList();
        var lines = new List<string>();
        foreach (var type in Relics())
        {
            var name = type.Name;
            var parts = new List<string>();
            if (ChoiceReminders.For(name) is { } rule)
            {
                parts.Add("on " + rule.On);
                if (rule.CrossedOn != ChoiceEffect.None) parts.Add("crossed on " + rule.CrossedOn);
                if (rule.OnCardReward) parts.Add("on card rewards");
                if (rule.Expect != ChoiceEffect.None) parts.Add("expects " + rule.Expect);
                if (!rule.ShopIcon) parts.Add("no shop icon");
                if (rule.CardTab) parts.Add("card tab");
                if (rule.Map != PathRoom.None) parts.Add("map " + rule.Map + (rule.MapCrossed != PathRoom.None ? " crossed " + rule.MapCrossed : ""));
                parts.Add("shows at " + string.Join(",", Enum.GetValues<ReminderPlace>().Where(rule.In)));
            }
            if (CardAffinity.For(name) is { } card)
            {
                parts.Add($"card rule {card.Kind}" + (card.Change != CardChange.None ? " " + card.Change : ""));
                parts.Add("applies to " + Cards(cards, candidate => CardAffinity.AppliesTo(card, candidate)));
                if (card.Base is { } test) parts.Add("base " + Cards(cards, candidate => test(candidate)));
            }
            if (parts.Count > 0) lines.Add(name + ": " + string.Join(" | ", parts));
        }
        return lines;
    }

    [Fact]
    public void EveryRelicsRuleIsTheSnapshot()
    {
        var lines = Lines();
        var path = Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Catalog.Tests", "Snapshots", "reminder-rules.txt");
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            return;
        }
        Assert.True(File.Exists(path), $"missing {path}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd().Split('\n');
        var changed = lines.Except(expected).Concat(expected.Except(lines).Select(line => "was: " + line)).Take(10).ToList();
        Assert.True(changed.Count == 0, "reminder rules changed (regenerate with SPIREQOL_UPDATE_SNAPSHOT=1 and explain):\n" + string.Join("\n", changed));
    }
}
