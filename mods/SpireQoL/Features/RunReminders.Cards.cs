using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using Sts2Mods.Core.Ui;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SpireQoL.Data;
using SpireQoL.Procs.Programs;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace SpireQoL.Features;

/// <summary>
/// Which cards an event option can give you (docs/CARD_ODDS.md), read from the same code walk as its effects, through the game
/// calls that make cards, so no event needs code of its own:
///  - a card reward or a random card from <c>CardCreationOptions</c>: its pools (a character's, <c>ModelDb.CardPool&lt;T&gt;</c>, or
///    the one the option passes in), its filter (the lambda passed with them; one captured in a loop over a constant array, as
///    Glass Eye's rarity, once per value), its odds, flags and card count. The options are then built by the game's own code
///    and passed through <c>Hook.ModifyCardRewardCreationOptions</c> (Dingy Rug's colorless cards, Prismatic Gem's other
///    characters' cards), so <c>GetPossibleCards</c> and <c>FilterForRun</c> give exactly the cards the reward can offer;
///  - a fixed card: <c>ModelDb.Card&lt;T&gt;</c>, <c>CreateCard&lt;T&gt;</c>, <c>CardCmd.TransformTo&lt;T&gt;</c>;
///  - a transform to a random card: <c>CardFactory.GetDefaultTransformationOptions</c> of each card in your deck;
///  - a pool used without the factory (the Museum's curse pool): every card in it.
/// Also what it does to cards: whether it upgrades the cards it gives (Astrolabe), which enchantments it applies to your cards
/// (Tezcatara's soup), whether it upgrades cards of your deck. When something can't be read, the cards are unknown (null) and
/// the relics keep their icon.
/// </summary>
public sealed partial class RunReminders
{
    private static readonly ConditionalWeakTable<EventOption, StrongBox<List<CardModel>?>> CardsCache = new();

    private static readonly MethodInfo? FilterForRun = AccessTools.Method(typeof(CardFactory), "FilterForRun");

