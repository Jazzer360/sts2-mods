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
using LocalBuilder = System.Reflection.Emit.LocalBuilder;
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
public sealed partial class RunRemindersFeature
{
    private static readonly ConditionalWeakTable<EventOption, OptionCards> CardsRead = new();
    private static readonly ConditionalWeakTable<EventOption, StrongBox<List<CardModel>?>> CardsCache = new();

    private static readonly MethodInfo? FilterForRun = AccessTools.Method(typeof(CardFactory), "FilterForRun");

    /// <summary>Whether a card the rule applies to can come from this option (true when its cards can't be read).</summary>
    private static bool CanGet(EventOption option, Player me, CardAffinityRule rule)
    {
        var cards = CardsOf(option, me);
        var held = Held(me);
        var upgraded = CardsRead.TryGetValue(option, out var read) && read.GivesUpgraded;
        return cards == null || cards.Any(card => CardAffinity.AppliesWith(rule, card, held, upgraded));
    }

    /// <summary>Whether the change this option makes to your cards (an upgrade, an enchantment) can reach one the rule applies to.</summary>
    private static bool ChangeReaches(EventOption option, Player me, CardAffinityRule rule)
    {
        EffectsOf(option);
        return !CardsRead.TryGetValue(option, out var read) || read.ChangeReaches(me, rule);
    }

    private static readonly ConditionalWeakTable<EventOption, Dictionary<string, string?>> OddsCache = new();

    /// <summary>
    /// The chance under a relic's icon (docs/CARD_ODDS.md): "43%", or a range for a transform whose cards differ in pool; null when
    /// it's sure or can't be told. Cached per option and relic.
    /// </summary>
    private static string? OddsText(EventOption option, Player me, CardAffinityRule rule)
    {
        var cache = OddsCache.GetValue(option, _ => new Dictionary<string, string?>());
        if (cache.TryGetValue(rule.Relic, out var text)) return text;
        text = null;
        try
        {
            if (CardsRead.TryGetValue(option, out var read))
            {
                var held = Held(me);
                if (read.Chance(me, card => CardAffinity.AppliesWith(rule, card, held, read.GivesUpgraded)) is { } chance && chance.Low < 1)
                {
                    text = Percent(chance.Low) == Percent(chance.High) ? Percent(chance.Low) + "%" : $"{Percent(chance.Low)}–{Percent(chance.High)}%";
                }
            }
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders-odds:" + option.TextKey);
        }
        cache[rule.Relic] = text;
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
        EffectsOf(option);
        List<CardModel>? list = null;
        try
        {
            if (CardsRead.TryGetValue(option, out var read)) list = read.Candidates(me);
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders-cards:" + option.TextKey);
            list = null;
        }
        CardsCache.AddOrUpdate(option, new StrongBox<List<CardModel>?>(list));
        return list;
    }

    /// <summary>What an option's code says about the cards it makes (filled while walking it).</summary>
    internal sealed class OptionCards
    {
        /// <summary>The option's delegate target (the event, or a closure over it): the filter lambdas' <c>this</c>.</summary>
        public object? Target;
        /// <summary>An ancient's relic the option gives (its AfterObtained is read too): Sea Glass's character, say.</summary>
        public object? Relic;

        /// <summary>A card pool in the code, and the call that gave the character whose pool it is (null: not a character's).</summary>
        private sealed record PoolSource(MethodBase Method, MethodBase? Character);

        /// <summary>
        /// A number in the code: a constant, a dynamic var of the object running it (<c>DynamicVars.Cards.IntValue</c>), or one
        /// divided by a constant (Sea Glass's <c>Cards / 3</c>).
        /// </summary>
        private sealed record IntExpr(int? Const, string? Var, int Divisor = 1);

        /// <summary>A card reward or random card, as its code builds it.</summary>
        private sealed class RewardSource
        {
            public required List<PoolSource> Pools;
            /// <summary>The CardCreationOptions factory or constructor the code calls.</summary>
            public required MethodBase Maker;
            public MethodInfo? Filter;
            /// <summary>The constructor's source and odds (CardCreationSource, CardRarityOddsType), read as numbers.</summary>
            public int? Source, Odds;
            /// <summary>Flags added with WithFlags; IsCardReward when a CardReward takes it.</summary>
            public int Flags;
            public IntExpr? Count;
            /// <summary>A filter closure's captured value set per pass of a loop over a constant array (Glass Eye's rarities).</summary>
            public FieldInfo? LoopField;
            public object[]? LoopValues;
        }

