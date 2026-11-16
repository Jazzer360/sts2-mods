using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SpireQoL.Data;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace SpireQoL.Features;

/// <summary>
/// Which cards an event option can give you (docs/CARD_ODDS.md), read from the same code walk as its effects, through the game
/// calls that make cards, so no event needs code of its own:
///  - a card reward or a random card from <c>CardCreationOptions</c>: its pools (your character's, or <c>ModelDb.CardPool&lt;T&gt;</c>)
///    and its filter (the lambda passed with them, run on each card of the pool), as <c>GetPossibleCards</c> and the factory's
///    <c>FilterForRun</c> would;
///  - a fixed card: <c>ModelDb.Card&lt;T&gt;</c>, <c>CreateCard&lt;T&gt;</c>, <c>CardCmd.TransformTo&lt;T&gt;</c>;
///  - a transform to a random card: <c>CardFactory.GetDefaultTransformationOptions</c> of each card in your deck;
///  - a pool used without the factory (the Museum's curse pool): every card in it.
/// A reward whose pool isn't loaded in the code takes the pool the option passes in (a closure field); when there is none,
/// the option's cards are unknown (null) and its relics keep their icon.
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
        return cards == null || cards.Any(c => CardAffinity.AppliesTo(rule, c));
    }

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
        private readonly HashSet<Type> _fixed = new();
        private readonly List<(List<PoolSource> Pools, MethodInfo? Filter)> _rewards = new();
        private readonly List<PoolSource> _loosePools = new();
        private bool _transformRandom;

        /// <summary>One method body: pools and a filter are paired with the CardCreationOptions call that follows them.</summary>
        public void Read(List<CodeInstruction> code)
        {
            var pools = new List<PoolSource>();
            MethodInfo? filter = null;
            MethodBase? character = null;
            foreach (var ins in code)
            {
                if (ins.opcode == OpCodes.Ldftn && ins.operand is MethodInfo fn && IsCardFilter(fn))
                {
                    filter = fn;
                    continue;
                }
                if (ins.operand is not MethodBase mb || mb.DeclaringType == null) continue;
                if (ins.opcode != OpCodes.Call && ins.opcode != OpCodes.Callvirt && ins.opcode != OpCodes.Newobj) continue;
                var type = mb.DeclaringType;
                if (mb.Name == "get_CardPool" && typeof(CharacterModel).IsAssignableFrom(type)) pools.Add(new PoolSource(mb, character));
                else if (type == typeof(ModelDb) && mb is MethodInfo { IsGenericMethod: true } g && g.Name == nameof(ModelDb.CardPool)) pools.Add(new PoolSource(mb, null));
                else if (mb is MethodInfo { IsGenericMethod: true } gc && (gc.Name is "Card" or "CreateCard" or "TransformTo")
                    && gc.GetGenericArguments()[0] is { } t && typeof(CardModel).IsAssignableFrom(t) && !t.IsAbstract)
                    _fixed.Add(t);
                else if (type == typeof(CardCreationOptions) && (mb.IsConstructor || (mb is MethodInfo f && f.IsStatic && f.ReturnType == typeof(CardCreationOptions))))
                {
                    // No pool loaded here: it's passed in (Colorful Philosophers' option for one character's pool), so it's
                    // the CardPoolModel the option's delegate holds.
                    _rewards.Add((pools.Count == 0 ? new List<PoolSource> { FromOption } : pools.ToList(), filter));
                    pools.Clear();
                    filter = null;
                }
                else if ((type == typeof(MegaCrit.Sts2.Core.Commands.CardCmd) && (mb.Name == "TransformToRandom"
                        || (mb.Name == "Transform" && mb.GetParameters().Any(p => p.ParameterType.Name == "Rng"))))
                    || (type == typeof(CardFactory) && mb.Name == nameof(CardFactory.CreateRandomCardForTransform)))
                    _transformRandom = true;
                // Whose character a pool belongs to: the last call that returned one (Owner.Character, or Sea Glass's Character).
                if (mb is MethodInfo { ReturnType: var rt } && typeof(CharacterModel).IsAssignableFrom(rt)) character = mb;
            }
            _loosePools.AddRange(pools);
        }

        /// <summary>For the tests: what was read (fixed cards, rewards, rewards with a filter, transform, rewards whose pool the option passes in, pools of a character other than yours).</summary>
        internal (int Fixed, int Rewards, int Filters, bool Transform, int PoolFromOption, int OtherCharacterPools) Summary =>
            (_fixed.Count, _rewards.Count, _rewards.Count(r => r.Filter != null), _transformRandom, _rewards.Count(r => r.Pools.Contains(FromOption)),
             _rewards.SelectMany(r => r.Pools).Count(p => p.Character != null && !typeof(Player).IsAssignableFrom(p.Character.DeclaringType)));

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
            foreach (var (pools, filter) in _rewards)
            {
                if (pools.Contains(FromOption) && OptionPool() == null) return null;
                // A pool whose character can't be told: the cards are unknown, so the icons stay.
                if (pools.Any(p => p.Method.Name == "get_CardPool" && CharacterOf(p.Character, me) == null)) return null;
                var keep = Filter(filter);
                var possible = pools.SelectMany(p => Unlocked(Pool(p, me), me)).Where(keep);
                foreach (var c in ForRun(me, possible)) set.Add(c);
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
        /// event itself, or a closure over the event (<c>&lt;&gt;4__this</c>). A filter that throws keeps the card.
        /// </summary>
        private Func<CardModel, bool> Filter(MethodInfo? m)
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
                    // A closure: only its "this" can be filled in. One that also captures a value (Glass Eye's rarity, set per
                    // reward in a loop) can't be evaluated, so it keeps every card of the pool.
                    var fields = dt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var self = ThisField(dt);
                    if (fields.Any(f => f != self)) return _ => true;
                    inst = RuntimeHelpers.GetUninitializedObject(dt);
                    var owner = Holders().FirstOrDefault(h => self != null && self.FieldType.IsInstanceOfType(h));
                    if (self != null && owner != null) self.SetValue(inst, owner);
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