    /// <summary>
    /// The card reading of an option's reading (decisions #353, #357). Its card rewards (pools, filter, odds, flags, count) and what it changes in your
    /// cards (an enchantment, an upgrade of your cards or of the cards it gives, a transform, and the cards an upgrade can take) are read from the
    /// program's steps and their values (<see cref="OptionReading.Steps"/>). The cards its code names by class (a ModelDb.Card&lt;T&gt;, a TransformTo&lt;T&gt;,
    /// the cards a property of its model lists: the Trash Heap's Cards, Archaic Tooth's TranscendenceUpgrades), a pool it uses without a reward
    /// (the Museum's curses) and a constant array a reward's filter takes its value from in a loop (Glass Eye's rarities) are still read from the
    /// code its reading read: every body of the game's models' code, each with the calls it makes into its own model's code. Also the relics whose
    /// obtaining it read and the cards its program makes by class. Code only: no game state, any thread.
    /// </summary>
    internal static OptionCards CardsFrom(OptionReading reading)
    {
        var cards = new OptionCards { Picks = reading.Picks };
        var seen = new HashSet<MethodBase>();
        foreach (var code in reading.Code)
            if (OuterType(code.Body)?.Namespace is { } space && (space.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal) || space.StartsWith("MegaCrit.Sts2.Core.Events", StringComparison.Ordinal)))
                ReadCardsIn(code.Body, OuterType(code.Body)!, cards, seen, depth: 0);
        foreach (var code in reading.Code)
            if (code.Relic != null) cards.Obtains(code.Relic);
        foreach (var card in reading.Made) cards.Makes(card);
        cards.ReadSteps(reading.Steps);
        return cards;
    }

    private static Type? OuterType(MethodBase body)
    {
        var type = body.DeclaringType;
        while (type?.DeclaringType != null) type = type.DeclaringType;
        return type;
    }

    /// <summary>A body's cards, then those of the calls it makes into its own model's code, in the order it makes them.</summary>
    private static void ReadCardsIn(MethodBase method, Type owner, OptionCards cards, HashSet<MethodBase> seen, int depth)
    {
        var body = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? method;
        if (depth > 6 || !seen.Add(body)) return;
        List<HarmonyLib.CodeInstruction> code;
        try { code = SpireQoL.Diagnostics.GameCode.Instructions(body); }
        catch { return; } // unreadable
        cards.Read(code);
        foreach (var ins in code)
            if (ins.operand is MethodBase { DeclaringType: { } declaring } called && (ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) && IsWithin(declaring, owner))
                ReadCardsIn(called, owner, cards, seen, depth + 1);
    }

    /// <summary>Whether a card the rule applies to can come from this option (true when its cards can't be read).</summary>
    private static bool CanGet(EventOption option, Player me, CardAffinityRule rule)
    {
        var cards = CardsOf(option, me);
        var held = Held(me);
        var upgraded = ReadyCardsOf(option) is { GivesUpgraded: true };
        return cards == null || cards.Any(card => CardAffinity.AppliesWith(rule, card, held, upgraded));
    }

    /// <summary>Whether the change this option makes to your cards (an upgrade, an enchantment) can reach one the rule applies to.</summary>
    private static bool ChangeReaches(EventOption option, Player me, CardAffinityRule rule) =>
        ReadyCardsOf(option) is not { } read || read.ChangeReaches(me, rule);

    private static readonly ConditionalWeakTable<EventOption, Dictionary<string, (string? Text, bool Never)>> OddsCache = new();

    /// <summary>
    /// The chance under a relic's icon (docs/CARD_ODDS.md): "43%", or a range for a transform whose cards differ in pool; null when
    /// it's sure or can't be told. <paramref name="never"/>: the option can't give a card the relic cares about (a 0% chance), so the
    /// icon isn't shown. Cached per option and relic.
    /// </summary>
    private static string? OddsText(EventOption option, Player me, CardAffinityRule rule, out bool never)
    {
        var cache = OddsCache.GetValue(option, _ => new Dictionary<string, (string? Text, bool Never)>());
        if (cache.TryGetValue(rule.Relic, out var cached))
        {
            never = cached.Never;
            return cached.Text;
        }
        string? text = null;
        never = false;
        try
        {
            if (ReadyCardsOf(option) is { } read)
            {
                var held = Held(me);
                if (read.Chance(me, card => CardAffinity.AppliesWith(rule, card, held, read.GivesUpgraded)) is { } chance)
                {
                    never = chance.High <= 0;
                    if (!never && chance.Low < 1)
                        text = Percent(chance.Low) == Percent(chance.High) ? Percent(chance.Low) + "%" : $"{Percent(chance.Low)}–{Percent(chance.High)}%";
                }
            }
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders-odds:" + option.TextKey);
        }
        cache[rule.Relic] = (text, never);
        return text;
    }

    /// <summary>A chance as a whole percent: never 0 when it can happen, never 100 when it might not.</summary>
    private static string Percent(double share) => share <= 0 ? "0" : share < 0.01 ? "<1" : Math.Min(99, (int)Math.Round(share * 100)).ToString();

    /// <summary>The chance, small, along an icon's bottom edge (like the amount on a tab).</summary>
    private static Label OddsLabel(string text, float iconSize)
    {
        var label = UiKit.Label(text, Math.Max(12, (int)(iconSize * 0.36f)), Colors.White);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Size = new Vector2(iconSize + 16, 0);
        label.Position = new Vector2(-8, iconSize * 0.62f);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    /// <summary>Your relics' card rules (the modifiers a boost can count on).</summary>
    private static List<CardAffinityRule> Held(Player me) =>
        me.Relics.Select(relic => CardAffinity.For(relic.GetType().Name)).OfType<CardAffinityRule>().ToList();

    /// <summary>The cards (canonical) an option can give you, or null when they can't be read. Cached per option.</summary>
    internal static List<CardModel>? CardsOf(EventOption option, Player me)
    {
        if (CardsCache.TryGetValue(option, out var box)) return box.Value;
        // Not read yet (a worker reads it): unknown, and not kept.
        if (ReadyCardsOf(option) is not { } read) return null;
        List<CardModel>? list;
        try
        {
            list = read.Candidates(me);
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders-cards:" + option.TextKey);
            list = null;
        }
        CardsCache.AddOrUpdate(option, new StrongBox<List<CardModel>?>(list));
        return list;
    }

    /// <summary>
    /// What an option's code says about the cards it makes (filled from the code its reading read: decision #353), and the objects that code runs on
    /// for one option (For).
    /// </summary>
    internal sealed class OptionCards
    {
        /// <summary>The option's delegate target (the event, or a closure over it): the filter lambdas' <c>this</c>.</summary>
        public object? Target;
        /// <summary>An ancient's relic the option gives (its AfterObtained is read too): Sea Glass's character, say.</summary>
        public object? Relic;
        /// <summary>The models of the delegates the option's event held as it was shown (a Neow modifier's): their values and filters.</summary>
        private object[] _heldTargets = Array.Empty<object>();
        /// <summary>The relics whose obtaining the reading read (Fragrant Mushroom, Claws): their values, on the relic's own model.</summary>
        private readonly List<Type> _relics = new();

        /// <summary>The picks from your deck the reading names (decision #353): what an enchantment pick can reach (Self-Help Book's).</summary>
        public IReadOnlyList<SpireQoL.Features.OptionPick> Picks { get; init; } = Array.Empty<SpireQoL.Features.OptionPick>();

        /// <summary>A card the reading's program makes by class (CreateCard&lt;T&gt;, its T read where a generic helper is called: Bugslayer's).</summary>
        public void Makes(string card)
        {
            if (typeof(CardModel).Assembly.GetTypes().FirstOrDefault(type => type.Name == card && typeof(CardModel).IsAssignableFrom(type) && !type.IsAbstract) is { } found)
                _fixed.Add(found);
        }

        /// <summary>The reading read a relic's obtaining: its code runs on that relic.</summary>
        public void Obtains(Type relic)
        {
            if (!_relics.Contains(relic)) _relics.Add(relic);
        }

        /// <summary>This card reading for one option: the objects its code runs on (the option's target, its relic, the delegates' models).</summary>
        public OptionCards For(object? target, object? relic, object[] heldTargets)
        {
            var copy = (OptionCards)MemberwiseClone();
            copy.Target = target;
            copy.Relic = relic;
            copy._heldTargets = heldTargets;
            return copy;
        }

        /// <summary>A card pool the code names without a reward, and the call that gave the character whose pool it is (null: not a character's).</summary>
        private sealed record PoolSource(MethodBase Method, MethodBase? Character);

        /// <summary>
        /// A card reward or random card, as the program's values build it (decision #357): the CardCreationOptions its step is given (a factory's or the
        /// constructor's, its pools, filter, odds and flags), how many cards it offers, and whose code it is (the model its values are read on).
        /// </summary>
        private sealed class RewardSource
        {
            /// <summary>The pools' value (new ReadOnlySingleElementList(Owner.Character.CardPool), [a, b], Array.Empty).</summary>
            public required ProgramValue Pools;
            /// <summary>The CardCreationOptions factory's name, or ".ctor" for its constructor.</summary>
            public required string Maker;
            /// <summary>Its filter (c => c.Rarity == Common); null when it has none. One the reading couldn't read keeps every card.</summary>
            public LambdaValue? Filter;
            /// <summary>The constructor's source and odds (CardCreationSource, CardRarityOddsType), read as numbers.</summary>
            public int? Source, Odds;
            /// <summary>Flags added with WithFlags; IsCardReward when a CardReward takes it.</summary>
            public int Flags;
            public ProgramValue? Count;
            public required Type Owner;
            /// <summary>A filter's value taken per pass of a loop over a constant array (Glass Eye's rarities), by the name the filter reads it by.</summary>
            public string? LoopName;
            public object[]? LoopValues;
        }

        private readonly HashSet<Type> _fixed = new();
        private readonly HashSet<Type> _enchantments = new();
        private readonly List<RewardSource> _rewards = new();
        private readonly List<PoolSource> _loosePools = new();
        /// <summary>Constant arrays of an enum the code builds (Glass Eye's rarities), by the enum.</summary>
        private readonly Dictionary<Type, object[]> _arrays = new();
        /// <summary>
        /// The CardCreationOptions the code makes (its factory or constructor calls), the check on the program's rewards: more of them than the program
        /// has rewards (Kaleidoscope's, made in a loop into a list no step is given) leaves the option's cards unknown, never a list that misses some.
        /// </summary>
        private int _optionsMade;
        /// <summary>The program's card rewards and the options it is given besides (a CardReward's reroll options): what the code's options should be.</summary>
        private int _optionsRead;

        /// <summary>The cards the program makes from a value (RunState.CreateCard(card, owner)), each with whose code it is: read on the objects the code runs on.</summary>
        private readonly List<(ProgramValue Card, Type Owner)> _madeFrom = new();

        /// <summary>
        /// The cards the program makes from a value that the objects its code runs on tell (Dusty Tome's card, by its id: the card its hover tip shows);
        /// one they can't (a roll's: the Trash Heap's) is the code's own list of cards, read with the cards it names.
        /// </summary>
        private List<CardModel> MadeFromValues()
        {
            var cards = new List<CardModel>();
            foreach (var (card, owner) in _madeFrom)
            {
                if (HolderOf(owner) is not { } holder) continue;
                try
                {
                    if (ProgramEvaluator.Value(card, ProgramBoard.Of(holder), ModelProgram.Empty) is CardModel made && !cards.Contains(made.IsCanonical ? made : made.CanonicalInstance))
                        cards.Add(made.IsCanonical ? made : made.CanonicalInstance);
                }
                catch { /* not told */ }
            }
            return cards;
        }

        /// <summary>The code makes a reward the program doesn't hold: the option's cards can't be told.</summary>
        internal bool RewardsUnread => _optionsMade > _optionsRead;
        private bool _transformRandom;
        /// <summary>You pick the card to transform (CardSelectCmd.FromDeckForTransformation), rather than all of a kind or one at random.</summary>
        private bool _picksTransform;
        private bool _upgradesDeck;
        private bool _enchantsDeck;
        /// <summary>
        /// What an upgrade of your cards can take (decision #358): a pick from your deck (FromDeckForUpgrade: any upgradable card, null), or the test the
        /// code puts on your deck's cards before it upgrades them (War Paint's c.Type == Skill). Empty with an upgrade of your cards: any upgradable card.
        /// </summary>
        private readonly List<LambdaValue?> _upgradeTakes = new();

        /// <summary>The option upgrades the cards it gives (Astrolabe: CardCmd.Upgrade on its new cards, not a pick from your deck).</summary>
        public bool GivesUpgraded { get; private set; }

        /// <summary>
        /// One method body's code, for what the program doesn't hold: the cards it names by class (ModelDb.Card&lt;T&gt;, CreateCard&lt;T&gt;,
        /// TransformTo&lt;T&gt;), a pool it uses without a reward (one a CardCreationOptions call takes is the reward's, read from the program), and
        /// the constant arrays of an enum it builds (a loop over them gives a reward's filter its value: Glass Eye's rarities).
        /// </summary>
        public void Read(List<CodeInstruction> code)
        {
            var pools = new List<PoolSource>();
            MethodBase? character = null;
            int? last = null;
            (Type Element, int Length, List<object> Values)? building = null;
            FieldInfo? arrayData = null;
            foreach (var ins in code)
            {
                if (ins.opcode == OpCodes.Newarr && ins.operand is Type { IsEnum: true } element && last is { } length)
                {
                    building = (element, length, new List<object>());
                    continue;
                }
                if (ins.opcode == OpCodes.Ldtoken && ins.operand is FieldInfo data) { arrayData = data; continue; }
                if (building is { } elementLength && ins.opcode.Name?.StartsWith("stelem", StringComparison.Ordinal) == true && last is { } value)
                {
                    elementLength.Values.Add(Enum.ToObject(elementLength.Element, value));
                    if (elementLength.Values.Count == elementLength.Length) { _arrays[elementLength.Element] = elementLength.Values.ToArray(); building = null; }
                    continue;
                }
                if (ins.LoadsConstant() && ins.operand is not string && TryConst(ins, out var constant)) { last = constant; continue; }
                if (ins.operand is not MethodBase otherMethod || otherMethod.DeclaringType == null) continue;
                if (ins.opcode != OpCodes.Call && ins.opcode != OpCodes.Callvirt && ins.opcode != OpCodes.Newobj) continue;
                var type = otherMethod.DeclaringType;
                if (otherMethod.Name == "InitializeArray" && type == typeof(RuntimeHelpers) && building is { } init && arrayData != null)
                {
                    try
                    {
                        var array = Array.CreateInstance(init.Element, init.Length);
                        RuntimeHelpers.InitializeArray(array, arrayData.FieldHandle);
                        _arrays[init.Element] = array.Cast<object>().ToArray();
                    }
                    catch { /* not a constant array we can read */ }
                    building = null;
                    continue;
                }
                if (otherMethod.Name == "get_CardPool" && typeof(CharacterModel).IsAssignableFrom(type)) pools.Add(new PoolSource(otherMethod, character));
                else if (type == typeof(ModelDb) && otherMethod is MethodInfo { IsGenericMethod: true } nextMethod && nextMethod.Name == nameof(ModelDb.CardPool)) pools.Add(new PoolSource(otherMethod, Character: null));
                else if (otherMethod is MethodInfo { IsGenericMethod: true } gc && (gc.Name is "Card" or "CreateCard" or "TransformTo")
                    && gc.GetGenericArguments()[0] is { } otherType && typeof(CardModel).IsAssignableFrom(otherType) && !otherType.IsAbstract && !otherType.IsGenericParameter)
                    _fixed.Add(otherType);
                // A reward's pools: the program's (ReadSteps), which is checked to hold every reward the code makes.
                else if (type == typeof(CardCreationOptions) && (otherMethod.IsConstructor || (otherMethod is MethodInfo f && f.IsStatic && f.ReturnType == typeof(CardCreationOptions))))
                {
                    pools.Clear();
                    _optionsMade++;
                }
                // Whose character a pool belongs to: the last call that returned one (Owner.Character, or Sea Glass's Character).
                if (otherMethod is MethodInfo { ReturnType: var returnType } && typeof(CharacterModel).IsAssignableFrom(returnType)) character = otherMethod;
            }
            _loosePools.AddRange(pools);
        }

        /// <summary>
        /// The program's steps (decision #357): each card reward (a CardReward made with its CardCreationOptions and count, a CardFactory.CreateForReward
        /// in a step's values) with its pools, filter, odds, flags and count as values; the enchantments and upgrades it makes and what they take; a
        /// transform to a random card, and whether you pick the card it turns.
        /// </summary>
        public void ReadSteps(IReadOnlyList<OptionStep> steps)
        {
            var seen = new HashSet<string>();
            var options = new HashSet<string>();
            var upgrades = new List<OptionStep>();
            foreach (var step in steps)
            {
                var values = ValuesOf(step.Step);
                // A CardReward made with a CardCreationOptions (not Kaleidoscope's, made from cards its code picked): its options and its count.
                if (step.Step is CommandStep { Command: { DeclaringType: "CardReward", Name: ".ctor" } } made && made.Values.Count >= 2 && IsOptions(made.Values[0]))
                    Reward(made.Values[0], made.Values[1], step, isCardReward: true, seen, steps);
                // Not inside a command's result (its arguments are that command's step's, read there).
                foreach (var part in values.Select(value => value.Map(inner => inner is ResultValue ? NullValue.Instance : null)).SelectMany(value => value.Parts()))
                    if (part is QueryValue { Type: "CardFactory", Name: "CreateForReward", Arguments.Count: 3 } created && IsOptions(created.Arguments[2]))
                        Reward(created.Arguments[2], created.Arguments[1], step, isCardReward: false, seen, steps);
                foreach (var part in values.Concat(step.Condition?.Literals.Select(literal => literal.Test) ?? Enumerable.Empty<ProgramValue>())
                             .Select(value => value.Map(inner => inner is ResultValue ? NullValue.Instance : null)).SelectMany(value => value.Parts()))
                {
                    // A card made from a value of the model's (Dusty Tome's card, ModelDb.GetById(this.AncientCard): the one its hover tip shows).
                    if (part is QueryValue { Name: "CreateCard", GenericArguments.Count: 0, Arguments: [var model, _] } && !_madeFrom.Contains((model, step.Owner))) _madeFrom.Add((model, step.Owner));
                    // Every CardCreationOptions the program makes (a reward's, a CardReward's reroll options), for the check against the code's.
                    if (part is QueryValue { Type: "CardCreationOptions", Of: null } maker && (maker.Name == ".ctor" || maker.ValueType == "CardCreationOptions"))
                        options.Add(step.Owner.FullName + "|" + step.Relic?.Name + "|" + step.Via + "|" + part);
                    if (part is QueryValue { Type: "ModelDb", Name: "Enchantment", GenericArguments: [var enchantment] } && EnchantmentType(enchantment) is { } type) _enchantments.Add(type);
                    if (part is QueryValue { Type: "CardFactory", Name: "CreateRandomCardForTransform" }) _transformRandom = true;
                }
                switch (step.Step)
                {
                    case CommandStep command when command.Command is { DeclaringType: "CardCmd" } card && card.Name.StartsWith("Enchant", StringComparison.Ordinal):
                        _enchantsDeck = true;
                        foreach (var enchantment in card.GenericArguments)
                            if (EnchantmentType(enchantment) is { } type) _enchantments.Add(type);
                        break;
                    case CommandStep { Command: { DeclaringType: "CardCmd", Name: "TransformToRandom" } }:
                    case CommandStep { Command: { DeclaringType: "CardCmd", Name: "Transform" } transform } when transform.ParameterTypes.Contains("Rng"):
                        _transformRandom = true;
                        break;
                    case CommandStep command when command.Command is { DeclaringType: "CardCmd" } card && card.Name.StartsWith("Upgrade", StringComparison.Ordinal):
                        upgrades.Add(step);
                        break;
                    case ChoiceStep { Command: { DeclaringType: "CardSelectCmd", Name: "FromDeckForEnchantment" } }:
                        _enchantsDeck = true;
                        break;
                    case ChoiceStep { Command: { DeclaringType: "CardSelectCmd", Name: "FromDeckForTransformation" } }:
                        _picksTransform = true;
                        break;
                    case ChoiceStep { Command: { DeclaringType: "CardSelectCmd", Name: "FromDeckForUpgrade" } }:
                        _upgradesDeck = true;
                        Taken(null);
                        break;
                }
            }
            _optionsRead = options.Count;
            // An upgrade is of the cards the option gives (Astrolabe's transformed cards, Archaic Tooth's made card) or of your deck's (a pick, or your
            // deck's cards through a test: War Paint's Skills), by where its card comes from; one whose card can't be told is of the cards it gives
            // when it gives any, else of your deck's.
            var gives = _fixed.Count > 0 || _rewards.Count > 0 || _transformRandom;
            foreach (var upgrade in upgrades)
            {
                var values = ((CommandStep)upgrade.Step).Values;
                var takes = new List<LambdaValue>();
                switch (values.Count == 0 ? UpgradeOf.Unknown : SourceOf(values[0], upgrade.Step, steps, takes))
                {
                    case UpgradeOf.Given:
                        GivesUpgraded = true;
                        break;
                    case UpgradeOf.Deck:
                        _upgradesDeck = true;
                        foreach (var take in takes.Count == 0 ? new LambdaValue?[] { null } : takes.Cast<LambdaValue?>()) Taken(take);
                        break;
                    default:
                        if (gives) GivesUpgraded = true;
                        else
                        {
                            _upgradesDeck = true;
                            Taken(null);
                        }
                        break;
                }
            }
        }

        private enum UpgradeOf { Unknown, Given, Deck }

        /// <summary>An upgrade of your cards takes the cards <paramref name="take"/> holds for (any upgradable card for null), once each.</summary>
        private void Taken(LambdaValue? take)
        {
            if (!_upgradeTakes.Any(known => known?.ToString() == take?.ToString())) _upgradeTakes.Add(take);
        }

        /// <summary>
        /// Where an upgrade's card comes from: a card the option makes (a transform's new card, a reward's, one it creates), your deck (a pick from it,
        /// its cards, with the tests the code puts on them: <paramref name="takes"/>), or neither it can tell. A foreach's item is the loop's items'.
        /// </summary>
        private static UpgradeOf SourceOf(ProgramValue card, ProgramStep at, IReadOnlyList<OptionStep> steps, List<LambdaValue> takes)
        {
            var parts = card.Parts().ToList();
            if (parts.Any(part => part is NamedValue { Name: "each" }) && LoopOf(at, steps) is { Items: { } items }) parts.AddRange(items.Parts());
            if (parts.Any(part => part is QueryValue { Type: "CardFactory" } or QueryValue { Name: "CreateCard" } || part is ResultValue { Command: var command }
                    && (command.StartsWith("CardCmd.Transform", StringComparison.Ordinal) || command.StartsWith("CardFactory.", StringComparison.Ordinal))))
                return UpgradeOf.Given;
            var deck = ((int)PileType.Deck).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (parts.Any(part => part is ResultValue { Command: "CardSelectCmd.FromDeckForUpgrade" } || part is QueryValue { Name: "Deck" }
                    || part is QueryValue { Name: "GetPile", Arguments: [ConstantValue pile, ..] } && pile.ToString() == deck))
            {
                takes.AddRange(parts.OfType<QueryValue>().Where(query => query is { Type: "Enumerable", Name: "Where" or "FirstOrDefault" or "First", Arguments.Count: 2 })
                    .Select(query => query.Arguments[1]).OfType<LambdaValue>());
                return UpgradeOf.Deck;
            }
            return UpgradeOf.Unknown;
        }

        /// <summary>The loop whose body holds <paramref name="step"/> (the nearest), among the steps read.</summary>
        private static LoopStep? LoopOf(ProgramStep step, IReadOnlyList<OptionStep> steps)
        {
            static bool Holds(IEnumerable<ProgramStep> body, ProgramStep step) =>
                body.Any(inner => ReferenceEquals(inner, step) || inner is LoopStep nested && Holds(nested.Body, step) || inner is CommandStep command && Holds(command.Inner, step));
            return steps.Select(read => read.Step).OfType<LoopStep>().LastOrDefault(loop => Holds(loop.Body, step));
        }

        /// <summary>A step's values (a command's or a choice's arguments, a made card's, a loop's items).</summary>
        private static IEnumerable<ProgramValue> ValuesOf(ProgramStep step) => step switch
        {
            CommandStep command => command.Values,
            ChoiceStep choice => choice.Values,
            CreateStep create => create.Values,
            LoopStep { Items: { } items } => new[] { items },
            _ => Array.Empty<ProgramValue>(),
        };

        /// <summary>A CardCreationOptions value: its factory or constructor, with WithFlags or WithRngOverride on it.</summary>
        private static bool IsOptions(ProgramValue value) => Unwrapped(value, out _) is QueryValue { Type: "CardCreationOptions" };

        /// <summary>The options' factory or constructor call under the WithFlags and WithRngOverride on it (the rng only changes the roll), and the flags they add.</summary>
        private static ProgramValue Unwrapped(ProgramValue value, out int flags)
        {
            flags = 0;
            while (true)
                switch (value)
                {
                    case StoredValue stored:
                        value = stored.Value;
                        break;
                    case QueryValue { Type: "CardCreationOptions", Name: "WithFlags", Of: { } inner, Arguments: [var added] }:
                        if (added is ConstantValue constant) flags |= (int)constant.Value;
                        value = inner;
                        break;
                    case QueryValue { Type: "CardCreationOptions", Name: "WithRngOverride", Of: { } inner }:
                        value = inner;
                        break;
                    default:
                        return value;
                }
        }

        private void Reward(ProgramValue options, ProgramValue count, OptionStep step, bool isCardReward, HashSet<string> seen, IReadOnlyList<OptionStep> steps)
        {
            if (Unwrapped(options, out var flags) is not QueryValue { Type: "CardCreationOptions" } maker || maker.Arguments.Count == 0) return;
            if (!seen.Add(step.Owner.FullName + "|" + step.Relic?.Name + "|" + step.Via + "|" + options + "|" + count)) return;
            var constructor = maker.Name == ".ctor";
            var filter = maker.Arguments.Count > (constructor ? 3 : 1) ? maker.Arguments[constructor ? 3 : 1] : null;
            var reward = new RewardSource
            {
                Pools = maker.Arguments[0],
                Maker = maker.Name,
                Filter = filter as LambdaValue,
                Source = constructor && maker.Arguments.Count > 1 && maker.Arguments[1] is ConstantValue source ? (int)source.Value : null,
                Odds = constructor && maker.Arguments.Count > 2 && maker.Arguments[2] is ConstantValue odds ? (int)odds.Value : null,
                Flags = flags | (isCardReward ? (int)CardCreationFlags.IsCardReward : 0),
                Count = count,
                Owner = step.Owner,
            };
            // A filter that reads a value a loop over a constant array of an enum sets (Glass Eye's c => c.Rarity == rarity): a pass per value.
            if (reward.Filter is { } lambda && LoopOf(step.Step, steps) != null && FreeName(lambda) is { } free && EnumTypeOf(lambda, free) is { } enumType
                && _arrays.TryGetValue(enumType, out var values))
            {
                reward.LoopName = free;
                reward.LoopValues = values;
            }
            _rewards.Add(reward);
        }

        /// <summary>The one name a filter reads that isn't its parameter or its model (a local a loop sets); null for none or more.</summary>
        private static string? FreeName(LambdaValue lambda)
        {
            var names = lambda.Body.Literals.SelectMany(literal => literal.Test.Parts()).Select(part => part switch
            {
                NamedValue { Name: var name } when name != "this" => name,
                ParameterValue { Name: var name } => name,
                _ => null,
            }).OfType<string>().Where(name => name != lambda.Parameter).Distinct().ToList();
            return names.Count == 1 ? names[0] : null;
        }

        /// <summary>The enum a filter compares the free name with (c.Rarity == rarity: CardRarity), by the other side's type.</summary>
        private static Type? EnumTypeOf(LambdaValue lambda, string free)
        {
            foreach (var compare in lambda.Body.Literals.Select(literal => literal.Test).OfType<CompareValue>())
            {
                var other = IsName(compare.Left, free) ? compare.Right : IsName(compare.Right, free) ? compare.Left : null;
                if (other is QueryValue { ValueType: var name } && typeof(CardModel).Assembly.GetTypes().FirstOrDefault(type => type.IsEnum && type.Name == name) is { } found) return found;
            }
            return null;
        }

        private static bool IsName(ProgramValue value, string name) => value is NamedValue named && named.Name == name || value is ParameterValue parameter && parameter.Name == name;

        private static Type? EnchantmentType(string name) =>
            typeof(EnchantmentModel).Assembly.GetTypes().FirstOrDefault(type => type.Name == name && typeof(EnchantmentModel).IsAssignableFrom(type) && !type.IsAbstract);

        /// <summary>For the tests: what was read (fixed cards, rewards, rewards with a filter, transform, rewards whose pool the option passes in, pools of a character other than yours).</summary>
        internal (int Fixed, int Rewards, int Filters, bool Transform, int PoolFromOption, int OtherCharacterPools) Summary =>
            (_fixed.Count, _rewards.Count, _rewards.Count(rewardSource => rewardSource.Filter != null), _transformRandom,
             _rewards.Count(rewardSource => PoolValues(rewardSource.Pools).Any(pool => pool is NamedValue { Name: not "this" } or ParameterValue)),
             _rewards.SelectMany(rewardSource => PoolValues(rewardSource.Pools)).Count(pool => pool is QueryValue { Name: "CardPool", Of: QueryValue { Name: "Character", Of: NamedValue { Name: "this" } } }));

        /// <summary>For the tests: each reward's odds (null: the factory's own), count ("Cards", "Cards/3", "3", or "?") and loop values.</summary>
        internal List<(string Maker, string Count, string Loop)> RewardsRead =>
            _rewards.Select(rewardSource => (rewardSource.Maker == ".ctor" ? "new:" + rewardSource.Odds : rewardSource.Maker, CountText(rewardSource.Count),
                rewardSource.LoopValues is { } loopValues ? string.Join(",", loopValues) : "")).ToList();

        private static string CountText(ProgramValue? count) => count switch
        {
            StoredValue stored => CountText(stored.Value),
            ConstantValue constant => constant.ToString(),
            CardValue card => card.Key,
            ArithmeticValue { Op: "div", Left: var left, Right: ConstantValue divisor } => CountText(left) + "/" + divisor,
            _ => "?",
        };

        /// <summary>For the tests: whether you pick the card a transform turns.</summary>
        internal bool PicksTransform => _picksTransform;

        /// <summary>For the tests: the enchantments the code applies, and how it upgrades.</summary>
        internal (string Enchantments, bool GivesUpgraded, bool UpgradesDeck) ChangesRead =>
            (string.Join(",", _enchantments.Select(type => type.Name).OrderBy(name => name)), GivesUpgraded, _upgradesDeck);

        /// <summary>For the tests: what an upgrade of your cards can take (each test the code puts on them; "any" for a pick or none).</summary>
        internal IReadOnlyList<string> UpgradeTakes => _upgradeTakes.Select(take => take?.ToString() ?? "any").ToList();

        private static bool TryConst(CodeInstruction ins, out int value)
        {
            value = 0;
            if (ins.opcode == OpCodes.Ldc_I4_M1) { value = -1; return true; }
            if (ins.opcode == OpCodes.Ldc_I4_0) return true;
            if (ins.opcode == OpCodes.Ldc_I4_1) { value = 1; return true; }
            if (ins.opcode == OpCodes.Ldc_I4_2) { value = 2; return true; }
            if (ins.opcode == OpCodes.Ldc_I4_3) { value = 3; return true; }
            if (ins.opcode == OpCodes.Ldc_I4_4) { value = 4; return true; }
            if (ins.opcode == OpCodes.Ldc_I4_5) { value = 5; return true; }
            if (ins.opcode == OpCodes.Ldc_I4_6) { value = 6; return true; }
            if (ins.opcode == OpCodes.Ldc_I4_7) { value = 7; return true; }
            if (ins.opcode == OpCodes.Ldc_I4_8) { value = 8; return true; }
            if ((ins.opcode == OpCodes.Ldc_I4 || ins.opcode == OpCodes.Ldc_I4_S) && ins.operand != null) { value = Convert.ToInt32(ins.operand); return true; }
            return false;
        }

        /// <summary>The cards (canonical) this code can give, or null when a reward's pool couldn't be read.</summary>
        public List<CardModel>? Candidates(Player me)
        {
            if (RewardsUnread) return null;
            var set = new HashSet<CardModel>();
            foreach (var type in _fixed)
            {
                var card = ModelDb.AllCards.FirstOrDefault(canonicalCard => canonicalCard.GetType() == type);
                if (card != null) set.Add(card);
            }
            foreach (var card in MadeFromValues()) set.Add(card);
            foreach (var reward in _rewards)
            {
                if (Built(reward, me) is not { } built) return null;
                foreach (var (_, possible) in built)
                foreach (var card in possible) set.Add(card);
            }
            foreach (var poolSource in _loosePools)
            foreach (var card in Unlocked(Pool(poolSource, me), me)) set.Add(card);
            if (_transformRandom)
            {
                foreach (var original in me.Deck.Cards)
                {
                    IEnumerable<CardModel> options;
                    try { options = CardFactory.GetDefaultTransformationOptions(original, false); }
                    catch { continue; }
                    foreach (var card in options) set.Add(card.IsCanonical ? card : card.CanonicalInstance);
                }
            }
            return set.Count == 0 ? null : set.ToList();
        }

        /// <summary>
        /// A reward as the game would build it: its CardCreationOptions made by the same factory or constructor, with its flags,
        /// through Hook.ModifyCardRewardCreationOptions (your relics and the run's modifiers: a pure query that only edits the
        /// options it's given), and its possible cards (GetPossibleCards, FilterForRun). One per loop value (Glass Eye). Null
        /// when its pool or its construction can't be told.
        /// </summary>
        private List<(CardCreationOptions Options, List<CardModel> Possible)>? Built(RewardSource reward, Player me)
        {
            // A pool that can't be told (a character's that can't be, one passed in that the option doesn't hold): the cards are unknown, so the icons stay.
            var pools = new List<CardPoolModel>();
            foreach (var poolValue in PoolValues(reward.Pools))
            {
                if (PoolOf(poolValue, reward.Owner, me) is not { } pool) return null;
                if (!pools.Contains(pool)) pools.Add(pool);
            }
            var built = new List<(CardCreationOptions, List<CardModel>)>();
            foreach (var value in reward.LoopValues ?? new object?[] { null })
            {
                var keep = Filter(reward, value);
                CardCreationOptions? options;
                try
                {
                    options = reward.Maker switch
                    {
                        ".ctor" when reward.Odds is { } odds => new CardCreationOptions(pools,
                            (CardCreationSource)(reward.Source ?? (int)CardCreationSource.Other), (CardRarityOddsType)odds, keep),
                        var name when typeof(CardCreationOptions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                            .FirstOrDefault(method => method.Name == name && method.ReturnType == typeof(CardCreationOptions) && method.GetParameters().Length == 2) is { } factory
                            => factory.Invoke(null, new object[] { pools, keep }) as CardCreationOptions,
                        _ => null,
                    };
                }
                catch { options = null; }
                if (options == null) return null;
                options = options.WithFlags((CardCreationFlags)reward.Flags);
                options = MegaCrit.Sts2.Core.Hooks.Hook.ModifyCardRewardCreationOptions(me.RunState, me, options);
                built.Add((options, ForRun(me, options.GetPossibleCards(me)).Distinct().ToList()));
            }
            return built;
        }

        /// <summary>
        /// The chance that what this option gives includes a card <paramref name="applies"/> accepts (docs/CARD_ODDS.md): 1 for a
        /// fixed card it accepts; for rewards, the chance at least one offered card is accepted (CardOdds, several rewards
        /// combined); for a random transform, the chance the new card is accepted. Null when it can't be told (a count or odds
        /// the code doesn't show, a pool used directly, rewards mixed with transforms). The upgrade roll a reward may make
        /// (Colorful Philosophers' rewards don't turn it off) isn't counted.
        /// </summary>
        public (double Low, double High)? Chance(Player me, Func<CardModel, bool> applies)
        {
            foreach (var type in _fixed)
            {
                if (ModelDb.AllCards.FirstOrDefault(canonicalCard => canonicalCard.GetType() == type) is { } card && applies(card)) return (1, 1);
            }
            var madeFrom = MadeFromValues();
            if (madeFrom.Any(applies)) return (1, 1);
            foreach (var poolSource in _loosePools)
            {
                if (Unlocked(Pool(poolSource, me), me).Any(applies)) return null;
            }
            // A reward the program doesn't hold, or an upgraded card it gives that the reading can't name (Dusty Tome's): unknown.
            if (RewardsUnread || GivesUpgraded && _fixed.Count == 0 && madeFrom.Count == 0 && _rewards.Count == 0 && _loosePools.Count == 0 && !_transformRandom) return null;
            var chances = new List<double>();
            foreach (var reward in _rewards)
            {
                if (Built(reward, me) is not { } built) return null;
                foreach (var (options, possible) in built)
                {
                    if (!possible.Any(applies)) continue;
                    if (reward.Count is not { } countExpr || Evaluate(countExpr) is not { } count) return null;
                    if (RewardChance(possible, applies, options.RarityOdds, count) is not { } rewardChance) return null;
                    chances.Add(rewardChance);
                }
            }
            if (_transformRandom)
            {
                if (chances.Count > 0) return null;
                return TransformChance(me, applies);
            }
            var any = SpireQoL.Sim.CardOdds.AnyOf(chances);
            return (any, any);
        }

        /// <summary>
        /// A random transform: the chance the new card is accepted. It's the same for every card you could transform, apart from
        /// two things in CardFactory.GetDefaultTransformationOptions: a card can't become itself (one card fewer when it's one of
        /// the results), and the pool depends on the card (yours; colorless for event, ancient, token and quest cards; every
        /// rarity for a curse or status). So one value per pool, from the card with the most results (nothing left out), and a
        /// range only when your deck's cards transform from different pools.
        /// </summary>
        private static (double Low, double High)? TransformChance(Player me, Func<CardModel, bool> applies)
        {
            var byPool = new Dictionary<string, (int Count, double Chance)>();
            foreach (var original in me.Deck.Cards)
            {
                List<CardModel> options;
                try { options = CardFactory.GetDefaultTransformationOptions(original, false).Select(card => card.IsCanonical ? card : card.CanonicalInstance).ToList(); }
                catch { continue; }
                if (options.Count == 0) continue;
                var colorless = original.Type == CardType.Quest || original.Rarity is CardRarity.Event or CardRarity.Ancient or CardRarity.Token;
                var anyRarity = original.Rarity is CardRarity.Status or CardRarity.Curse;
                var key = (colorless ? "colorless" : original.Pool?.Id.ToString() ?? "?") + (anyRarity ? "/all" : "");
                if (!byPool.TryGetValue(key, out var seen) || options.Count > seen.Count)
                    byPool[key] = (options.Count, (double)options.Count(applies) / options.Count);
            }
            if (byPool.Count == 0) return null;
            return (byPool.Values.Min(value => value.Chance), byPool.Values.Max(value => value.Chance));
        }

        /// <summary>
        /// Whether the change this option makes to your deck reaches a card the rule applies to: Mystic Lighter when one of its
        /// enchantments can go on an Attack in your deck (any Attack when the enchantment isn't named), Miniature Cannon when an
        /// Attack in your deck can be upgraded. A change it makes to the cards it gives is counted with those cards instead.
        /// </summary>
        public bool ChangeReaches(Player me, CardAffinityRule rule)
        {
            var deck = me.Deck.Cards.ToList();
            bool Base(CardModel card) => rule.Base == null || rule.Base(card);
            var enchantPicks = Picks.Where(pick => pick.Kind == ChoiceEffect.EnchantCard).ToList();
            var named = enchantPicks.SelectMany(pick => pick.Enchantments).ToHashSet();
            var others = _enchantments.Where(type => !named.Contains(type.Name)).ToList();
            return rule.Change switch
            {
                // An upgrade of your cards reaches one it can take (decision #358): a pick takes any upgradable card, War Paint's code only your Skills.
                CardChange.Upgrade => _upgradesDeck && deck.Any(card => Base(card) && card.IsUpgradable && (_upgradeTakes.Count == 0 || _upgradeTakes.Any(take => Takes(take, card)))),
                // A pick the reading names (Self-Help Book's: Sharp on an Attack you pick): a card of yours the rule applies to that the pick lets you
                // choose and its enchantment can go on (decision #353); an enchantment the code names besides, as before.
                CardChange.Enchant when enchantPicks.Count > 0 => deck.Any(card => Base(card) && (enchantPicks.Any(pick => Takes(pick, card)
                    && (pick.Enchantments.Count == 0 ? CanTakeAny(card, _enchantments) : pick.Enchantments.Any(name => EnchantmentNamed(name)?.CanEnchant(card) ?? true)))
                    || others.Any(type => EnchantmentOf(type)?.CanEnchant(card) ?? true))),
                CardChange.Enchant => _enchantsDeck && deck.Any(card => Base(card) && CanTakeAny(card, _enchantments)),
                _ => false,
            };
        }

        /// <summary>An enchantment of <paramref name="enchantments"/> can go on the card (any, when there are none).</summary>
        private static bool CanTakeAny(CardModel card, IReadOnlyCollection<Type> enchantments) =>
            enchantments.Count == 0 || enchantments.Any(type => EnchantmentOf(type)?.CanEnchant(card) ?? true);

        /// <summary>The pick lets you choose the card: its filter, on the card, as the game's code tests it (true when it has none or can't be told).</summary>
        private bool Takes(SpireQoL.Features.OptionPick pick, CardModel card) => Takes(pick.Filter, card);

        /// <summary>A test the code puts on a card of yours holds for it, as the game's code tests it (true when there's none or it can't be told).</summary>
        private bool Takes(LambdaValue? filter, CardModel card)
        {
            if (filter == null) return true;
            try
            {
                var board = ProgramBoard.Of(Holders().OfType<EventModel>().FirstOrDefault() ?? (object?)Target ?? card);
                return ProgramEvaluator.Passes(filter, card, board, ModelProgram.Empty) != false;
            }
            catch { return true; }
        }

        private static EnchantmentModel? EnchantmentNamed(string name) =>
            typeof(EnchantmentModel).Assembly.GetTypes().FirstOrDefault(type => type.Name == name && typeof(EnchantmentModel).IsAssignableFrom(type)) is { } type ? EnchantmentOf(type) : null;

        private static EnchantmentModel? EnchantmentOf(Type type)
        {
            try { return typeof(ModelDb).GetMethod(nameof(ModelDb.Enchantment))!.MakeGenericMethod(type).Invoke(null, null) as EnchantmentModel; }
            catch { return null; }
        }

        /// <summary>
        /// What the option draws from, for the Inspect screen (decision #64): one group per reward (per pass of a loop: Glass
        /// Eye's five), its possible cards, odds and card count; one per pool a random transform draws from (a card each); the
        /// cards it always gives; a pool used directly. Null when its cards can't be read.
        /// </summary>
        public List<DrawGroup>? Groups(Player me)
        {
            if (RewardsUnread) return null;
            var groups = new List<DrawGroup>();
            var fixedCards = _fixed.Select(type => ModelDb.AllCards.FirstOrDefault(card => card.GetType() == type)).OfType<CardModel>().ToList();
            fixedCards.AddRange(MadeFromValues().Where(card => !fixedCards.Contains(card)));
            if (fixedCards.Count > 0) groups.Add(new DrawGroup(DrawKind.Certain, fixedCards, CardRarityOddsType.Uniform, Count: null, GivesUpgraded));
            foreach (var reward in _rewards)
            {
                if (Built(reward, me) is not { } built) return null;
                var count = reward.Count is { } intExpr ? Evaluate(intExpr) : null;
                foreach (var (options, possible) in built)
                    groups.Add(new DrawGroup(DrawKind.Reward, OrderForShow(possible, options.RarityOdds), options.RarityOdds, count, GivesUpgraded));
            }
            foreach (var pool in _loosePools)
            {
                var cards = Unlocked(Pool(pool, me), me).ToList();
                if (cards.Count > 0) groups.Add(new DrawGroup(DrawKind.Pool, OrderForShow(cards, CardRarityOddsType.Uniform), CardRarityOddsType.Uniform, Count: null));
            }
            // A transform you don't choose (Pandora's Box's Strikes and Defends, a random card) draws from your whole pool:
            // nothing to inspect there. One you choose can be explored by the card you'd pick.
            if (_transformRandom && _picksTransform)
            {
                foreach (var cards in TransformPools(me))
                    groups.Add(new DrawGroup(DrawKind.Transform, OrderForShow(cards, CardRarityOddsType.Uniform), CardRarityOddsType.Uniform, Count: 1, GivesUpgraded));
            }
            return groups.Count == 0 ? null : groups;
        }

        /// <summary>Rarity, then name: the order the compendium uses.</summary>
        private static List<CardModel> OrderForShow(IEnumerable<CardModel> cards, CardRarityOddsType odds)
        {
            var list = cards.Distinct();
            // A reward (CreateForReward) never offers Basic or Ancient cards: uniform odds leave them out, and a rarity roll only
            // lands on Common, Uncommon or Rare (Infested Automaton's Study listed Lost Lute).
            if (odds != CardRarityOddsType.None) list = list.Where(card => card.Rarity != CardRarity.Basic && card.Rarity != CardRarity.Ancient);
            return list.OrderBy(card => card.Rarity).ThenBy(TitleOf, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>A card's title, or its class's name where its text can't be read (outside the game: the tests).</summary>
        private static string TitleOf(CardModel card)
        {
            try { return card.Title; }
            catch { return card.GetType().Name; }
        }

        /// <summary>What one card can transform into (CardFactory.GetDefaultTransformationOptions), as the Inspect screen lists it.</summary>
        internal static List<CardModel> TransformResults(CardModel original)
        {
            try { return OrderForShow(CardFactory.GetDefaultTransformationOptions(original, false).Select(card => card.IsCanonical ? card : card.CanonicalInstance), CardRarityOddsType.None); }
            catch { return new List<CardModel>(); }
        }

        /// <summary>The distinct pools your deck's cards transform from (see TransformChance), each as its full list of results.</summary>
        private static List<List<CardModel>> TransformPools(Player me)
        {
            var byPool = new Dictionary<string, List<CardModel>>();
            foreach (var original in me.Deck.Cards)
            {
                List<CardModel> options;
                try { options = CardFactory.GetDefaultTransformationOptions(original, false).Select(card => card.IsCanonical ? card : card.CanonicalInstance).ToList(); }
                catch { continue; }
                if (options.Count == 0) continue;
                var colorless = original.Type == CardType.Quest || original.Rarity is CardRarity.Event or CardRarity.Ancient or CardRarity.Token;
                var key = (colorless ? "colorless" : original.Pool?.Id.ToString() ?? "?") + (original.Rarity is CardRarity.Status or CardRarity.Curse ? "/all" : "");
                if (!byPool.TryGetValue(key, out var seen) || options.Count > seen.Count) byPool[key] = options;
            }
            return byPool.Values.ToList();
        }

        /// <summary>One reward: the game's draw over the possible cards (CreateForReward), as CardOdds counts it.</summary>
        internal static double? RewardChance(List<CardModel> possible, Func<CardModel, bool> applies, CardRarityOddsType odds, int count)
        {
            if (odds == CardRarityOddsType.Uniform)
            {
                // Uniform: every card but Basic and Ancient ones, equally likely.
                var pool = possible.Where(card => card.Rarity != CardRarity.Basic && card.Rarity != CardRarity.Ancient).ToList();
                return SpireQoL.Sim.CardOdds.AtLeastOneUniform(pool.Count, pool.Count(applies), count);
            }
            if (BaseOdds(odds) is not { } baseOdds) return null;
            var groups = new[] { CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare }
                .Select(cardRarity => possible.Where(card => card.Rarity == cardRarity).ToList())
                .Select(cards => new SpireQoL.Sim.RarityGroup(cards.Count, cards.Count(applies))).ToList();
            return SpireQoL.Sim.CardOdds.AtLeastOne(groups, baseOdds.Rare, baseOdds.Uncommon, count);
        }

        /// <summary>
        /// The chance a reward from <paramref name="possible"/> is of each rarity it has (the base odds, a roll for a rarity with no
        /// card moving on to the next: GetNextAllowedRarity), rarest first: "rare 1.5%, uncommon 98.5%". Null for uniform odds.
        /// </summary>
        internal static string? RarityRoll(List<CardModel> possible, CardRarityOddsType odds)
        {
            if (odds is CardRarityOddsType.Uniform or CardRarityOddsType.None || BaseOdds(odds) is not { } baseOdds) return null;
            var order = new[] { CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare };
            var has = order.Select(cardRarity => possible.Any(card => card.Rarity == cardRarity)).ToArray();
            if (!has.Any(present => present)) return null;
            var rolled = new[] { Math.Max(0, 1 - baseOdds.Rare - baseOdds.Uncommon), baseOdds.Uncommon, baseOdds.Rare };
            var lands = new double[3];
            for (var i = 0; i < 3; i++)
            {
                var rarityIndex = i;
                while (!has[rarityIndex]) rarityIndex = (rarityIndex + 1) % 3;
                lands[rarityIndex] += rolled[i];
            }
            static string Pct(double share) => share >= 0.1 ? $"{Math.Round(share * 100)}%" : $"{Math.Round(share * 100, 1)}%";
            return string.Join(", ", new[] { 2, 1, 0 }.Where(rarity => has[rarity]).Select(rarity => $"{order[rarity].ToString().ToLowerInvariant()} {Pct(lands[rarity])}"));
        }

        private static readonly MethodInfo? GetBaseOdds = AccessTools.Method(typeof(MegaCrit.Sts2.Core.Odds.CardRarityOdds), "GetBaseOdds");

        /// <summary>The game's base rarity odds (CardRarityOdds.GetBaseOdds: events roll with these, no rarity offset).</summary>
        private static (double Rare, double Uncommon)? BaseOdds(CardRarityOddsType odds)
        {
            try
            {
                var rare = Convert.ToDouble(GetBaseOdds!.Invoke(null, new object[] { odds, CardRarity.Rare }));
                var uncommon = Convert.ToDouble(GetBaseOdds.Invoke(null, new object[] { odds, CardRarity.Uncommon }));
                return (rare, uncommon);
            }
            catch { return null; }
        }

        /// <summary>
        /// A number the program's value works out to on the objects running the code (decision #357): a constant, a value of theirs (DynamicVars.Cards:
        /// the event's, the relic's), one divided by another (Sea Glass's Cards / 3); null when it can't be told.
        /// </summary>
        private int? Evaluate(ProgramValue value)
        {
            switch (value)
            {
                case StoredValue stored:
                    return Evaluate(stored.Value);
                case ConstantValue constant:
                    return (int)constant.Value;
                case ArithmeticValue { Op: "div" } division:
                    return Evaluate(division.Left) is { } left && Evaluate(division.Right) is { } right && right != 0 ? left / right : null;
                case CardValue card:
                    foreach (var holder in Holders())
                    {
                        // Events, relics, cards: each model type has its own DynamicVars.
                        if (holder.GetType().GetProperty("DynamicVars")?.GetValue(holder) is not MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVarSet vars) continue;
                        try { return (int)vars[card.Key].IntValue; }
                        catch { /* not this one */ }
                    }
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>A reward's pools, one value each (a list or array the code builds of them; none for Array.Empty).</summary>
        private static IEnumerable<ProgramValue> PoolValues(ProgramValue pools) => pools switch
        {
            StoredValue stored => PoolValues(stored.Value),
            ArrayValue array => array.Items.SelectMany(PoolValues),
            QueryValue { Type: "Array", Name: "Empty" } => Enumerable.Empty<ProgramValue>(),
            QueryValue { Name: ".ctor", Of: null } list when list.Type != "CardPoolModel" => list.Arguments.SelectMany(PoolValues),
            _ => new[] { pools },
        };

        /// <summary>
        /// A pool as the program's value names it: the game's (ModelDb.CardPool&lt;ColorlessCardPool&gt;), a character's (yours: a player's Character; the
        /// model's own: Sea Glass's), or one passed in (Colorful Philosophers' pool, the one the option's delegate holds). Null when it can't be told.
        /// </summary>
        private CardPoolModel? PoolOf(ProgramValue pool, Type owner, Player me)
        {
            try
            {
                return pool switch
                {
                    StoredValue stored => PoolOf(stored.Value, owner, me),
                    QueryValue { Type: "ModelDb", Name: "CardPool", GenericArguments: [var name] } =>
                        typeof(CardPoolModel).Assembly.GetTypes().FirstOrDefault(type => type.Name == name && typeof(CardPoolModel).IsAssignableFrom(type)) is { } poolType
                            ? typeof(ModelDb).GetMethod(nameof(ModelDb.CardPool))!.MakeGenericMethod(poolType).Invoke(null, null) as CardPoolModel : null,
                    QueryValue { Name: "CardPool", Of: { } character } => CharacterOf(character, owner, me)?.CardPool,
                    NamedValue { Name: not "this" } or ParameterValue => OptionPool(),
                    _ => null,
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// The character whose pool a reward takes: the model's own (this.Character: Sea Glass's, on the object running the code), else a player's,
        /// yours (Owner.Character, a player passed in); null when it can't be told.
        /// </summary>
        private CharacterModel? CharacterOf(ProgramValue character, Type owner, Player me)
        {
            if (character is not QueryValue { Name: "Character" } query) return null;
            if (query.Of is not NamedValue { Name: "this" }) return me.Character;
            try { return HolderOf(owner)?.GetType().GetProperty("Character", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(HolderOf(owner)) as CharacterModel; }
            catch { return null; }
        }

        /// <summary>The object the code of <paramref name="owner"/> runs on, among the option's (its event, a relic it gives); null when it's none of them (an event's part).</summary>
        private object? HolderOf(Type owner) => Holders().FirstOrDefault(owner.IsInstanceOfType);

        /// <summary>The objects the option's code runs on: its delegate target, the event a closure holds, the relic it gives.</summary>
        private IEnumerable<object> Holders()
        {
            if (Target != null)
            {
                yield return Target;
                if (ThisField(Target.GetType())?.GetValue(Target) is { } outer) yield return outer;
            }
            if (Relic != null) yield return Relic;
            foreach (var held in _heldTargets) yield return held;
            // A relic whose obtaining the reading read, by its class (Fragrant Mushroom): its canonical model's values.
            foreach (var relic in _relics)
                if (Relic?.GetType() != relic && ModelDb.AllRelics.FirstOrDefault(model => model.GetType() == relic) is { } canonical) yield return canonical;
        }

        private static FieldInfo? ThisField(Type type) =>
            type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(field => field.Name.EndsWith("__this", StringComparison.Ordinal));

        /// <summary>The card pool the option's delegate holds (a closure field), if any.</summary>
        private CardPoolModel? OptionPool()
        {
            if (Target == null) return null;
            return Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => typeof(CardPoolModel).IsAssignableFrom(field.FieldType)).Select(field => field.GetValue(Target) as CardPoolModel)
                .FirstOrDefault(cardPoolModel => cardPoolModel != null);
        }

        /// <summary>A pool the code names without a reward: the game's, or a character's (yours, or the model's own).</summary>
        private CardPoolModel? Pool(PoolSource source, Player me)
        {
            try
            {
                if (source.Method.Name != "get_CardPool") return ((MethodInfo)source.Method).Invoke(null, null) as CardPoolModel;
                if (source.Character == null || typeof(Player).IsAssignableFrom(source.Character.DeclaringType)) return me.Character.CardPool;
                if (source.Character.IsStatic) return (source.Character.Invoke(null, null) as CharacterModel)?.CardPool;
                foreach (var on in Holders())
                    if (source.Character.DeclaringType!.IsInstanceOfType(on)) return (source.Character.Invoke(on, null) as CharacterModel)?.CardPool;
            }
            catch { /* unknown */ }
            return null;
        }

        private static IEnumerable<CardModel> Unlocked(CardPoolModel? pool, Player me) =>
            pool == null ? Enumerable.Empty<CardModel>() : pool.GetUnlockedCards(me.UnlockState, me.RunState.CardMultiplayerConstraint);

        private static IEnumerable<CardModel> ForRun(Player me, IEnumerable<CardModel> cards)
        {
            try { return FilterForRun?.Invoke(null, new object[] { me.RunState, cards }) as IEnumerable<CardModel> ?? cards; }
            catch { return cards; }
        }

        /// <summary>
        /// A reward's filter as a predicate (decision #357): its lambda evaluated on each card as the game's code tests it (ProgramEvaluator), on the
        /// object its code runs on, with a loop's value for this pass (Glass Eye's rarity). A filter that can't be told keeps the card.
        /// </summary>
        private Func<CardModel, bool> Filter(RewardSource reward, object? loopValue)
        {
            if (reward.Filter is not { } filter) return _ => true;
            if (reward.LoopName is { } name && loopValue != null)
            {
                var constant = new ConstantValue(Convert.ToInt64(loopValue, System.Globalization.CultureInfo.InvariantCulture));
                filter = (LambdaValue)filter.Map(part => IsName(part, name) ? constant : null);
            }
            var board = ProgramBoard.Of(HolderOf(reward.Owner) ?? new object());
            return card =>
            {
                try { return ProgramEvaluator.Passes(filter, card, board, ModelProgram.Empty) != false; }
                catch { return true; }
            };
        }
    }
}

/// <summary>How a group of the Inspect screen is drawn from.</summary>
internal enum DrawKind
{
    /// <summary>A card reward or random card: Count different cards, by its rarity odds.</summary>
    Reward,
    /// <summary>A random transform: each card you transform becomes one of these, equally likely.</summary>
    Transform,
    /// <summary>Cards the option always gives.</summary>
    Certain,
    /// <summary>A pool the code picks from some other way (no odds).</summary>
    Pool,
}

/// <param name="Count">Cards drawn (a reward's offer, 1 for a transform); null when it can't be read or doesn't apply.</param>
/// <param name="Upgraded">The option upgrades the cards it gives (Astrolabe): relic tabs are judged on them upgraded.</param>
internal sealed record DrawGroup(DrawKind Kind, List<CardModel> Cards, CardRarityOddsType Odds, int? Count, bool Upgraded = false)
{
    /// <summary>The chance at least one card <paramref name="good"/> accepts comes up from this group (null: no odds for it).</summary>
    public double? Chance(Func<CardModel, bool> good) => Kind switch
    {
        DrawKind.Certain => Cards.Any(good) ? 1 : 0,
        DrawKind.Transform => Cards.Count == 0 ? null : (double)Cards.Count(good) / Cards.Count,
        DrawKind.Reward when Count is { } count => RunReminders.OptionCards.RewardChance(Cards, good, Odds, count),
        _ => null,
    };

    /// <summary>A random draw the player can't see beforehand (worth inspecting): more cards than it gives.</summary>
    public bool IsRandom => Kind is DrawKind.Transform or DrawKind.Pool || (Kind == DrawKind.Reward && (Count is not { } count || Cards.Count > count));
}