        private readonly HashSet<Type> _fixed = new();
        private readonly HashSet<Type> _enchantments = new();
        private readonly List<RewardSource> _rewards = new();
        private readonly List<PoolSource> _loosePools = new();
        private bool _transformRandom;
        /// <summary>You pick the card to transform (CardSelectCmd.FromDeckForTransformation), rather than all of a kind or one at random.</summary>
        private bool _picksTransform;
        private bool _upgradesDeck;
        private bool _enchantsDeck;

        /// <summary>The option upgrades the cards it gives (Astrolabe: CardCmd.Upgrade on its new cards, not a pick from your deck).</summary>
        public bool GivesUpgraded { get; private set; }

        /// <summary>One method body: pools and a filter are paired with the CardCreationOptions call that follows them.</summary>
        public void Read(List<CodeInstruction> code)
        {
            var pools = new List<PoolSource>();
            MethodInfo? filter = null;
            MethodBase? character = null;
            // The last number pushed (for a reward's count, flags and a constructor's odds), the one before it (a division, the
            // constructor's source), the dynamic var named last, and numbers stored in locals or fields (an async method's
            // locals are fields).
            IntExpr? last = null, before = null;
            string? lastString = null, varName = null;
            var stored = new Dictionary<string, IntExpr>();
            CodeInstruction? previous = null;
            // Constant arrays of an enum (Glass Eye's rarities): built element by element, or from a data field.
            var arrays = new Dictionary<Type, object[]>();
            (Type Element, int Length, List<object> Values)? building = null;
            FieldInfo? arrayData = null;
            var upgrades = false;
            var picksUpgrade = false;
            void Push(IntExpr intExpr) { before = last; last = intExpr; }
            foreach (var ins in code)
            {
                var producedInt = previous != null && ProducesInt(previous);
                previous = ins;
                if (ins.opcode == OpCodes.Newarr && ins.operand is Type { IsEnum: true } element && last is { Const: { } length })
                {
                    building = (element, length, new List<object>());
                    continue;
                }
                if (ins.opcode == OpCodes.Ldtoken && ins.operand is FieldInfo data) { arrayData = data; continue; }
                if (building is { } elementLength && ins.opcode.Name?.StartsWith("stelem") == true && last is { Const: { } value })
                {
                    elementLength.Values.Add(Enum.ToObject(elementLength.Element, value));
                    if (elementLength.Values.Count == elementLength.Length) { arrays[elementLength.Element] = elementLength.Values.ToArray(); building = null; }
                    continue;
                }
                if (ins.LoadsConstant() && ins.operand is not string && TryConst(ins, out var constant)) { Push(new IntExpr(constant, Var: null)); continue; }
                if (ins.opcode == OpCodes.Ldstr) { lastString = ins.operand as string; continue; }
                if (ins.opcode == OpCodes.Div && last is { Const: { } divisor } && before is { Var: not null } && divisor != 0) { last = before with { Divisor = before.Divisor * divisor }; continue; }
                if (SlotKey(ins, store: true) is { } slotKey) { if (producedInt && last != null) stored[slotKey] = last; continue; }
                if (SlotKey(ins, store: false) is { } otherSlotKey) { if (stored.TryGetValue(otherSlotKey, out var val)) Push(val); continue; }
                if (ins.opcode == OpCodes.Ldftn && ins.operand is MethodInfo method && IsCardFilter(method))
                {
                    filter = method;
                    continue;
                }
                if (ins.operand is not MethodBase otherMethod || otherMethod.DeclaringType == null) continue;
                if (ins.opcode != OpCodes.Call && ins.opcode != OpCodes.Callvirt && ins.opcode != OpCodes.Newobj) continue;
                var type = otherMethod.DeclaringType;
                if (otherMethod.Name == "InitializeArray" && type == typeof(RuntimeHelpers) && building is { } init && arrayData != null)
                {
                    try
                    {
                        var array = Array.CreateInstance(init.Element, init.Length);
                        RuntimeHelpers.InitializeArray(array, arrayData.FieldHandle);
                        arrays[init.Element] = array.Cast<object>().ToArray();
                    }
                    catch { /* not a constant array we can read */ }
                    building = null;
                    continue;
                }
                if (type.Name == "DynamicVarSet" && otherMethod.Name.StartsWith("get_")) varName = otherMethod.Name == "get_Item" ? lastString : otherMethod.Name[4..];
                else if (otherMethod.Name == "get_IntValue" && type.Name.EndsWith("Var") && varName != null) Push(new IntExpr(Const: null, varName));
                else if (IsRewardMaker(otherMethod))
                {
                    // CardReward(options, count, player) / CardFactory.CreateForReward(player, count, options): the count is the
                    // last number pushed; it goes to the first reward still without one.
                    if (_rewards.FirstOrDefault(rewardSource => rewardSource.Count == null) is { } open)
                    {
                        open.Count = last;
                        if (otherMethod.IsConstructor) open.Flags |= (int)CardCreationFlags.IsCardReward;
                    }
                }
                else if (type == typeof(CardCreationOptions) && otherMethod.Name == nameof(CardCreationOptions.WithFlags) && _rewards.Count > 0 && last is { Const: { } flags })
                    _rewards[^1].Flags |= flags;
                if (otherMethod.Name == "get_CardPool" && typeof(CharacterModel).IsAssignableFrom(type)) pools.Add(new PoolSource(otherMethod, character));
                else if (type == typeof(ModelDb) && otherMethod is MethodInfo { IsGenericMethod: true } nextMethod && nextMethod.Name == nameof(ModelDb.CardPool)) pools.Add(new PoolSource(otherMethod, Character: null));
                else if (otherMethod is MethodInfo { IsGenericMethod: true } gc && (gc.Name is "Card" or "CreateCard" or "TransformTo")
                    && gc.GetGenericArguments()[0] is { } otherType && typeof(CardModel).IsAssignableFrom(otherType) && !otherType.IsAbstract)
                    _fixed.Add(otherType);
                else if (otherMethod is MethodInfo { IsGenericMethod: true } ge && (ge.Name is "Enchantment" or "Enchant")
                    && ge.GetGenericArguments()[0] is { } nextType && typeof(EnchantmentModel).IsAssignableFrom(nextType) && !nextType.IsAbstract)
                    _enchantments.Add(nextType);
                else if (type == typeof(CardCreationOptions) && (otherMethod.IsConstructor || (otherMethod is MethodInfo f && f.IsStatic && f.ReturnType == typeof(CardCreationOptions))))
                {
                    var reward = new RewardSource
                    {
                        // No pool loaded here: it's passed in (Colorful Philosophers' option for one character's pool), so it's
                        // the CardPoolModel the option's delegate holds.
                        Pools = pools.Count == 0 ? new List<PoolSource> { FromOption } : pools.ToList(),
                        Maker = otherMethod,
                        Filter = filter,
                        // The constructor takes (pools, source, odds, filter): its odds are the last number pushed, its source the one before.
                        Odds = otherMethod.IsConstructor ? last?.Const : null,
                        Source = otherMethod.IsConstructor ? before?.Const : null,
                    };
                    // A closure capturing one value of an enum we saw a constant array of: the loop's values (Glass Eye).
                    if (filter is { IsStatic: false } && filter.DeclaringType is { } closure && ThisField(closure) is var self
                        && closure.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(field => field != self).ToList() is { Count: 1 } captured
                        && arrays.TryGetValue(captured[0].FieldType, out var values))
                    {
                        reward.LoopField = captured[0];
                        reward.LoopValues = values;
                    }
                    _rewards.Add(reward);
                    pools.Clear();
                    filter = null;
                }
                else if ((type == typeof(MegaCrit.Sts2.Core.Commands.CardCmd) && (otherMethod.Name == "TransformToRandom"
                        || (otherMethod.Name == "Transform" && otherMethod.GetParameters().Any(parameterInfo => parameterInfo.ParameterType.Name == "Rng"))))
                    || (type == typeof(CardFactory) && otherMethod.Name == nameof(CardFactory.CreateRandomCardForTransform)))
                    _transformRandom = true;
                if (type == typeof(MegaCrit.Sts2.Core.Commands.CardCmd) && otherMethod.Name.StartsWith("Upgrade")) upgrades = true;
                if (type.Name == "CardSelectCmd" && otherMethod.Name == "FromDeckForUpgrade") picksUpgrade = true;
                if (type.Name == "CardSelectCmd" && otherMethod.Name == "FromDeckForTransformation") _picksTransform = true;
                if ((type == typeof(MegaCrit.Sts2.Core.Commands.CardCmd) && otherMethod.Name.StartsWith("Enchant")) || (type.Name == "CardSelectCmd" && otherMethod.Name == "FromDeckForEnchantment"))
                    _enchantsDeck = true;
                // Whose character a pool belongs to: the last call that returned one (Owner.Character, or Sea Glass's Character).
                if (otherMethod is MethodInfo { ReturnType: var returnType } && typeof(CharacterModel).IsAssignableFrom(returnType)) character = otherMethod;
            }
            _loosePools.AddRange(pools);
            // An upgrade with no pick from your deck in the same code is of the cards it makes (Astrolabe's new cards); with a
            // pick, it's of your cards.
            if (upgrades && !picksUpgrade && (_fixed.Count > 0 || _transformRandom || _rewards.Count > 0)) GivesUpgraded = true;
            else if (upgrades || picksUpgrade) _upgradesDeck = true;
        }

