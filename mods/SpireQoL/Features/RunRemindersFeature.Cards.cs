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
        /// <summary>The event (or relic) running the code: the filter lambdas' <c>this</c>.</summary>
        public object? Target;
        private readonly HashSet<Type> _fixed = new();
        private readonly List<(List<MethodBase> Pools, MethodInfo? Filter)> _rewards = new();
        private readonly List<MethodBase> _loosePools = new();
        private bool _transformRandom;

        /// <summary>One method body: pools and a filter are paired with the CardCreationOptions call that follows them.</summary>
        public void Read(List<CodeInstruction> code)
        {
            var pools = new List<MethodBase>();
            MethodInfo? filter = null;
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
                if (mb.Name == "get_CardPool" && typeof(CharacterModel).IsAssignableFrom(type)) pools.Add(mb);
                else if (type == typeof(ModelDb) && mb is MethodInfo { IsGenericMethod: true } g && g.Name == nameof(ModelDb.CardPool)) pools.Add(mb);
                else if (mb is MethodInfo { IsGenericMethod: true } gc && (gc.Name is "Card" or "CreateCard" or "TransformTo")
                    && gc.GetGenericArguments()[0] is { } t && typeof(CardModel).IsAssignableFrom(t) && !t.IsAbstract)
                    _fixed.Add(t);
                else if (type == typeof(CardCreationOptions) && (mb.IsConstructor || (mb is MethodInfo f && f.IsStatic && f.ReturnType == typeof(CardCreationOptions))))
                {
                    // No pool loaded here: it's passed in (Colorful Philosophers' option for one character's pool), so it's
                    // the CardPoolModel the option's delegate holds.
                    _rewards.Add((pools.Count == 0 ? new List<MethodBase> { FromOption } : pools.ToList(), filter));
                    pools.Clear();
                    filter = null;
                }
                else if (type == typeof(MegaCrit.Sts2.Core.Commands.CardCmd) && (mb.Name == "TransformToRandom"
                    || (mb.Name == "Transform" && mb.GetParameters().Any(p => p.ParameterType.Name == "Rng"))))
                    _transformRandom = true;
            }
            _loosePools.AddRange(pools);
        }

        /// <summary>For the tests: what was read (fixed cards, rewards, rewards with a filter, transform, rewards whose pool the option passes in).</summary>
        internal (int Fixed, int Rewards, int Filters, bool Transform, int PoolFromOption) Summary =>
            (_fixed.Count, _rewards.Count, _rewards.Count(r => r.Filter != null), _transformRandom, _rewards.Count(r => r.Pools.Contains(FromOption)));

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
        private static readonly MethodBase FromOption = AccessTools.Method(typeof(OptionCards), nameof(Candidates));

        private CardPoolModel? Pool(MethodBase source, Player me)
        {
            if (source == FromOption) return OptionPool();
            if (source.Name == "get_CardPool") return me.Character.CardPool;
            try { return ((MethodInfo)source).Invoke(null, null) as CardPoolModel; }
            catch { return null; }
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
                // The delegate may be a closure (Colorful Philosophers' per-pool option): the event is its <>4__this.
                var owner = Target != null && ThisField(Target.GetType()) is { } outer && outer.GetValue(Target) is { } ev ? ev : Target;
                if (Target != null && dt.IsInstanceOfType(Target)) inst = Target;
                else if (owner != null && dt.IsInstanceOfType(owner)) inst = owner;
                else if (dt.GetField("<>9", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is { } nine) inst = nine.GetValue(null);
                else
                {
                    inst = RuntimeHelpers.GetUninitializedObject(dt);
                    if (ThisField(dt) is { } self && owner != null && self.FieldType.IsInstanceOfType(owner)) self.SetValue(inst, owner);
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
