using System.Collections.Concurrent;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Features;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Data;

/// <summary>
/// A relic's card rule as its own hook's code reads (decision #359): the hook, the ways it acts on a card (each the card's own tests along one
/// way through the code; the rest of the way is the setting: your card, joining your deck, an attack you play), and the change it makes to the
/// card (a modifier) or looks for (a boost).
/// </summary>
/// <param name="Parameter">The hook's card parameter ("card", "cardSource").</param>
/// <param name="Ways">Or of ands: the card's own tests on each way the hook acts. An empty way acts on every card.</param>
/// <param name="Enchantment">A modifier's enchantment's class (Nimble), when its change is an enchantment.</param>
public sealed record CardReading(AffinityKind Kind, string Hook, string Parameter, IReadOnlyList<IReadOnlyList<ConditionLiteral>> Ways, CardChange Change, string? Enchantment)
{
    /// <summary>Whether a card passes the hook's tests: null when one can't be told on it.</summary>
    public bool? Holds(CardModel card) => RelicReadings.Holds(this, card, withChange: true);

    /// <summary>A boost's tests besides the change it looks for (Miniature Cannon's Attack, without its upgrade).</summary>
    public bool? HoldsBesidesChange(CardModel card) => RelicReadings.Holds(this, card, withChange: false);

    public override string ToString() =>
        $"{Kind} {Hook}" + (Change != CardChange.None ? " " + Change + (Enchantment != null ? "<" + Enchantment + ">" : "") : "")
        + " on " + string.Join(" or ", Ways.Select(way => way.Count == 0 ? "every card" : string.Join(" and ", way)));
}

/// <summary>
/// What a relic's own hooks do to a choice outside fights (decision #359), read by the program reader from its code: the choices it reacts to,
/// those where it works against you, the ones it always flashes on, and its card rule.
/// </summary>
/// <param name="Joining">What a card joining your deck sets off (AfterCardChangedPiles): a card reward's cards set it off too.</param>
/// <param name="ChangesShopCards">It changes the shop's cards itself (ModifyMerchantCardCreationResults): the shop shows it on them.</param>
/// <param name="Notes">Each read hook and what it came to, and what the reading couldn't tell (for the snapshot and the review).</param>
public sealed record RelicReading(string Relic, ChoiceEffect On, ChoiceEffect CrossedOn, ChoiceEffect Expect, ChoiceEffect Joining, bool ChangesShopCards,
    CardReading? Card, IReadOnlyList<string> Notes)
{
    /// <summary>It does something to a choice outside fights.</summary>
    public bool Reacts => On != ChoiceEffect.None || Card != null;
}