        /// <summary>For the tests: what was read (fixed cards, rewards, rewards with a filter, transform, rewards whose pool the option passes in, pools of a character other than yours).</summary>
        internal (int Fixed, int Rewards, int Filters, bool Transform, int PoolFromOption, int OtherCharacterPools) Summary =>
            (_fixed.Count, _rewards.Count, _rewards.Count(rewardSource => rewardSource.Filter != null), _transformRandom, _rewards.Count(rewardSource => rewardSource.Pools.Contains(FromOption)),
             _rewards.SelectMany(rewardSource => rewardSource.Pools).Count(poolSource => poolSource.Character != null && !typeof(Player).IsAssignableFrom(poolSource.Character.DeclaringType)));

        /// <summary>For the tests: each reward's odds (null: the factory's own), count ("Cards", "Cards/3", "3", or "?") and loop values.</summary>
        internal List<(string Maker, string Count, string Loop)> RewardsRead =>
            _rewards.Select(rewardSource => (rewardSource.Maker.IsConstructor ? "new:" + rewardSource.Odds : rewardSource.Maker.Name,
                rewardSource.Count is { } intExpr ? (intExpr.Const?.ToString() ?? intExpr.Var!) + (intExpr.Divisor != 1 ? "/" + intExpr.Divisor : "") : "?",
                rewardSource.LoopValues is { } loopValues ? string.Join(",", loopValues) : "")).ToList();

