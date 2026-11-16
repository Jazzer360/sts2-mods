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
        return cards == null || cards.Any(c => CardAffinity.AppliesWith(rule, c, held, upgraded));
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
                if (read.Chance(me, c => CardAffinity.AppliesWith(rule, c, held, read.GivesUpgraded)) is { } chance && chance.Low < 1)
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
    private static string Percent(double p) => p <= 0 ? "0" : p < 0.01 ? "<1" : Math.Min(99, (int)Math.Round(p * 100)).ToString();

    /// <summary>The chance, small, along an icon's bottom edge (like the amount on a tab).</summary>
    private static Label OddsLabel(string text, float iconSize)
    {
        var l = UiKit.Label(text, Math.Max(12, (int)(iconSize * 0.36f)), Colors.White);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.Size = new Vector2(iconSize + 16, 0);
        l.Position = new Vector2(-8, iconSize * 0.62f);
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        return l;
    }

    /// <summary>Your relics' card rules (the modifiers a boost can count on).</summary>
    private static List<CardAffinityRule> Held(Player me) =>
        me.Relics.Select(r => CardAffinity.For(r.GetType().Name)).OfType<CardAffinityRule>().ToList();

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
            void Push(IntExpr e) { before = last; last = e; }
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
                if (building is { } b && ins.opcode.Name?.StartsWith("stelem") == true && last is { Const: { } value })
                {
                    b.Values.Add(Enum.ToObject(b.Element, value));
                    if (b.Values.Count == b.Length) { arrays[b.Element] = b.Values.ToArray(); building = null; }
                    continue;
                }
                if (ins.LoadsConstant() && ins.operand is not string && TryConst(ins, out var k)) { Push(new IntExpr(k, null)); continue; }
                if (ins.opcode == OpCodes.Ldstr) { lastString = ins.operand as string; continue; }
                if (ins.opcode == OpCodes.Div && last is { Const: { } d } && before is { Var: not null } && d != 0) { last = before with { Divisor = before.Divisor * d }; continue; }
                if (SlotKey(ins, store: true) is { } st) { if (producedInt && last != null) stored[st] = last; continue; }
                if (SlotKey(ins, store: false) is { } ld) { if (stored.TryGetValue(ld, out var val)) Push(val); continue; }
                if (ins.opcode == OpCodes.Ldftn && ins.operand is MethodInfo fn && IsCardFilter(fn))
                {
                    filter = fn;
                    continue;
                }
                if (ins.operand is not MethodBase mb || mb.DeclaringType == null) continue;
                if (ins.opcode != OpCodes.Call && ins.opcode != OpCodes.Callvirt && ins.opcode != OpCodes.Newobj) continue;
                var type = mb.DeclaringType;
                if (mb.Name == "InitializeArray" && type == typeof(RuntimeHelpers) && building is { } init && arrayData != null)
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
                if (type.Name == "DynamicVarSet" && mb.Name.StartsWith("get_")) varName = mb.Name == "get_Item" ? lastString : mb.Name[4..];
                else if (mb.Name == "get_IntValue" && type.Name.EndsWith("Var") && varName != null) Push(new IntExpr(null, varName));
                else if (IsRewardMaker(mb))
                {
                    // CardReward(options, count, player) / CardFactory.CreateForReward(player, count, options): the count is the
                    // last number pushed; it goes to the first reward still without one.
                    if (_rewards.FirstOrDefault(r => r.Count == null) is { } open)
                    {
                        open.Count = last;
                        if (mb.IsConstructor) open.Flags |= (int)CardCreationFlags.IsCardReward;
                    }
                }
                else if (type == typeof(CardCreationOptions) && mb.Name == nameof(CardCreationOptions.WithFlags) && _rewards.Count > 0 && last is { Const: { } flags })
                    _rewards[^1].Flags |= flags;
                if (mb.Name == "get_CardPool" && typeof(CharacterModel).IsAssignableFrom(type)) pools.Add(new PoolSource(mb, character));
                else if (type == typeof(ModelDb) && mb is MethodInfo { IsGenericMethod: true } g && g.Name == nameof(ModelDb.CardPool)) pools.Add(new PoolSource(mb, null));
                else if (mb is MethodInfo { IsGenericMethod: true } gc && (gc.Name is "Card" or "CreateCard" or "TransformTo")
                    && gc.GetGenericArguments()[0] is { } t && typeof(CardModel).IsAssignableFrom(t) && !t.IsAbstract)
                    _fixed.Add(t);
                else if (mb is MethodInfo { IsGenericMethod: true } ge && (ge.Name is "Enchantment" or "Enchant")
                    && ge.GetGenericArguments()[0] is { } e && typeof(EnchantmentModel).IsAssignableFrom(e) && !e.IsAbstract)
                    _enchantments.Add(e);
                else if (type == typeof(CardCreationOptions) && (mb.IsConstructor || (mb is MethodInfo f && f.IsStatic && f.ReturnType == typeof(CardCreationOptions))))
                {
                    var reward = new RewardSource
                    {
                        // No pool loaded here: it's passed in (Colorful Philosophers' option for one character's pool), so it's
                        // the CardPoolModel the option's delegate holds.
                        Pools = pools.Count == 0 ? new List<PoolSource> { FromOption } : pools.ToList(),
                        Maker = mb,
                        Filter = filter,
                        // The constructor takes (pools, source, odds, filter): its odds are the last number pushed, its source the one before.
                        Odds = mb.IsConstructor ? last?.Const : null,
                        Source = mb.IsConstructor ? before?.Const : null,
                    };
                    // A closure capturing one value of an enum we saw a constant array of: the loop's values (Glass Eye).
                    if (filter is { IsStatic: false } && filter.DeclaringType is { } closure && ThisField(closure) is var self
                        && closure.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(x => x != self).ToList() is { Count: 1 } captured
                        && arrays.TryGetValue(captured[0].FieldType, out var values))
                    {
                        reward.LoopField = captured[0];
                        reward.LoopValues = values;
                    }
                    _rewards.Add(reward);
                    pools.Clear();
                    filter = null;
                }
                else if ((type == typeof(MegaCrit.Sts2.Core.Commands.CardCmd) && (mb.Name == "TransformToRandom"
                        || (mb.Name == "Transform" && mb.GetParameters().Any(p => p.ParameterType.Name == "Rng"))))
                    || (type == typeof(CardFactory) && mb.Name == nameof(CardFactory.CreateRandomCardForTransform)))
                    _transformRandom = true;
                if (type == typeof(MegaCrit.Sts2.Core.Commands.CardCmd) && mb.Name.StartsWith("Upgrade")) upgrades = true;
                if (type.Name == "CardSelectCmd" && mb.Name == "FromDeckForUpgrade") picksUpgrade = true;
                if ((type == typeof(MegaCrit.Sts2.Core.Commands.CardCmd) && mb.Name.StartsWith("Enchant")) || (type.Name == "CardSelectCmd" && mb.Name == "FromDeckForEnchantment"))
                    _enchantsDeck = true;
                // Whose character a pool belongs to: the last call that returned one (Owner.Character, or Sea Glass's Character).
                if (mb is MethodInfo { ReturnType: var rt } && typeof(CharacterModel).IsAssignableFrom(rt)) character = mb;
            }
            _loosePools.AddRange(pools);
            // An upgrade with no pick from your deck in the same code is of the cards it makes (Astrolabe's new cards); with a
            // pick, it's of your cards.
            if (upgrades && !picksUpgrade && (_fixed.Count > 0 || _transformRandom || _rewards.Count > 0)) GivesUpgraded = true;
            else if (upgrades || picksUpgrade) _upgradesDeck = true;
        }

        /// <summary>For the tests: what was read (fixed cards, rewards, rewards with a filter, transform, rewards whose pool the option passes in, pools of a character other than yours).</summary>
        internal (int Fixed, int Rewards, int Filters, bool Transform, int PoolFromOption, int OtherCharacterPools) Summary =>
            (_fixed.Count, _rewards.Count, _rewards.Count(r => r.Filter != null), _transformRandom, _rewards.Count(r => r.Pools.Contains(FromOption)),
             _rewards.SelectMany(r => r.Pools).Count(p => p.Character != null && !typeof(Player).IsAssignableFrom(p.Character.DeclaringType)));

        /// <summary>For the tests: each reward's odds (null: the factory's own), count ("Cards", "Cards/3", "3", or "?") and loop values.</summary>
        internal List<(string Maker, string Count, string Loop)> RewardsRead =>
            _rewards.Select(r => (r.Maker.IsConstructor ? "new:" + r.Odds : r.Maker.Name,
                r.Count is { } c ? (c.Const?.ToString() ?? c.Var!) + (c.Divisor != 1 ? "/" + c.Divisor : "") : "?",
                r.LoopValues is { } v ? string.Join(",", v) : "")).ToList();

        /// <summary>For the tests: the enchantments the code applies, and how it upgrades.</summary>
        internal (string Enchantments, bool GivesUpgraded, bool UpgradesDeck) ChangesRead =>
            (string.Join(",", _enchantments.Select(e => e.Name).OrderBy(n => n)), GivesUpgraded, _upgradesDeck);

        private static bool IsRewardMaker(MethodBase mb) =>
            (mb.IsConstructor && mb.DeclaringType?.Name == "CardReward" && mb.GetParameters().Any(p => p.ParameterType == typeof(CardCreationOptions)))
            || (mb.DeclaringType == typeof(CardFactory) && mb.Name == nameof(CardFactory.CreateForReward) && mb.GetParameters().Length == 3);

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
                var idx = ins.operand switch { LocalBuilder lb => lb.LocalIndex, int i => i, byte b => b, _ => LocalIndex(ins) };
                return idx < 0 ? null : "loc:" + idx;
            }
            if (ins.opcode == (store ? OpCodes.Stfld : OpCodes.Ldfld) && ins.operand is FieldInfo f && f.FieldType == typeof(int)) return "fld:" + f.Name;
            return null;
        }

        private static int LocalIndex(CodeInstruction ins)
        {
            var name = ins.opcode.Name ?? "";
            var dot = name.LastIndexOf('.');
            return dot >= 0 && int.TryParse(name[(dot + 1)..], out var i) ? i : -1;
        }

        private static bool IsCardFilter(MethodInfo m) =>
            m.ReturnType == typeof(bool) && m.GetParameters() is { Length: 1 } p && typeof(CardModel).IsAssignableFrom(p[0].ParameterType);

        /// <summary>The cards (canonical) this code can give, or null when a reward's pool couldn't be read.</summary>
        public List<CardModel>? Candidates(Player me)
        {
            var set = new HashSet<CardModel>();
            foreach (var t in _fixed)
            {
                var card = ModelDb.AllCards.FirstOrDefault(c => c.GetType() == t);
                if (card != null) set.Add(card);
            }
            foreach (var reward in _rewards)
            {
                if (Built(reward, me) is not { } built) return null;
                foreach (var (_, possible) in built)
                foreach (var c in possible) set.Add(c);
            }
            foreach (var p in _loosePools)
            foreach (var c in Unlocked(Pool(p, me), me)) set.Add(c);
            if (_transformRandom)
            {
                foreach (var original in me.Deck.Cards)
                {
                    IEnumerable<CardModel> options;
                    try { options = CardFactory.GetDefaultTransformationOptions(original, false); }
                    catch { continue; }
                    foreach (var c in options) set.Add(c.IsCanonical ? c : c.CanonicalInstance);
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
            if (reward.Pools.Any(p => p.Method.Name == "get_CardPool" && CharacterOf(p.Character, me) == null)) return null;
            var pools = reward.Pools.Select(p => Pool(p, me)).OfType<CardPoolModel>().Distinct().ToList();
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
            foreach (var t in _fixed)
            {
                if (ModelDb.AllCards.FirstOrDefault(c => c.GetType() == t) is { } card && applies(card)) return (1, 1);
            }
            foreach (var p in _loosePools)
            {
                if (Unlocked(Pool(p, me), me).Any(applies)) return null;
            }
            var chances = new List<double>();
            foreach (var reward in _rewards)
            {
                if (Built(reward, me) is not { } built) return null;
                foreach (var (options, possible) in built)
                {
                    if (!possible.Any(applies)) continue;
                    if (reward.Count is not { } countExpr || Evaluate(countExpr) is not { } count) return null;
                    if (RewardChance(possible, applies, options.RarityOdds, count) is not { } p) return null;
                    chances.Add(p);
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
                try { options = CardFactory.GetDefaultTransformationOptions(original, false).Select(c => c.IsCanonical ? c : c.CanonicalInstance).ToList(); }
                catch { continue; }
                if (options.Count == 0) continue;
                var colorless = original.Type == CardType.Quest || original.Rarity is CardRarity.Event or CardRarity.Ancient or CardRarity.Token;
                var anyRarity = original.Rarity is CardRarity.Status or CardRarity.Curse;
                var key = (colorless ? "colorless" : original.Pool?.Id.ToString() ?? "?") + (anyRarity ? "/all" : "");
                if (!byPool.TryGetValue(key, out var seen) || options.Count > seen.Count)
                    byPool[key] = (options.Count, (double)options.Count(applies) / options.Count);
            }
            if (byPool.Count == 0) return null;
            return (byPool.Values.Min(v => v.Chance), byPool.Values.Max(v => v.Chance));
        }

        /// <summary>
        /// Whether the change this option makes to your deck reaches a card the rule applies to: Mystic Lighter when one of its
        /// enchantments can go on an Attack in your deck (any Attack when the enchantment isn't named), Miniature Cannon when an
        /// Attack in your deck can be upgraded. A change it makes to the cards it gives is counted with those cards instead.
        /// </summary>
        public bool ChangeReaches(Player me, CardAffinityRule rule)
        {
            var deck = me.Deck.Cards.ToList();
            bool Base(CardModel c) => rule.Base == null || rule.Base(c);
            return rule.Change switch
            {
                CardChange.Upgrade => _upgradesDeck && deck.Any(c => Base(c) && c.IsUpgradable),
                CardChange.Enchant => _enchantsDeck && deck.Any(c => Base(c) && (_enchantments.Count == 0
                    || _enchantments.Any(e => EnchantmentOf(e)?.CanEnchant(c) ?? true))),
                _ => false,
            };
        }

        private static EnchantmentModel? EnchantmentOf(Type t)
        {
            try { return typeof(ModelDb).GetMethod(nameof(ModelDb.Enchantment))!.MakeGenericMethod(t).Invoke(null, null) as EnchantmentModel; }
            catch { return null; }
        }

        /// <summary>One reward: the game's draw over the possible cards (CreateForReward), as CardOdds counts it.</summary>
        private static double? RewardChance(List<CardModel> possible, Func<CardModel, bool> applies, CardRarityOddsType odds, int count)
        {
            if (odds == CardRarityOddsType.Uniform)
            {
                // Uniform: every card but Basic and Ancient ones, equally likely.
                var pool = possible.Where(c => c.Rarity != CardRarity.Basic && c.Rarity != CardRarity.Ancient).ToList();
                return SpireQoL.Sim.CardOdds.AtLeastOneUniform(pool.Count, pool.Count(applies), count);
            }
            if (BaseOdds(odds) is not { } baseOdds) return null;
            var groups = new[] { CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare }
                .Select(r => possible.Where(c => c.Rarity == r).ToList())
                .Select(g => new SpireQoL.Sim.RarityGroup(g.Count, g.Count(applies))).ToList();
            return SpireQoL.Sim.CardOdds.AtLeastOne(groups, baseOdds.Rare, baseOdds.Uncommon, count);
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
        private int? Evaluate(IntExpr e)
        {
            int? value = e.Const;
            if (e.Var != null)
            {
                foreach (var holder in Holders())
                {
                    // Events, relics, cards: each model type has its own DynamicVars.
                    if (holder.GetType().GetProperty("DynamicVars")?.GetValue(holder) is not MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVarSet vars) continue;
                    try
                    {
                        value = (int)vars[e.Var].IntValue;
                        break;
                    }
                    catch { /* not this one */ }
                }
            }
            return value is { } v && e.Divisor != 0 ? v / e.Divisor : null;
        }

        /// <summary>Stands for "the pool the option passes in" in a reward's pools.</summary>
        private static readonly PoolSource FromOption = new(AccessTools.Method(typeof(OptionCards), nameof(Candidates)), null);

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

        private static FieldInfo? ThisField(Type t) =>
            t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(f => f.Name.EndsWith("__this"));

        /// <summary>The card pool the option's delegate holds (a closure field), if any.</summary>
        private CardPoolModel? OptionPool()
        {
            if (Target == null) return null;
            return Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => typeof(CardPoolModel).IsAssignableFrom(f.FieldType)).Select(f => f.GetValue(Target) as CardPoolModel)
                .FirstOrDefault(p => p != null);
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
        private Func<CardModel, bool> Filter(MethodInfo? m, FieldInfo? loopField = null, object? loopValue = null)
        {
            if (m == null) return _ => true;
            object? inst = null;
            if (!m.IsStatic)
            {
                var dt = m.DeclaringType!;
                inst = Holders().FirstOrDefault(dt.IsInstanceOfType);
                if (inst == null && dt.GetField("<>9", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is { } nine) inst = nine.GetValue(null);
                if (inst == null)
                {
                    var fields = dt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var self = ThisField(dt);
                    if (fields.Any(f => f != self && f != loopField)) return _ => true;
                    inst = RuntimeHelpers.GetUninitializedObject(dt);
                    var owner = Holders().FirstOrDefault(h => self != null && self.FieldType.IsInstanceOfType(h));
                    if (self != null && owner != null) self.SetValue(inst, owner);
                    if (loopField != null && loopValue != null) loopField.SetValue(inst, loopValue);
                }
            }
            return c =>
            {
                try { return m.Invoke(inst, new object[] { c }) is true; }
                catch { return true; }
            };
        }
    }
}
