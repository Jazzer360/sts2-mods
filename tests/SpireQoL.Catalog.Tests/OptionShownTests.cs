using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Data;
using SpireQoL.Features;
using SpireQoL.Procs.Programs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What the option reading reads once an option is shown, and the reading the card relics' checks use (decisions #352 to #356): the reading on a worker,
/// the card reading from the code the reading read, a generic helper read with its type argument, the relic an option shows, a card reward's
/// alternatives your relics add, and a delegate given to a card choice.
/// </summary>
[Collection(GameFightCollection.Name)]
public class OptionShownTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type EventNamed(string name) =>
        typeof(EventModel).Assembly.GetTypes().Single(type => type.Name == name && !type.IsAbstract && typeof(EventModel).IsAssignableFrom(type));

    private static (MethodBase Handler, Type? Relic) OptionOf(string eventName, string handler)
    {
        GameModels.EnsureLoaded();
        return OptionReader.Game.Options(EventNamed(eventName)).First(option => option.Handler.Name.Contains(handler, StringComparison.Ordinal));
    }

    private static OptionReading Read(string eventName, string handler, OptionShown? shown = null)
    {
        var (method, relic) = OptionOf(eventName, handler);
        return OptionReader.Game.Read(method, relic, shown);
    }

    /// <summary>An option of the loaded game's running <paramref name="handler"/> on <paramref name="target"/> (an event made without its constructor).</summary>
    private static EventOption Option(MethodBase handler, object target)
    {
        var option = (EventOption)RuntimeHelpers.GetUninitializedObject(typeof(EventOption));
        typeof(EventOption).GetField("<OnChosen>k__BackingField", Instance)!.SetValue(option, Delegate.CreateDelegate(typeof(Func<Task>), target, (MethodInfo)handler));
        return option;
    }

    [Fact]
    public void AGenericHelpersEnchantmentAndThePicksFilterAreRead()
    {
        // Self-Help Book: each option runs SelectAndEnchant<T>(2, type, ...), whose pick from your deck takes a card of that type (c.Type == typeRestriction)
        // and whose Enchant<T> is the option's enchantment: read with the type argument its call names, and the type its call passes.
        GameModels.EnsureLoaded();
        var strike = ModelDb.Card<StrikeIronclad>();
        var defend = ModelDb.Card<DefendIronclad>();
        var inflame = ModelDb.Card<Inflame>();
        foreach (var (handler, enchantment, takes) in new (string Handler, string Enchantment, CardModel Takes)[] { ("ReadTheBack", "Sharp", strike), ("ReadPassage", "Nimble", defend), ("ReadEntireBook", "Swift", inflame) })
        {
            var reading = Read("SelfHelpBook", handler);
            Assert.True(reading.Picks.Count == 1, handler + ": " + string.Join(" | ", reading.Picks.Select(each => string.Join(",", each.Enchantments) + " " + each.Filter)) + " / " + reading);
            var pick = reading.Picks[0];
            Assert.Equal(new[] { enchantment }, pick.Enchantments);
            Assert.NotNull(pick.Filter);
            foreach (var card in new CardModel[] { strike, defend, inflame })
                Assert.Equal(card == takes, ProgramEvaluator.Passes(pick.Filter!, card, ProgramBoard.Of(new object()), ModelProgram.Empty));
        }
    }

    [Fact]
    public void AGenericHelpersCardIsReadWithItsTypeArgument()
    {
        // Bugslayer's options run AddAndPreview<T>, which makes a T: Exterminate and Squash, the card relics' fixed cards.
        Assert.Contains("Exterminate", Read("Bugslayer", "Extermination").Made);
        Assert.Contains("Squash", Read("Bugslayer", "Squash").Made);
    }

    [Fact]
    public void TheRelicAnOptionShowsIsReadThroughItsObtaining()
    {
        // The Museum's Take obtains the relic the event holds: read once shown, by the one relic its hover tips show (Old Coin's gold here).
        Assert.Contains(Read("MuseumOfTorture", "TakeRelic").Gaps, gap => gap is { Reason: "obtains a relic the event holds", WhenShown: true });
        var shown = Read("MuseumOfTorture", "TakeRelic", new OptionShown(Held: null, new[] { typeof(OldCoin) }, Listeners: null));
        Assert.Contains(shown.Effects, effect => effect.Kind.HasFlag(ChoiceEffect.GoldGain) && effect.Via == nameof(OldCoin));
        Assert.True(shown.Whole, shown.ToString());
        // Two relics shown: which one it gives can't be told from them.
        var two = Read("MuseumOfTorture", "TakeRelic", new OptionShown(Held: null, new[] { typeof(OldCoin), typeof(FragrantMushroom) }, Listeners: null));
        Assert.Contains(two.Gaps, gap => gap is { WhenShown: false } && gap.Reason.StartsWith("obtains a relic the event holds", StringComparison.Ordinal));
    }

    [Fact]
    public void ACardRewardsAlternativesAreTheOnesYourRelicsAdd()
    {
        // A card reward's alternatives: the skip and the reroll from CardRewardAlternative's own code (nothing a reminder looks at), and what your relics add
        // through TryModifyCardRewardAlternatives, once shown: Pael's Wing's sacrifice, which can obtain a relic its code picks.
        const string handler = "<GenerateInitialOptions>b__0";
        Assert.Contains(Read("ColorfulPhilosophers", handler).Gaps, gap => gap.WhenShown && gap.Reason.Contains("TryModifyCardRewardAlternatives", StringComparison.Ordinal));
        var none = Read("ColorfulPhilosophers", handler, new OptionShown(Held: null, Array.Empty<Type>(), Listeners: Array.Empty<Type>()));
        Assert.True(none.Whole, none.ToString());
        var wing = Read("ColorfulPhilosophers", handler, new OptionShown(Held: null, Array.Empty<Type>(), Listeners: new[] { typeof(PaelsWing) }));
        Assert.Contains(wing.Effects, effect => effect.Kind.HasFlag(ChoiceEffect.RelicGain) && effect.Via?.EndsWith(nameof(PaelsWing), StringComparison.Ordinal) == true);
        Assert.Contains(wing.Gaps, gap => gap.Reason == "obtains a relic its code picks as you choose");
    }

    [Fact]
    public void ADelegateGivenToACardChoiceIsReadWhereItRuns()
    {
        // Claws' preview (c => new CardTransformation(c, CreateMaulFromOriginal(c, forPreview: true))): read as the choice runs it; its commands are on the
        // way forPreview is false, so it adds nothing, and nothing is left unread.
        GameModels.EnsureLoaded();
        var claws = OptionReader.Game.ReadObtained(typeof(Claws));
        Assert.True(claws.Whole, claws.ToString());
        Assert.True(claws.Kinds.HasFlag(ChoiceEffect.UpgradeCard) && claws.Kinds.HasFlag(ChoiceEffect.CardToDeck), claws.ToString());
    }

    [Fact]
    public void TheCardReadingReadsTheCodeTheReadingRead()
    {
        // Hungry for Mushrooms' Fragrant Mushroom: the option obtains the relic by its class, and its AfterObtained upgrades your cards: the card reading
        // reads that code (Miniature Cannon's check), which the walk it replaced didn't follow.
        var reading = Read("HungryForMushrooms", "FragrantMushroom");
        Assert.Contains(reading.Code, code => code.Relic == typeof(FragrantMushroom));
        Assert.True(RunReminders.CardsFrom(reading).ChangesRead.UpgradesDeck);
        // A reward's own code (how taking it works) isn't read for the cards: Gorge's eight commons are its own code's.
        var gorge = Read("RoomFullOfCheese", "Gorge");
        Assert.DoesNotContain(gorge.Code, code => code.Body.DeclaringType?.Name.Contains("CardReward", StringComparison.Ordinal) == true);
        Assert.Equal(new[] { ("ForNonCombatWithUniformOdds", "8", "") }, RunReminders.CardsFrom(gorge).RewardsRead);
    }

    [Fact]
    public void AnOptionsReadingIsLookedUpOnceKeptAndReadOnAWorkerOtherwise()
    {
        // At load every option is read without its event (decision #352): an option shown then finds its reading at once. One that needs what it holds
        // as it's shown (the Conveyor's dish) is read on a worker; the frame doesn't wait for it.
        GameModels.EnsureLoaded();
        RunReminders.ReadAllOptions();
        var cheese = EventNamed("RoomFullOfCheese");
        var gorge = Option(OptionOf("RoomFullOfCheese", "Gorge").Handler, RuntimeHelpers.GetUninitializedObject(cheese));
        Assert.True(RunReminders.ReadyReadingOf(gorge)?.Kinds.HasFlag(ChoiceEffect.CardToDeck), "a kept reading is found the frame the option shows");
        Assert.NotNull(RunReminders.ReadyCardsOf(gorge));

        var conveyorType = EventNamed("EndlessConveyor");
        var conveyor = RuntimeHelpers.GetUninitializedObject(conveyorType);
        var dishField = conveyorType.GetField("_currentDish", Instance)!;
        var dish = RuntimeHelpers.GetUninitializedObject(dishField.FieldType);
        dishField.FieldType.GetField("action", Instance)!.SetValue(dish, Delegate.CreateDelegate(typeof(Func<Task>), conveyor, conveyorType.GetMethod("FriedEel", Instance)!));
        dishField.SetValue(conveyor, dish);
        var grab = Option(conveyorType.GetMethod("GrabSomethingOffTheBelt", Instance)!, conveyor);
        RunReminders.ReadOf(grab);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (RunReminders.ReadyReadingOf(grab) == null && waited.ElapsedMilliseconds < 20_000) Thread.Sleep(10);
        Assert.True(RunReminders.ReadyReadingOf(grab)?.Kinds.HasFlag(ChoiceEffect.CardToDeck), "read on a worker, with the dish the event held as it was shown");
    }
}
