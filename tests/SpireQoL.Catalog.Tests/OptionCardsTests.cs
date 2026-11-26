using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Run Reminders reads which cards an event option can give from its program (docs/CARD_ODDS.md, decisions #353, #357): each reward's pools, filter,
/// odds and count from its step's values. Read against every event and relic in the installed game: rewards whose pool isn't in their code are the
/// reviewed ones, the known filters are found, and the code makes no reward the program doesn't hold but the reviewed ones.
/// </summary>
[Collection(GameFightCollection.Name)]
public class OptionCardsTests
{
    private static IEnumerable<Type> Events() => typeof(EventModel).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && !type.IsNested && typeof(EventModel).IsAssignableFrom(type) && type.Namespace?.EndsWith(".Mocks", StringComparison.Ordinal) != true);

    private static Dictionary<string, Type> Relics() => typeof(RelicModel).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && typeof(RelicModel).IsAssignableFrom(type)).ToDictionary(type => type.Name);

    /// <summary>The card readings of every option of an event (a relic: its obtaining's).</summary>
    private static List<RunReminders.OptionCards> Read(Type model)
    {
        GameModels.EnsureLoaded();
        if (typeof(RelicModel).IsAssignableFrom(model)) return new List<RunReminders.OptionCards> { RunReminders.CardsFrom(OptionReader.Game.ReadObtained(model)) };
        return OptionReader.Game.Options(model).Select(option => RunReminders.CardsFrom(OptionReader.Game.Read(option.Handler, option.Relic))).ToList();
    }

    private static (int Fixed, int Rewards, int Filters, bool Transform, int PoolFromOption, int OtherCharacterPools) Summary(Type model) =>
        Read(model).Select(cards => cards.Summary).Aggregate((0, 0, 0, false, 0, 0), (sum, each) =>
            (sum.Item1 + each.Fixed, sum.Item2 + each.Rewards, sum.Item3 + each.Filters, sum.Item4 || each.Transform, sum.Item5 + each.PoolFromOption, sum.Item6 + each.OtherCharacterPools));

    private static List<(string Maker, string Count, string Loop)> RewardsRead(Type model) => Read(model).SelectMany(cards => cards.RewardsRead).ToList();

    [Fact]
    public void OnlyReviewedEventsTakeTheirPoolFromTheOption()
    {
        // A reward with a pool its code is given (not one it names) takes the CardPoolModel the option's delegate holds. That's how Colorful
        // Philosophers passes each character's pool; a new event doing it needs a look (does its delegate hold the pool?).
        var reviewed = new[] { "ColorfulPhilosophers" };
        var found = Events().Where(type => Summary(type).PoolFromOption > 0).Select(type => type.Name).OrderBy(name => name).ToList();
        Assert.Equal(reviewed, found);
    }

    /// <summary>
    /// Options and relics whose code makes a CardCreationOptions the program holds no step for (decision #357), and why: their cards are unknown (the
    /// relics keep their icons) rather than a list that misses some.
    /// </summary>
    private static readonly Dictionary<string, string> UnreadReviewed = new()
    {
        ["Kaleidoscope"] = "each reward's cards are made in a loop over other characters' pools into a list, and the reward is made from that list",
        ["ScrollBoxes"] = "its bundles are drawn from two reward options' possible cards (GetPossibleCards), not offered as rewards",
    };

    [Fact]
    public void TheProgramHoldsEveryRewardTheCodeMakes()
    {
        // The code read as before (every CardCreationOptions its factory or constructor makes) is the check on the program's rewards: a reward made a new
        // way (into a list, through a helper the reader doesn't follow) shows here, not as a card list that misses its cards.
        GameModels.EnsureLoaded();
        var unread = new List<string>();
        foreach (var eventType in Events())
            foreach (var (handler, relic) in OptionReader.Game.Options(eventType))
                if (RunReminders.CardsFrom(OptionReader.Game.Read(handler, relic)).RewardsUnread && !(relic != null && UnreadReviewed.ContainsKey(relic.Name)))
                    unread.Add(eventType.Name + "." + handler.Name + (relic != null ? " [" + relic.Name + "]" : ""));
        foreach (var (name, relic) in Relics())
            if (relic.GetMethod("AfterObtained", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null
                && RunReminders.CardsFrom(OptionReader.Game.ReadObtained(relic)).RewardsUnread != UnreadReviewed.ContainsKey(name))
                unread.Add("relic " + name + (UnreadReviewed.ContainsKey(name) ? ": reviewed, but the program holds its rewards now (take it out)" : ""));
        Assert.True(unread.Count == 0, "the code makes card rewards the program doesn't hold:\n" + string.Join("\n", unread));
    }

    [Fact]
    public void KnownEventsAreReadAsTheyAre()
    {
        var byName = Events().ToDictionary(type => type.Name);
        // Study (Powers) and Touch Core (a cost filter): two rewards from your pool, each with its filter.
        var automaton = Summary(byName["InfestedAutomaton"]);
        Assert.Equal(2, automaton.Rewards);
        Assert.Equal(2, automaton.Filters);
        // Gorge: commons from your pool.
        Assert.True(Summary(byName["RoomFullOfCheese"]).Filters >= 1);
        // Colorful Philosophers: a common, an uncommon and a rare reward from the pool the option passes in (each filter given to the options' constructor).
        var philosophers = Summary(byName["ColorfulPhilosophers"]);
        Assert.Equal(3, philosophers.Rewards);
        Assert.Equal(3, philosophers.Filters);
        // Wuppy in Trouble: an Injury, a fixed card.
        Assert.True(Summary(byName["WuppyInTrouble"]).Fixed >= 1);
        // Some event transforms a card at random.
        Assert.Contains(Events(), type => Summary(type).Transform);
    }

    [Fact]
    public void RewardCountsAndOddsAreRead()
    {
        // What the odds need: how each reward's options are built and how many cards it offers, read from the program's values.
        var byName = Events().ToDictionary(type => type.Name);
        var relics = Relics();
        var uniform = "new:" + (int)MegaCrit.Sts2.Core.Runs.CardRarityOddsType.Uniform;
        Assert.Equal(new[] { (uniform, "Cards", ""), (uniform, "Cards", ""), (uniform, "Cards", "") }, RewardsRead(byName["ColorfulPhilosophers"]));
        Assert.All(RewardsRead(byName["InfestedAutomaton"]), makerCount => Assert.Equal(("ForNonCombatWithDefaultOdds", "1", ""), makerCount));
        Assert.Equal(new[] { ("ForNonCombatWithUniformOdds", "8", "") }, RewardsRead(byName["RoomFullOfCheese"]));
        Assert.All(RewardsRead(relics["SeaGlass"]), makerCount => Assert.Equal(("ForNonCombatWithUniformOdds", "Cards/3", ""), makerCount));
        // Glass Eye: one reward in a loop over its rarities, a pass per value.
        Assert.Equal(new[] { ("ForNonCombatWithUniformOdds", "3", "Common,Common,Uncommon,Uncommon,Rare") }, RewardsRead(relics["GlassEye"]));
    }

    [Fact]
    public void ChangesToCardsAreRead()
    {
        var relics = Relics();
        // Tezcatara's Nutritious Soup enchants your Strikes with Tezcatara's Ember (Mystic Lighter).
        Assert.Equal("TezcatarasEmber", Read(relics["NutritiousSoup"])[0].ChangesRead.Enchantments);
        // Astrolabe upgrades the cards it transforms into: the eggs have nothing to do, Miniature Cannon applies.
        var astrolabe = Read(relics["Astrolabe"])[0].ChangesRead;
        Assert.True(astrolabe.GivesUpgraded);
        Assert.False(astrolabe.UpgradesDeck);
        // Dusty Tome upgrades the card it makes (the one its hover tip shows), not a card of yours.
        var tome = Read(relics["DustyTome"])[0].ChangesRead;
        Assert.True(tome.GivesUpgraded);
        Assert.False(tome.UpgradesDeck);
        // Inspect lists a transform's pool only when you pick the card: Astrolabe yes, Pandora's Box (all Strikes and Defends) no.
        Assert.True(Read(relics["Astrolabe"])[0].PicksTransform);
        Assert.False(Read(relics["PandorasBox"])[0].PicksTransform);
    }

    [Fact]
    public void AnUpgradeOfYourCardsReachesOnlyTheCardsItTakes()
    {
        // Miniature Cannon on an option that upgrades your cards (decision #358): War Paint upgrades your Skills (its code's c.Type == Skill), so it never
        // reaches the Strikes and the Bash of a new Ironclad; Whetstone upgrades your Attacks; a pick from your deck (Aroma of Chaos) takes any upgradable card.
        GameModels.EnsureLoaded();
        var me = OptionCardsSnapshotTests.Ironclad();
        var cannon = SpireQoL.Data.CardAffinity.For("MiniatureCannon")!;
        var relics = Relics();
        var warPaint = Read(relics["WarPaint"])[0];
        Assert.Contains(warPaint.UpgradeTakes, take => take.Contains("Skill", StringComparison.Ordinal));
        Assert.False(warPaint.ChangeReaches(me, cannon));
        Assert.True(Read(relics["Whetstone"])[0].ChangeReaches(me, cannon));
        var aroma = RunReminders.CardsFrom(OptionReader.Game.Read(OptionReader.Game.Options(Events().Single(type => type.Name == "AromaOfChaos"))
            .Single(option => option.Handler.Name == "MaintainControl").Handler));
        Assert.Equal(new[] { "any" }, aroma.UpgradeTakes);
        Assert.True(aroma.ChangeReaches(me, cannon));
    }

    [Fact]
    public void AncientRelicsAreReadAsTheyAre()
    {
        // An ancient's relic option is read through its AfterObtained (and the lambdas it uses).
        var relics = Relics();
        // Pandora's Box: your Strikes and Defends transformed at random (CreateRandomCardForTransform in a lambda).
        Assert.True(Summary(relics["PandorasBox"]).Transform);
        // Sea Glass: commons, uncommons and rares of another character's pool (its own Character, not yours).
        var seaGlass = Summary(relics["SeaGlass"]);
        Assert.Equal(3, seaGlass.Rewards);
        Assert.Equal(3, seaGlass.OtherCharacterPools);
        // Glass Eye: a reward per rarity from your pool, the rarity a loop's value its filter reads.
        var glassEye = Summary(relics["GlassEye"]);
        Assert.True(glassEye.Rewards >= 1);
        Assert.Equal(0, glassEye.OtherCharacterPools);
    }
}