        /// <summary>For the tests: whether you pick the card a transform turns.</summary>
        internal bool PicksTransform => _picksTransform;

        /// <summary>For the tests: the enchantments the code applies, and how it upgrades.</summary>
        internal (string Enchantments, bool GivesUpgraded, bool UpgradesDeck) ChangesRead =>
            (string.Join(",", _enchantments.Select(type => type.Name).OrderBy(name => name)), GivesUpgraded, _upgradesDeck);

        private static bool IsRewardMaker(MethodBase method) =>
            (method.IsConstructor && method.DeclaringType?.Name == "CardReward" && method.GetParameters().Any(parameterInfo => parameterInfo.ParameterType == typeof(CardCreationOptions)))
            || (method.DeclaringType == typeof(CardFactory) && method.Name == nameof(CardFactory.CreateForReward) && method.GetParameters().Length == 3);

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

        private static bool ProducesInt(CodeInstruction ins) =>
            TryConst(ins, out _) || ins.opcode == OpCodes.Div || (ins.operand is MethodBase { Name: "get_IntValue" });

        /// <summary>A local or field an instruction stores to (store) or loads from: "loc:3", "fld:&lt;cardCount&gt;5__2".</summary>
        private static string? SlotKey(CodeInstruction ins, bool store)
        {
            if (store ? ins.IsStloc() : ins.IsLdloc())
            {
                var idx = ins.operand switch { LocalBuilder localBuilder => localBuilder.LocalIndex, int index => index, byte shortIndex => shortIndex, _ => LocalIndex(ins) };
                return idx < 0 ? null : "loc:" + idx;
            }
            if (ins.opcode == (store ? OpCodes.Stfld : OpCodes.Ldfld) && ins.operand is FieldInfo field && field.FieldType == typeof(int)) return "fld:" + field.Name;
            return null;
        }

