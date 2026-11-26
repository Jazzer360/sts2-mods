using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;
using SpireQoL.Data;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The cards every event option and every relic's obtaining can give, as the card reading works them out (decision #353) for a new Ironclad run: each
/// draw the Inspect screen lists (its kind, odds, count and cards), every card it can give, what it changes in your deck, and the chances the card
/// relics' icons show. Snapshots/option-cards.txt (SPIREQOL_UPDATE_SNAPSHOT=1 regenerates) holds them, so a change in how an option's cards are read
/// shows option by option.
/// </summary>
[Collection(GameFightCollection.Name)]
public class OptionCardsSnapshotTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static IEnumerable<Type> Events() => typeof(EventModel).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && !type.IsNested && typeof(EventModel).IsAssignableFrom(type) && type.Namespace?.EndsWith(".Mocks", StringComparison.Ordinal) != true)
        .OrderBy(type => type.Name, StringComparer.Ordinal);

    /// <summary>What the option's handler runs on: the event's canonical model, or a closure over it (made without its constructor); null for a static lambda's.</summary>
    private static object? TargetOf(MethodBase handler, EventModel? canonical)
    {
        if (handler.IsStatic || handler.DeclaringType is not { } declaring) return null;
        if (canonical != null && declaring.IsInstanceOfType(canonical)) return canonical;
        if (declaring.IsGenericTypeDefinition) return null;
        var closure = RuntimeHelpers.GetUninitializedObject(declaring);
        if (declaring.GetFields(Instance).FirstOrDefault(field => field.Name.EndsWith("__this", StringComparison.Ordinal)) is { } outer && canonical != null && outer.FieldType.IsInstanceOfType(canonical))
            outer.SetValue(closure, canonical);
        return closure;
    }

    private static string Cards(IEnumerable<CardModel> cards)
    {
        var names = cards.Select(card => card.GetType().Name).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();
        if (names.Count <= 6) return names.Count + " [" + string.Join(",", names) + "]";
        var hash = 2166136261u;
        foreach (var character in string.Join(",", names)) hash = (hash ^ character) * 16777619u;
        return names.Count + " #" + hash.ToString("x8");
    }

    private static string Chance((double Low, double High)? chance) =>
        chance is { } known ? (Math.Abs(known.Low - known.High) < 1e-9 ? known.Low.ToString("0.####") : known.Low.ToString("0.####") + "-" + known.High.ToString("0.####")) : "?";

    private static string Describe(RunReminders.OptionCards cards, Player me)
    {
        var parts = new List<string>();
        var groups = cards.Groups(me);
        parts.Add("groups " + (groups == null ? "?" : string.Join(" + ", groups.Select(group =>
            $"{group.Kind}/{group.Odds}/{group.Count?.ToString() ?? "?"}{(group.Upgraded ? "/up" : "")}: {Cards(group.Cards)}"))));
        var candidates = cards.Candidates(me);
        parts.Add("cards " + (candidates == null ? "?" : Cards(candidates)));
        var changes = cards.ChangesRead;
        parts.Add($"changes [{changes.Enchantments}]{(changes.GivesUpgraded ? " gives upgraded" : "")}{(changes.UpgradesDeck ? " upgrades deck" : "")}{(cards.PicksTransform ? " picks transform" : "")}");
        var cannon = CardAffinity.For("MiniatureCannon")!;
        var lighter = CardAffinity.For("MysticLighter")!;
        parts.Add("attack " + Chance(cards.Chance(me, card => card.Type == CardType.Attack)));
        parts.Add("rare " + Chance(cards.Chance(me, card => card.Rarity == CardRarity.Rare)));
        parts.Add("cannon " + Chance(cards.Chance(me, card => CardAffinity.AppliesWith(cannon, card, Array.Empty<CardAffinityRule>(), cards.GivesUpgraded)))
            + (cards.ChangeReaches(me, cannon) ? " reaches" : ""));
        parts.Add("lighter" + (cards.ChangeReaches(me, lighter) ? " reaches" : " no"));
        return string.Join("; ", parts);
    }

    /// <summary>
    /// A new run's Ironclad, as far as the card reading looks: the game's test character (an Ironclad made for a new run reaches the game's engine) with
    /// the Ironclad's class and the cards it starts with (five Strikes, four Defends, a Bash) in its deck.
    /// </summary>
    internal static Player Ironclad()
    {
        var me = Player.CreateForNewRun<Deprived>(MegaCrit.Sts2.Core.Unlocks.UnlockState.all, 1);
        typeof(Player).GetField("<Character>k__BackingField", Instance)!.SetValue(me, ModelDb.Character<Ironclad>());
        var run = RunState.CreateForTest(new[] { me }, seed: "TESTSEED");
        foreach (var card in Enumerable.Repeat<CardModel>(ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad>(), 5)
                     .Concat(Enumerable.Repeat<CardModel>(ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>(), 4)).Append(ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.Bash>()))
            me.Deck.AddInternal(run.CreateCard(card, me));
        return me;
    }

    [Fact]
    public void EveryOptionsCardsAreTheSnapshot()
    {
        GameModels.EnsureLoaded();
        var me = Ironclad();
        var canonical = ModelDb.AllEvents.Concat(ModelDb.AllAncients).GroupBy(model => model.GetType()).ToDictionary(group => group.Key, group => group.First());
        var lines = new List<string>();
        foreach (var eventType in Events())
        {
            var seen = new Dictionary<string, int>();
            foreach (var (handler, relic) in OptionReader.Game.Options(eventType))
            {
                var label = eventType.Name + "." + handler.Name + (relic != null ? " [" + relic.Name + "]" : "");
                seen[label] = seen.GetValueOrDefault(label) + 1;
                if (seen[label] > 1) label += " #" + seen[label];
                var reading = OptionReader.Game.Read(handler, relic);
                var relicModel = relic == null ? null : ModelDb.AllRelics.First(model => model.GetType() == relic);
                var cards = RunReminders.CardsFrom(reading).For(TargetOf(handler, canonical.GetValueOrDefault(eventType)), relicModel, Array.Empty<object>());
                lines.Add(label + ": " + Describe(cards, me));
            }
        }
        foreach (var relic in typeof(RelicModel).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(RelicModel).IsAssignableFrom(type) && type.Namespace == "MegaCrit.Sts2.Core.Models.Relics")
                     .OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            if (relic.GetMethod("AfterObtained", Instance | BindingFlags.DeclaredOnly) == null) continue;
            var model = ModelDb.AllRelics.FirstOrDefault(each => each.GetType() == relic);
            var cards = RunReminders.CardsFrom(OptionReader.Game.ReadObtained(relic)).For(target: null, model, Array.Empty<object>());
            lines.Add("relic " + relic.Name + ": " + Describe(cards, me));
        }
        var text = new StringBuilder("# The cards each event option and relic's obtaining can give, for a new Ironclad run (OptionCardsSnapshotTests: SPIREQOL_UPDATE_SNAPSHOT=1 regenerates; decision #353).\n");
        foreach (var line in lines) text.Append(line).Append('\n');
        var file = Path.Combine(CardPlaySnapshotTests.SourceFolder(), "Snapshots", "option-cards.txt");
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(file, text.ToString());
            return;
        }
        Assert.True(File.Exists(file), $"missing {file}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        var now = text.ToString().Split('\n');
        var expected = File.ReadAllText(file).Split('\n');
        var changed = now.Except(expected).Take(12).ToList();
        var gone = expected.Except(now).Take(12).ToList();
        Assert.True(changed.Count == 0 && gone.Count == 0, "option-cards.txt isn't what the card reading reads now: regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1 and explain the change\nnow:\n"
            + string.Join("\n", changed) + "\nbefore:\n" + string.Join("\n", gone));
    }
}