/// <summary>
/// Reads a relic's rules for the choices outside fights from its own hook code (decision #359), the way the combat side reads relics: each hook
/// it overrides that a choice runs (a card joining your deck, a card being added, gold, potions, HP lost, a rest, a purchase, and the damage and
/// block a card of yours deals in fights) is read by the program reader, and its tests are judged in the setting the choice runs it in (your card,
/// joining your deck, out of combat, no dealer for an event's damage). What it does then is read from its steps and the values it returns.
/// The card's own tests are the card rule, evaluated on each card by the program evaluator. Works on any branch's code (the code reader it's given).
/// </summary>
public static class RelicReadings
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>How a hook's code is read.</summary>
    private enum How
    {
        /// <summary>Its steps: what it does (After... hooks).</summary>
        Steps,
        /// <summary>The value it returns on each way: a change from the amount it's given is a reaction (Modify... hooks).</summary>
        Value,
        /// <summary>The true or false it returns: false takes away what the choice gives (Should... hooks).</summary>
        Should,
        /// <summary>The ways it returns true and the change its steps make to the card (TryModifyCardBeingAddedToDeck).</summary>
        Modifier,
        /// <summary>The ways its value changes a card's damage or block in fights: a boost.</summary>
        Boost,
        /// <summary>Only its flash (AfterModifying... hooks, told when the Modify hook changed the amount).</summary>
        Flash,
    }

    /// <summary>What a parameter of the hook is in the setting the choice runs it in.</summary>
    private enum Who
    {
        /// <summary>You (your player, your creature).</summary>
        You,
        /// <summary>Someone else (the enemy your attack hits).</summary>
        Other,
        /// <summary>No one (an event's damage has no dealer), null.</summary>
        Nobody,
        /// <summary>The card the hook is about.</summary>
        Card,
        /// <summary>Something that's there (a potion, a reward list).</summary>
        Present,
    }

    /// <summary>The value of a Modify hook's that changes nothing.</summary>
    private enum Identity { None, Amount, Zero, One }

    /// <param name="Name">The hook (a virtual of AbstractModel's).</param>
    /// <param name="Effect">The choice it's told about.</param>
    /// <param name="InCombat">Whether the setting is a fight (CombatManager.IsInProgress).</param>
    /// <param name="Amount">A Modify hook's amount parameter: returned as is, nothing changes.</param>
    /// <param name="Parameters">What each of its parameters is in the setting; one not named is unknown.</param>
    private sealed record HookSetting(string Name, ChoiceEffect Effect, How How, bool InCombat, Identity Identity, string? Amount, IReadOnlyDictionary<string, Who> Parameters)
    {
        /// <summary>The card parameter, for a hook about a card.</summary>
        public string? CardParameter => Parameters.FirstOrDefault(parameter => parameter.Value == Who.Card).Key;
    }

    private static Dictionary<string, Who> Setting(params (string Parameter, Who Who)[] parameters) => parameters.ToDictionary(entry => entry.Parameter, entry => entry.Who);

    private static readonly Dictionary<string, Who> CardJoining = Setting(("card", Who.Card), ("clonedBy", Who.Nobody));
    private static readonly Dictionary<string, Who> Gold = Setting(("player", Who.You));
    private static readonly Dictionary<string, Who> HpLost = Setting(("target", Who.You), ("dealer", Who.Nobody), ("cardSource", Who.Nobody));
    private static readonly Dictionary<string, Who> Damage = Setting(("target", Who.Other), ("dealer", Who.You), ("cardSource", Who.Card), ("cardPlay", Who.Present));
    private static readonly Dictionary<string, Who> Block = Setting(("target", Who.You), ("cardSource", Who.Card), ("cardPlay", Who.Present));

    /// <summary>
    /// The hooks a choice outside fights runs (Diagnostics/ChoiceHooks.txt's that say what a choice does to you; the others change what a choice
    /// offers, which the game shows), and the fight hooks a card rule is read from (a boost: damage and block of a card of yours).
    /// </summary>
    private static readonly HookSetting[] Hooks =
    {
        new("AfterCardChangedPiles", ChoiceEffect.CardToDeck | ChoiceEffect.CurseToDeck, How.Steps, InCombat: false, Identity.None, null, CardJoining),
        new("TryModifyCardBeingAddedToDeck", ChoiceEffect.CardToDeck, How.Modifier, InCombat: false, Identity.None, null, Setting(("card", Who.Card))),
        new("TryModifyCardBeingAddedToDeckLate", ChoiceEffect.CardToDeck, How.Modifier, InCombat: false, Identity.None, null, Setting(("card", Who.Card))),
        new("ModifyGoldGained", ChoiceEffect.GoldGain, How.Value, InCombat: false, Identity.Amount, "amount", Gold),
        new("AfterModifyingGoldGained", ChoiceEffect.GoldGain, How.Flash, InCombat: false, Identity.None, null, Gold),
        new("AfterGoldGained", ChoiceEffect.GoldGain, How.Steps, InCombat: false, Identity.None, null, Gold),
        new("ShouldProcurePotion", ChoiceEffect.Potion, How.Should, InCombat: false, Identity.None, null, Setting(("player", Who.You), ("potion", Who.Present))),
        new("AfterPotionProcured", ChoiceEffect.Potion, How.Steps, InCombat: false, Identity.None, null, Setting(("potion", Who.Present))),
        new("ModifyHpLostBeforeOsty", ChoiceEffect.HpLoss, How.Value, InCombat: false, Identity.Amount, "amount", HpLost),
        new("ModifyHpLostBeforeOstyLate", ChoiceEffect.HpLoss, How.Value, InCombat: false, Identity.Amount, "amount", HpLost),
        new("ModifyHpLostAfterOsty", ChoiceEffect.HpLoss, How.Value, InCombat: false, Identity.Amount, "amount", HpLost),
        new("ModifyHpLostAfterOstyLate", ChoiceEffect.HpLoss, How.Value, InCombat: false, Identity.Amount, "amount", HpLost),
        new("AfterModifyingHpLostBeforeOsty", ChoiceEffect.HpLoss, How.Flash, InCombat: false, Identity.None, null, HpLost),
        new("AfterModifyingHpLostAfterOsty", ChoiceEffect.HpLoss, How.Flash, InCombat: false, Identity.None, null, HpLost),
        new("AfterRestSiteHeal", ChoiceEffect.RestHeal, How.Steps, InCombat: false, Identity.None, null, Gold),
        new("ModifyRestSiteHealAmount", ChoiceEffect.RestHeal, How.Value, InCombat: false, Identity.Amount, "amount", Setting(("creature", Who.You))),
        new("TryModifyRestSiteHealRewards", ChoiceEffect.RestHeal, How.Steps, InCombat: false, Identity.None, null, Setting(("player", Who.You), ("rewards", Who.Present))),
        new("AfterItemPurchased", ChoiceEffect.Purchase, How.Steps, InCombat: false, Identity.None, null, Setting(("player", Who.You), ("itemPurchased", Who.Present))),
        new("ModifyDamageAdditive", ChoiceEffect.None, How.Boost, InCombat: true, Identity.Zero, "amount", Damage),
        new("ModifyDamageMultiplicative", ChoiceEffect.None, How.Boost, InCombat: true, Identity.One, "amount", Damage),
        new("ModifyBlockAdditive", ChoiceEffect.None, How.Boost, InCombat: true, Identity.Zero, "block", Block),
        new("ModifyBlockMultiplicative", ChoiceEffect.None, How.Boost, InCombat: true, Identity.One, "block", Block),
    };

    /// <summary>The hooks the reading reads (the coverage check: a relic overriding one has a rule or a reviewed reason).</summary>
    public static IReadOnlyList<string> HookNames { get; } = Hooks.Select(hook => hook.Name).ToList();

    /// <summary>What a reaction does that works against you (the option reader's meaning of its step: a curse joining, HP or max HP lost...).</summary>
    private const ChoiceEffect Bad = ChoiceEffect.CurseToDeck | ChoiceEffect.HpLoss | ChoiceEffect.MaxHpLoss | ChoiceEffect.GoldLoss | ChoiceEffect.PotionLoss
                                     | ChoiceEffect.CardRemoval | ChoiceEffect.RelicLoss;

    /// <summary>What the event check sees happen (RunReminders.Observed): a flash on anything else isn't expected of an icon.</summary>
    private const ChoiceEffect Observable = ~(ChoiceEffect.RestHeal | ChoiceEffect.Purchase);

    // ------------------------------------------------------------------ the loaded game's

    private static readonly ConcurrentDictionary<string, RelicReading?> Loaded = new();

    private static readonly Lazy<Dictionary<string, Type>> LoadedTypes = new(() => RelicTypes(typeof(RelicModel).Assembly).ToDictionary(type => type.Name));

    /// <summary>The game's relic classes on an assembly.</summary>
    public static IEnumerable<Type> RelicTypes(Assembly game)
    {
        Type[] types;
        try { types = game.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }
        return types.Where(type => !type.IsAbstract && Derives(type, "RelicModel") && type.Namespace?.EndsWith(".Mocks", StringComparison.Ordinal) != true);
    }

    /// <summary>Every relic of the loaded game, by class name.</summary>
    public static IReadOnlyCollection<string> LoadedRelics => LoadedTypes.Value.Keys;

    /// <summary>Readings made before the game's cards were known (a card test there may hold on any card), until they are.</summary>
    private static readonly ConcurrentDictionary<string, RelicReading?> Early = new();

    /// <summary>
    /// A relic of the loaded game's reading (null: no such relic), kept once the game's cards are known (ReadAll, once the game has made its models:
    /// the card tests are judged on them). One asked before is read without them (a card test may hold) and read again once they're known.
    /// </summary>
    public static RelicReading? For(string relic)
    {
        if (Loaded.TryGetValue(relic, out var known)) return known;
        var cards = _cards;
        if (cards == null) return Early.GetOrAdd(relic, name => ReadLoaded(name, Array.Empty<CardModel>()));
        return Loaded.GetOrAdd(relic, name => ReadLoaded(name, cards));
    }

    private static RelicReading? ReadLoaded(string relic, IReadOnlyList<CardModel> cards) =>
        LoadedTypes.Value.TryGetValue(relic, out var type) ? Read(type, RuntimeCode.Read, () => cards) : null;

    /// <summary>Whether a relic's reading is kept (the game's cards were known when it was read).</summary>
    public static bool Kept(string relic) => Loaded.ContainsKey(relic);

    /// <summary>The game's cards (canonical), taken once by ReadAll: the game's card lists aren't safe to make on two threads at once.</summary>
    private static volatile IReadOnlyList<CardModel>? _cards;

    private static readonly object CardsTaken = new();

    /// <summary>
    /// Every relic of the loaded game read, once the game has made its models (at load, on the worker that reads the event options: decision #352), so a
    /// screen only looks its reading up. Takes the game's cards first. Returns how many.
    /// </summary>
    public static int ReadAll()
    {
        lock (CardsTaken)
        {
            if (_cards == null)
            {
                try
                {
                    var cards = ModelDb.AllCards.ToList();
                    if (cards.Count > 0) _cards = cards;
                }
                catch { /* the game's models aren't made yet: read without them */ }
            }
        }
        var count = 0;
        foreach (var relic in LoadedRelics)
        {
            try
            {
                For(relic);
                count++;
            }
            catch { /* read again when it's asked for, where a failure is reported */ }
        }
        return count;
    }

    // ------------------------------------------------------------------ reading

    private sealed class Sink
    {
        public ChoiceEffect On, Crossed, Expect, Joining, TakenAway;
        public readonly List<string> Notes = new();
        public readonly List<CardReading> Cards = new();
        public readonly List<(string Field, long Value, ChoiceEffect Effect)> Stores = new();
    }

    /// <summary>
    /// A relic's reading from its own code: <paramref name="read"/> gives a method's instructions, <paramref name="cards"/> the game's cards (canonical),
    /// on which a card test is judged for a card or a curse joining your deck.
    /// </summary>
    public static RelicReading Read(Type relic, Func<MethodBase, List<CodeOp>> read, Func<IReadOnlyList<CardModel>> cards)
    {
        var sink = new Sink();
        var overrides = relic.GetMethods(All).Where(method => method.IsVirtual && !method.IsSpecialName && (method.Attributes & MethodAttributes.NewSlot) == 0).ToList();
        var allCards = new Lazy<IReadOnlyList<CardModel>>(cards);
        foreach (var hook in Hooks)
        {
            if (overrides.FirstOrDefault(method => method.Name == hook.Name) is not { } method) continue;
            try { ReadHook(relic, method, hook, read, allCards, sink); }
            catch (Exception ex) { sink.Notes.Add($"{hook.Name}: ? not read ({ex.GetType().Name})"); }
        }
        try { ReadNeeds(relic, overrides, read, sink); }
        catch (Exception ex) { sink.Notes.Add($"needs: ? not read ({ex.GetType().Name})"); }
        var card = Combined(sink.Cards);
        if (card is { Kind: AffinityKind.Boosts })
            sink.On |= ChoiceEffect.CardToDeck | card.Change switch { CardChange.Upgrade => ChoiceEffect.UpgradeCard, CardChange.Enchant => ChoiceEffect.EnchantCard, _ => ChoiceEffect.None };
        if (card is { Kind: AffinityKind.Modifies }) sink.On |= ChoiceEffect.CardToDeck;
        sink.On |= sink.Crossed;
        var changesShopCards = overrides.Any(method => method.Name == "ModifyMerchantCardCreationResults");
        return new RelicReading(relic.Name, sink.On, sink.Crossed, sink.Expect & sink.On & ~sink.TakenAway & Observable, sink.Joining, changesShopCards, card, sink.Notes);
    }

    /// <summary>A relic's card readings as one (Vitruvian Minion's damage and block): the ways of each, of one kind and change.</summary>
    private static CardReading? Combined(List<CardReading> readings)
    {
        if (readings.Count == 0) return null;
        var first = readings[0];
        var ways = readings.Where(reading => reading.Kind == first.Kind && reading.Change == first.Change).SelectMany(reading => reading.Ways).ToList();
        return first with { Ways = Distinct(ways) };
    }

    private static List<IReadOnlyList<ConditionLiteral>> Distinct(IEnumerable<IReadOnlyList<ConditionLiteral>> ways)
    {
        var kept = new List<IReadOnlyList<ConditionLiteral>>();
        foreach (var way in ways)
            if (!kept.Any(known => known.Count == way.Count && known.Select(literal => literal.ToString()).SequenceEqual(way.Select(literal => literal.ToString())))) kept.Add(way);
        return kept;
    }

    private static void ReadHook(Type relic, MethodInfo method, HookSetting hook, Func<MethodBase, List<CodeOp>> read, Lazy<IReadOnlyList<CardModel>> cards, Sink sink)
    {
        switch (hook.How)
        {
            case How.Steps:
            case How.Flash:
                ReadSteps(relic, ProgramReader.ReadMethod(relic, method, read), hook, cards, sink);
                break;
            case How.Value:
                ReadValue(ProgramReader.ReadReturnedWays(relic, method, read), hook, sink);
                break;
            case How.Should:
                ReadShould(ProgramReader.ReadReturnedWays(relic, method, read), hook, sink);
                break;
            case How.Modifier:
                ReadModifier(ProgramReader.ReadReturnedWays(relic, method, read), ProgramReader.ReadMethod(relic, method, read), hook, sink);
                break;
            case How.Boost:
                ReadBoost(ProgramReader.ReadReturnedWays(relic, method, read), hook, sink);
                break;
        }
    }

    /// <summary>
    /// An After hook's steps: each that acts and can run in the setting is a reaction to the hook's choice (for a card joining your deck, a card's and a
    /// curse's apart, by the card's own tests on the game's cards); a step whose own effect works against you (a curse, HP lost), or one that adds the
    /// card itself again when it's a curse (Bing Bong's copy), is a reaction against you; a flash that always runs there is expected of it.
    /// </summary>
    private static void ReadSteps(Type relic, ModelProgram program, HookSetting hook, Lazy<IReadOnlyList<CardModel>> cards, Sink sink)
    {
        var kinds = hook.Effect == (ChoiceEffect.CardToDeck | ChoiceEffect.CurseToDeck)
            ? new[] { ChoiceEffect.CardToDeck, ChoiceEffect.CurseToDeck }
            : new[] { hook.Effect };
        var found = new List<string>();
        foreach (var (step, condition) in Flattened(program.Steps, ProgramCondition.Always))
        {
            if (step is UnknownStep unknown) found.Add("? " + unknown.Reason);
            if (step is not FlashCall && !Acts(step)) continue;
            var runs = false;
            foreach (var kind in kinds)
            {
                var kindCards = kind switch
                {
                    ChoiceEffect.CardToDeck => cards.Value.Where(card => card.Type != CardType.Curse).ToList(),
                    ChoiceEffect.CurseToDeck => cards.Value.Where(card => card.Type == CardType.Curse).ToList(),
                    _ => null,
                };
                var holds = Judged(condition, hook, kindCards);
                if (holds == false) continue;
                runs = true;
                if (step is FlashCall)
                {
                    if (holds == true) sink.Expect |= kind;
                    continue;
                }
                sink.On |= kind;
                if (hook.Name == "AfterCardChangedPiles") sink.Joining |= kind;
                if (step is CommandStep command && (OptionReader.EffectOf(command.Name) & Bad) != 0) sink.Crossed |= kind;
                if (kind == ChoiceEffect.CurseToDeck && step is CommandStep { Command: { DeclaringType: "CardPileCmd", Name: "Add" } } add
                    && add.Values.Count > 0 && add.Values[0] is { } added && hook.CardParameter is { } parameter && Names(added, parameter))
                    sink.Crossed |= kind;
                if (step is StoreStep { Value: ConstantValue constant } store) sink.Stores.Add((store.Field, constant.Value, kind));
            }
            if (runs) found.Add(step.ToString());
        }
        sink.Notes.Add($"{hook.Name}: {(found.Count == 0 ? "nothing in the setting" : string.Join("; ", found))}");
    }

    /// <summary>A step that changes something (a command, a store to the relic's state but its look, a change to a list or a card), or one not read.</summary>
    private static bool Acts(ProgramStep step) => step switch
    {
        CommandStep or CardActionStep or ParameterListStep or DataStep or UnknownStep or ChoiceStep or CreateStep => true,
        // Status is how the relic looks (RelicModel.Status), not what it does.
        StoreStep store => store.Field != "Status",
        _ => false,
    };

    /// <summary>The steps in order, loops' bodies included, each with the condition it runs on (a loop's own and its step's).</summary>
    private static IEnumerable<(ProgramStep Step, ProgramCondition Condition)> Flattened(IEnumerable<ProgramStep> steps, ProgramCondition outer)
    {
        foreach (var step in steps)
        {
            var condition = step.Condition == null ? outer : outer.And(step.Condition);
            yield return (step, condition);
            if (step is LoopStep loop)
                foreach (var inner in Flattened(loop.Body, condition)) yield return inner;
        }
    }

    /// <summary>A Modify hook's ways: one that can be taken in the setting and returns other than the amount is a reaction; zero takes it away (against you).</summary>
    private static void ReadValue(WaysValue ways, HookSetting hook, Sink sink)
    {
        var found = new List<string>();
        foreach (var (when, value) in ways.Ways)
        {
            var holds = Judged(when, hook, cards: null);
            if (holds == false) continue;
            if (!IsIdentity(value, hook))
            {
                sink.On |= hook.Effect;
                if (hook.Identity == Identity.Amount && IsZero(value))
                {
                    sink.Crossed |= hook.Effect;
                    sink.TakenAway |= hook.Effect;
                }
                found.Add(value.ToString()!);
            }
            if (holds == true) break; // the first way that holds is the value
        }
        sink.Notes.Add($"{hook.Name}: {(found.Count == 0 ? "unchanged in the setting" : string.Join("; ", found))}");
    }

    /// <summary>A Should hook's ways: false in the setting takes the choice's effect away (against you); one it can't tell is a reaction.</summary>
    private static void ReadShould(WaysValue ways, HookSetting hook, Sink sink)
    {
        var result = "unchanged in the setting";
        foreach (var (when, value) in ways.Ways)
        {
            var holds = Judged(when, hook, cards: null);
            if (holds == false) continue;
            var returns = value is ConstantValue constant ? constant.Value != 0 : Truth(value, hook);
            if (returns == false)
            {
                sink.On |= hook.Effect;
                sink.Crossed |= hook.Effect;
                sink.TakenAway |= hook.Effect;
                result = "takes it away";
            }
            else if (returns == null)
            {
                sink.On |= hook.Effect;
                result = "? " + value;
            }
            if (holds == true) break;
        }
        sink.Notes.Add($"{hook.Name}: {result}");
    }

    /// <summary>
    /// TryModifyCardBeingAddedToDeck: the ways it returns true are the cards it changes (its card tests), the command its steps run on the card the
    /// change (CardCmd.Upgrade, CardCmd.Enchant&lt;T&gt;).
    /// </summary>
    private static void ReadModifier(WaysValue ways, ModelProgram program, HookSetting hook, Sink sink)
    {
        var parameter = hook.CardParameter!;
        var cardWays = new List<IReadOnlyList<ConditionLiteral>>();
        foreach (var (when, value) in ways.Ways)
            if (value is ConstantValue { Value: not 0 }) cardWays.AddRange(CardWays(when, hook));
        var commands = program.Commands.ToList();
        var (change, enchantment) = commands.FirstOrDefault(command => command.Command is { DeclaringType: "CardCmd", Name: "Upgrade" }) != null ? (CardChange.Upgrade, (string?)null)
            : commands.FirstOrDefault(command => command.Command is { DeclaringType: "CardCmd", Name: "Enchant" }) is { } enchant ? (CardChange.Enchant, (enchant.Command.GenericArguments.Count > 0 ? enchant.Command.GenericArguments[0] : null))
            : (CardChange.None, null);
        if (cardWays.Count == 0)
        {
            sink.Notes.Add($"{hook.Name}: changes no card in the setting");
            return;
        }
        var reading = new CardReading(AffinityKind.Modifies, hook.Name, parameter, Distinct(cardWays), change, enchantment);
        sink.Cards.Add(reading);
        sink.Notes.Add($"{hook.Name}: {reading}");
    }

    /// <summary>
    /// A damage or block hook in fights: the ways it changes a card of yours' amount (not the identity) on the card's own tests, with no test of the
    /// relic's state (Pen Nib's count is about the fight, not the card), are its boost; the test it makes of the card's upgrade or enchantment is the
    /// change it looks for.
    /// </summary>
    private static void ReadBoost(WaysValue ways, HookSetting hook, Sink sink)
    {
        var parameter = hook.CardParameter!;
        var cardWays = new List<IReadOnlyList<ConditionLiteral>>();
        foreach (var (when, value) in ways.Ways)
        {
            if (IsIdentity(value, hook)) continue;
            foreach (var way in CardWays(when, hook))
                if (way.Any(literal => !IsAttackTest(literal))) cardWays.Add(way);
        }
        if (cardWays.Count == 0) return;
        var changes = cardWays.SelectMany(way => way).Select(literal => ChangeOf(literal, parameter)).Where(change => change != CardChange.None).Distinct().ToList();
        var reading = new CardReading(AffinityKind.Boosts, hook.Name, parameter, Distinct(cardWays), changes.Count == 1 ? changes[0] : CardChange.None, null);
        sink.Cards.Add(reading);
        sink.Notes.Add($"{hook.Name}: {reading}");
    }

    /// <summary>
    /// The ways of a condition that can be taken in the setting (no test there fails) and test nothing of the relic's own state, each as the card's
    /// own tests along it (an attack's test kept as the card being an Attack).
    /// </summary>
    private static IEnumerable<IReadOnlyList<ConditionLiteral>> CardWays(ProgramCondition condition, HookSetting hook)
    {
        foreach (var term in condition.Terms)
        {
            var card = new List<ConditionLiteral>();
            var possible = true;
            foreach (var literal in term)
            {
                switch (KindOf(literal, hook))
                {
                    case LiteralKind.Card:
                        card.Add(literal);
                        break;
                    case LiteralKind.State:
                        possible = false;
                        break;
                    default:
                        if (SettingHolds(literal, hook) == false) possible = false;
                        else if (IsAttackTest(literal) && literal.Holds) card.Add(literal);
                        break;
                }
                if (!possible) break;
            }
            if (possible) yield return card;
        }
    }

    /// <summary>
    /// The needs of the relic's own effects that a choice breaks (the bonus it gives up): an effect of any hook of its that runs only while you hold no
    /// potion (Belt Buckle's Dexterity) is against you on a potion; one that runs only while a field of its own is unset, which a choice hook of its
    /// sets (Maw Bank's gold per room, ended by a purchase), is against you on that choice.
    /// </summary>
    private static void ReadNeeds(Type relic, List<MethodInfo> overrides, Func<MethodBase, List<CodeOp>> read, Sink sink)
    {
        foreach (var method in overrides)
        {
            if (!method.ReturnType.Name.StartsWith("Task", StringComparison.Ordinal)) continue;
            ModelProgram program;
            try { program = ProgramReader.ReadMethod(relic, method, read); }
            catch { continue; }
            foreach (var (step, condition) in Flattened(program.Steps, ProgramCondition.Always))
            {
                if (step is not CommandStep || condition.IsAlways || condition.IsNever) continue;
                if (condition.Terms.All(term => term.Any(literal => !literal.Holds && IsYourPotions(literal.Test))))
                {
                    sink.On |= ChoiceEffect.Potion;
                    sink.Crossed |= ChoiceEffect.Potion;
                    sink.Notes.Add($"{method.Name}: needs no potion ({step})");
                }
                foreach (var (field, value, effect) in sink.Stores.ToList())
                    if (condition.Terms.All(term => term.Any(literal => literal.Test is QueryValue { Of: NamedValue { Name: "this" } } own && own.Name == field && literal.Holds == (value == 0))))
                    {
                        sink.Crossed |= effect;
                        sink.Notes.Add($"{method.Name}: needs {field} unset, which {effect} sets ({step})");
                    }
            }
        }
    }

    /// <summary>Any(this.Owner.Potions): you hold a potion.</summary>
    private static bool IsYourPotions(ProgramValue test) =>
        test is QueryValue { Type: "Enumerable", Name: "Any", Arguments: [QueryValue { Name: "Potions", Of: var holder }] } && IsYouValue(holder);

    // ------------------------------------------------------------------ judging a condition in the setting

    private enum LiteralKind { Setting, Card, State }

    /// <summary>
    /// What a test is about: the relic's own state (a field of its besides its owner: Book of Five Rings' count), the card (a member of the hook's card
    /// besides its owner and pile, or the card given to a query: CanEnchant(card)), or the setting (whose it is, where, in a fight).
    /// </summary>
    private static LiteralKind KindOf(ConditionLiteral literal, HookSetting hook)
    {
        var parts = literal.Test.Parts().ToList();
        if (parts.Any(part => part is QueryValue { Of: NamedValue { Name: "this" } } own && own.Name != "Owner")) return LiteralKind.State;
        if (hook.CardParameter is { } parameter && parts.Any(part => ReadsCard(part, parameter))) return LiteralKind.Card;
        return LiteralKind.Setting;
    }

    private static bool IsParameter(ProgramValue? value, string name) => value is NamedValue named && named.Name == name || value is ParameterValue given && given.Name == name;

    private static bool Names(ProgramValue value, string parameter) => value.Parts().Any(part => IsParameter(part, parameter));

    /// <summary>A member of the card (but its owner and pile), or the card given to a query.</summary>
    private static bool ReadsCard(ProgramValue part, string parameter) => part is QueryValue query
        && (IsParameter(query.Of, parameter) && query.Name is not ("Owner" or "Pile") || query.Arguments.Any(argument => IsParameter(argument, parameter)));

    /// <summary>An attack's damage (IsPoweredAttack(props)): in a fight, an Attack card's (the card rule tests the card is one).</summary>
    private static bool IsAttackTest(ConditionLiteral literal) => literal.Test is QueryValue { Name: "IsPoweredAttack" };

    /// <summary>The change a card test looks for: its upgrade or its enchantment.</summary>
    private static CardChange ChangeOf(ConditionLiteral literal, string parameter)
    {
        var parts = literal.Test.Parts().OfType<QueryValue>().Where(query => IsParameter(query.Of, parameter)).Select(query => query.Name).ToList();
        if (parts.Contains("IsUpgraded")) return CardChange.Upgrade;
        if (parts.Contains("Enchantment")) return CardChange.Enchant;
        return CardChange.None;
    }

    /// <summary>
    /// Whether a condition holds in the setting: a way holds when each of its tests does (the card's on every card of <paramref name="cards"/>; one
    /// it can't tell, or the relic's state, may), and the condition when a way does. Null: it may.
    /// </summary>
    private static bool? Judged(ProgramCondition condition, HookSetting hook, IReadOnlyList<CardModel>? cards)
    {
        var result = (bool?)false;
        foreach (var term in condition.Terms)
        {
            var way = (bool?)true;
            foreach (var literal in term)
            {
                bool? holds = KindOf(literal, hook) switch
                {
                    LiteralKind.State => null,
                    LiteralKind.Card => cards == null ? null : OnCards(literal, hook, cards),
                    _ => SettingHolds(literal, hook),
                };
                if (holds == false) { way = false; break; }
                if (holds == null) way = null;
            }
            if (way == true) return true;
            if (way == null) result = null;
        }
        return result;
    }

    /// <summary>A card test on a set of cards: true when every card passes, false when none does, null when some do (or it can't tell).</summary>
    private static bool? OnCards(ConditionLiteral literal, HookSetting hook, IReadOnlyList<CardModel> cards)
    {
        if (cards.Count == 0) return null; // the game's cards aren't made yet: it may
        bool any = false, all = true;
        foreach (var card in cards)
        {
            var holds = Evaluate(literal, hook.CardParameter!, card);
            if (holds != false) any = true;
            if (holds != true) all = false;
        }
        return all ? true : any ? null : false;
    }

    private static bool? SettingHolds(ConditionLiteral literal, HookSetting hook) => Truth(literal.Test, hook) is { } truth ? truth == literal.Holds : null;

    /// <summary>What a test of the setting comes to there: null when it can't be told (a count, a parameter not in the setting).</summary>
    private static bool? Truth(ProgramValue test, HookSetting hook)
    {
        switch (test)
        {
            case ConditionValue inner:
                return Judged(inner.Condition, hook, cards: null);
            case NamedValue or ParameterValue when WhoOf(test, hook) is { } who:
                return who != Who.Nobody;
            case QueryValue { Name: "IsDead" }:
                return false;
            case QueryValue { Name: "IsInProgress", Type: "CombatManager" }:
                return hook.InCombat;
            case QueryValue { Name: "IsPoweredAttack" or "IsCardOrMonsterMove" }:
                return hook.InCombat;
            case QueryValue { Name: "Pile" } pile when hook.CardParameter is { } parameter && IsParameter(pile.Of, parameter):
                return true;
            case CompareValue { Op: Comparison.Equal or Comparison.NotEqual } compare:
                var equal = Equal(compare.Left, compare.Right, hook);
                return equal == null ? null : equal == (compare.Op == Comparison.Equal);
            default:
                return null;
        }
    }

    /// <summary>Whether two values of the setting are the same: whose (you, someone else, no one), or a card's pile's type against a constant.</summary>
    private static bool? Equal(ProgramValue left, ProgramValue right, HookSetting hook)
    {
        if (left is QueryValue { Name: "Type", Of: QueryValue { Name: "Pile" } pile } && hook.CardParameter is { } parameter && IsParameter(pile.Of, parameter)
            && right is ConstantValue pileType)
            return hook.Name == "AfterCardChangedPiles" ? pileType.Value == (long)PileType.Deck : null;
        var whoLeft = right is NullValue ? Who.Nobody : WhoOf(left, hook);
        var whoRight = right is NullValue ? WhoOf(left, hook) : WhoOf(right, hook);
        if (whoLeft is not { } first || whoRight is not { } second || first == Who.Card || second == Who.Card || first == Who.Present || second == Who.Present) return null;
        return first == second;
    }

    /// <summary>Who a value is in the setting: you (this.Owner, its creature, a parameter that's you, your card's owner), Osty (not you), no one.</summary>
    private static Who? WhoOf(ProgramValue value, HookSetting hook)
    {
        switch (value)
        {
            case NamedValue or ParameterValue:
                var name = value is NamedValue named ? named.Name : ((ParameterValue)value).Name;
                return hook.Parameters.TryGetValue(name, out var who) ? who : null;
            case QueryValue { Name: "Owner" } owner when owner.Of is NamedValue { Name: "this" } || hook.CardParameter is { } card && IsParameter(owner.Of, card):
                return Who.You;
            case QueryValue { Name: "Creature" or "Player" } creature when creature.Of is { } whose && WhoOf(whose, hook) == Who.You:
                return Who.You;
            case QueryValue { Name: "Osty" or "PetOwner" } other when other.Of is { } whose && WhoOf(whose, hook) == Who.You:
                return other.Name == "Osty" ? Who.Other : Who.Nobody;
            default:
                return null;
        }
    }

    private static bool IsYouValue(ProgramValue? value) => value is QueryValue { Name: "Owner", Of: NamedValue { Name: "this" } };

    private static bool IsIdentity(ProgramValue value, HookSetting hook) => hook.Identity switch
    {
        Identity.Amount => IsParameter(value, hook.Amount!),
        Identity.Zero => IsZero(value),
        Identity.One => value is NamedValue { Name: "Decimal.One" } or ConstantValue { Value: 1 },
        _ => false,
    };

    private static bool IsZero(ProgramValue value) => value is NamedValue { Name: "Decimal.Zero" } or ConstantValue { Value: 0 };

    private static bool Derives(Type type, string baseName)
    {
        for (var other = type.BaseType; other != null; other = other.BaseType)
            if (other.Name == baseName) return true;
        return false;
    }

    // ------------------------------------------------------------------ a card rule on a card

    /// <summary>
    /// Whether a card passes a card reading's tests (each way's, evaluated by the program evaluator with the hook's card parameter bound to it, an
    /// attack's test as the card being an Attack, a model the code names by class, ModelDb.Enchantment&lt;Nimble&gt;(), as the game's canonical one).
    /// <paramref name="withChange"/> false leaves out the change a boost looks for. Null when one way can't be told and none holds.
    /// </summary>
    internal static bool? Holds(CardReading reading, CardModel card, bool withChange)
    {
        var result = (bool?)false;
        foreach (var way in reading.Ways)
        {
            var holds = (bool?)true;
            foreach (var literal in way)
            {
                if (!withChange && ChangeOf(literal, reading.Parameter) != CardChange.None) continue;
                var one = Evaluate(literal, reading.Parameter, card);
                if (one == false) { holds = false; break; }
                if (one == null) holds = null;
            }
            if (holds == true) return true;
            if (holds == null) result = null;
        }
        return result;
    }

    private static bool? Evaluate(ConditionLiteral literal, string parameter, CardModel card)
    {
        if (IsAttackTest(literal)) return (card.Type == CardType.Attack) == literal.Holds;
        var parameters = new Dictionary<string, object?> { [parameter] = card, ["props"] = MegaCrit.Sts2.Core.ValueProps.ValueProp.Move };
        var test = literal.Test.Map(part =>
        {
            if (part is not QueryValue { Type: "ModelDb", Of: null, Arguments.Count: 0, GenericArguments: [var model] } query) return null;
            var key = "model:" + query.Name + "<" + model + ">";
            if (!parameters.ContainsKey(key)) parameters[key] = CanonicalModel(query.Name, model);
            return new ParameterValue(key);
        });
        var condition = ProgramCondition.Of(literal with { Test = test });
        return ProgramEvaluator.Holds(condition, new ProgramBoard(card), ModelProgram.Empty, parameters);
    }

    private static readonly ConcurrentDictionary<string, object?> Canonical = new();

    /// <summary>The game's canonical model a ModelDb call names by class (ModelDb.Enchantment&lt;Nimble&gt;()): a lookup, never a roll.</summary>
    private static object? CanonicalModel(string method, string model) => Canonical.GetOrAdd(method + "<" + model + ">", _ =>
    {
        try
        {
            var type = typeof(ModelDb).Assembly.GetTypes().FirstOrDefault(candidate => candidate.Name == model && typeof(AbstractModel).IsAssignableFrom(candidate));
            var lookup = typeof(ModelDb).GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault(candidate => candidate.Name == method && candidate.IsGenericMethodDefinition
                && candidate.GetParameters().Length == 0);
            return type == null || lookup == null ? null : lookup.MakeGenericMethod(type).Invoke(null, null);
        }
        catch { return null; }
    });
}