        private static int LocalIndex(CodeInstruction ins)
        {
            var name = ins.opcode.Name ?? "";
            var dot = name.LastIndexOf('.');
            return dot >= 0 && int.TryParse(name[(dot + 1)..], out var number) ? number : -1;
        }

        private static bool IsCardFilter(MethodInfo method) =>
            method.ReturnType == typeof(bool) && method.GetParameters() is { Length: 1 } parameterInfos && typeof(CardModel).IsAssignableFrom(parameterInfos[0].ParameterType);

        /// <summary>The cards (canonical) this code can give, or null when a reward's pool couldn't be read.</summary>
        public List<CardModel>? Candidates(Player me)
        {
            var set = new HashSet<CardModel>();
            foreach (var type in _fixed)
            {
                var card = ModelDb.AllCards.FirstOrDefault(canonicalCard => canonicalCard.GetType() == type);
                if (card != null) set.Add(card);
            }
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
            if (reward.Pools.Contains(FromOption) && OptionPool() == null) return null;
            // A pool whose character can't be told: the cards are unknown, so the icons stay.
            if (reward.Pools.Any(poolSource => poolSource.Method.Name == "get_CardPool" && CharacterOf(poolSource.Character, me) == null)) return null;
            var pools = reward.Pools.Select(poolSource => Pool(poolSource, me)).OfType<CardPoolModel>().Distinct().ToList();
            var built = new List<(CardCreationOptions, List<CardModel>)>();
            foreach (var value in reward.LoopValues ?? new object?[] { null })
            {
                var keep = Filter(reward.Filter, reward.LoopField, value);
                CardCreationOptions? options;
                try
                {
                    options = reward.Maker switch
                    {
                        ConstructorInfo when reward.Odds is { } odds => new CardCreationOptions(pools,
                            (CardCreationSource)(reward.Source ?? (int)CardCreationSource.Other), (CardRarityOddsType)odds, keep),
                        MethodInfo { IsStatic: true } factory when factory.GetParameters().Length == 2 => factory.Invoke(null, new object[] { pools, keep }) as CardCreationOptions,
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
            foreach (var poolSource in _loosePools)
            {
                if (Unlocked(Pool(poolSource, me), me).Any(applies)) return null;
            }
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
            return rule.Change switch
            {
                CardChange.Upgrade => _upgradesDeck && deck.Any(card => Base(card) && card.IsUpgradable),
                CardChange.Enchant => _enchantsDeck && deck.Any(card => Base(card) && (_enchantments.Count == 0
                    || _enchantments.Any(type => EnchantmentOf(type)?.CanEnchant(card) ?? true))),
                _ => false,
            };
        }

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
            var groups = new List<DrawGroup>();
            var fixedCards = _fixed.Select(type => ModelDb.AllCards.FirstOrDefault(card => card.GetType() == type)).OfType<CardModel>().ToList();
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
            return list.OrderBy(card => card.Rarity).ThenBy(card => card.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
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

        /// <summary>A number read from the code, on the objects running it (their DynamicVars).</summary>
        private int? Evaluate(IntExpr intExpr)
        {
            int? value = intExpr.Const;
            if (intExpr.Var != null)
            {
                foreach (var holder in Holders())
                {
                    // Events, relics, cards: each model type has its own DynamicVars.
                    if (holder.GetType().GetProperty("DynamicVars")?.GetValue(holder) is not MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVarSet vars) continue;
                    try
                    {
                        value = (int)vars[intExpr.Var].IntValue;
                        break;
                    }
                    catch { /* not this one */ }
                }
            }
            return value is { } known && intExpr.Divisor != 0 ? known / intExpr.Divisor : null;
        }

        /// <summary>Stands for "the pool the option passes in" in a reward's pools.</summary>
        private static readonly PoolSource FromOption = new(AccessTools.Method(typeof(OptionCards), nameof(Candidates)), Character: null);

        private CardPoolModel? Pool(PoolSource source, Player me)
        {
            if (source == FromOption) return OptionPool();
            if (source.Method.Name == "get_CardPool") return CharacterOf(source.Character, me)?.CardPool;
            try { return ((MethodInfo)source.Method).Invoke(null, null) as CardPoolModel; }
            catch { return null; }
        }

        /// <summary>
        /// The character a pool belongs to: yours (a Player's Character, or no call seen), or what the call gives on the object
        /// running the code (the relic, the event, a closure); null when it can't be told.
        /// </summary>
        private CharacterModel? CharacterOf(MethodBase? getter, Player me)
        {
            if (getter == null || typeof(Player).IsAssignableFrom(getter.DeclaringType)) return me.Character;
            try
            {
                if (getter.IsStatic) return getter.Invoke(null, null) as CharacterModel;
                foreach (var on in Holders())
                {
                    if (getter.DeclaringType!.IsInstanceOfType(on)) return getter.Invoke(on, null) as CharacterModel;
                }
            }
            catch { /* unknown */ }
            return null;
        }

        /// <summary>The objects the option's code runs on: its delegate target, the event a closure holds, the relic it gives.</summary>
        private IEnumerable<object> Holders()
        {
            if (Target != null)
            {
                yield return Target;
                if (ThisField(Target.GetType())?.GetValue(Target) is { } outer) yield return outer;
            }
            if (Relic != null) yield return Relic;
        }

        private static FieldInfo? ThisField(Type type) =>
            type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(field => field.Name.EndsWith("__this"));

        /// <summary>The card pool the option's delegate holds (a closure field), if any.</summary>
        private CardPoolModel? OptionPool()
        {
            if (Target == null) return null;
            return Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => typeof(CardPoolModel).IsAssignableFrom(field.FieldType)).Select(field => field.GetValue(Target) as CardPoolModel)
                .FirstOrDefault(cardPoolModel => cardPoolModel != null);
        }

        private static IEnumerable<CardModel> Unlocked(CardPoolModel? pool, Player me) =>
            pool == null ? Enumerable.Empty<CardModel>() : pool.GetUnlockedCards(me.UnlockState, me.RunState.CardMultiplayerConstraint);

        private static IEnumerable<CardModel> ForRun(Player me, IEnumerable<CardModel> cards)
        {
            try { return FilterForRun?.Invoke(null, new object[] { me.RunState, cards }) as IEnumerable<CardModel> ?? cards; }
            catch { return cards; }
        }

        /// <summary>
        /// The filter as a predicate: a static lambda, a lambda on the compiler's singleton (<c>&lt;&gt;9</c>), a method of the
        /// event itself, or a closure over the event (<c>&lt;&gt;4__this</c>) and, for a loop, the value of this pass. A closure
        /// holding another value it can't be given keeps every card; a filter that throws keeps the card.
        /// </summary>
        private Func<CardModel, bool> Filter(MethodInfo? method, FieldInfo? loopField = null, object? loopValue = null)
        {
            if (method == null) return _ => true;
            object? inst = null;
            if (!method.IsStatic)
            {
                var type = method.DeclaringType!;
                inst = Holders().FirstOrDefault(type.IsInstanceOfType);
                if (inst == null && type.GetField("<>9", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is { } nine) inst = nine.GetValue(null);
                if (inst == null)
                {
                    var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var self = ThisField(type);
                    if (fields.Any(field => field != self && field != loopField)) return _ => true;
                    inst = RuntimeHelpers.GetUninitializedObject(type);
                    var owner = Holders().FirstOrDefault(holder => self != null && self.FieldType.IsInstanceOfType(holder));
                    if (self != null && owner != null) self.SetValue(inst, owner);
                    if (loopField != null && loopValue != null) loopField.SetValue(inst, loopValue);
                }
            }
            return card =>
            {
                try { return method.Invoke(inst, new object[] { card }) is true; }
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
        DrawKind.Reward when Count is { } count => RunRemindersFeature.OptionCards.RewardChance(Cards, good, Odds, count),
        _ => null,
    };

    /// <summary>A random draw the player can't see beforehand (worth inspecting): more cards than it gives.</summary>
    public bool IsRandom => Kind is DrawKind.Transform or DrawKind.Pool || (Kind == DrawKind.Reward && (Count is not { } count || Cards.Count > count));
}
