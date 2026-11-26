using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace SpireQoL.Data;

/// <summary>How a relic relates to certain cards (docs/CARD_ODDS.md).</summary>
public enum AffinityKind
{
    /// <summary>It changes the card you get (upgrades or enchants it as it joins your deck or a reward).</summary>
    Modifies,
    /// <summary>It makes the card stronger in fights, in a way the card's own text doesn't show outside them.</summary>
    Boosts,
}

/// <summary>A change to a card that a relic gives (a modifier) or looks for (a boost).</summary>
public enum CardChange
{
    None,
    /// <summary>Upgraded: the eggs give it, Miniature Cannon looks for it.</summary>
    Upgrade,
    /// <summary>Enchanted: Fresnel Lens gives it, Mystic Lighter looks for it.</summary>
    Enchant,
}

/// <param name="Relic">The relic's class name.</param>
/// <param name="Applies">Whether it applies to a card (its hook's tests on the card: CardReading.Holds; one it can't tell applies).</param>
/// <param name="Change">A modifier: the change it makes to the cards it applies to. A boost: the change it looks for, so it also
/// applies to a card a held modifier would change that way (an egg's upgraded Attack for Miniature Cannon), and to options
/// that make that change to your cards (an upgrade, an enchantment).</param>
/// <param name="Base">A boost: what the card must be besides the change (Miniature Cannon: an Attack).</param>
/// <param name="Shows">A modifier: whether a card shows its change (the check: an egg's card is upgraded, Fresnel Lens' has Nimble).</param>
public sealed record CardAffinityRule(string Relic, AffinityKind Kind, Func<CardModel, bool> Applies,
    CardChange Change = CardChange.None, Func<CardModel, bool>? Base = null, Func<CardModel, bool>? Shows = null);

/// <summary>
/// Relics that care about certain cards, each with the test its own hook makes, read from the hook's code (Data/RelicReadings, decision #359):
/// a card being added to your deck (TryModifyCardBeingAddedToDeck: the eggs' type and upgradable test, Fresnel Lens' Nimble.CanEnchant), or the
/// damage and block a card of yours deals in fights (Strike Dummy's Strike tag, Miniature Cannon's upgrade, Mystic Lighter's enchantment, Vitruvian
/// Minion's Minion tag). Modifiers run first: a boost sees the card after them. Every relic whose card-creation or damage/block hooks test a card has
/// a reading with a card rule or a reviewed reason in <see cref="Reviewed"/>; the coverage check reports the rest.
/// </summary>
public static class CardAffinity
{
    /// <summary>
    /// Relics whose hooks test a card but get no rule: "none: why". Reacting to plays is a fight's business (Proc Indicators),
    /// not a choice of cards (owner, 2026-11-16: no synergy hints for now).
    /// </summary>
    public static readonly Dictionary<string, string> Reviewed = new()
    {
        // TryModifyCardRewardOptionsLate only: they change the cards a reward offers, and the reward shows them changed.
        ["Glitter"] = "none: enchants offered reward cards (shown on the offered cards)",
        ["LavaLamp"] = "none: upgrades offered reward cards after a fight without damage (shown on the offered cards)",
        ["SilkenTress"] = "none: enchants offered reward cards (shown on the offered cards)",
        ["SilverCrucible"] = "none: upgrades offered reward cards (shown on the offered cards)",
        ["WingCharm"] = "none: enchants an offered reward card (shown on the offered card)",
    };

    private static readonly ConcurrentDictionary<string, CardAffinityRule?> ByRelic = new();

    /// <summary>A relic's card rule, from its reading; null when it has none.</summary>
    public static CardAffinityRule? For(string relic)
    {
        if (ByRelic.TryGetValue(relic, out var known)) return known;
        var rule = RelicReadings.For(relic)?.Card is { } card ? RuleOf(relic, card) : null;
        if (RelicReadings.Kept(relic)) ByRelic.TryAdd(relic, rule);
        return rule;
    }

    /// <summary>The rule a card reading makes: its tests on a card, a boost's tests besides its change, the change a modifier's card shows.</summary>
    public static CardAffinityRule RuleOf(string relic, CardReading card) => new(relic, card.Kind, candidate => card.Holds(candidate) ?? true, card.Change,
        Base: card.Kind == AffinityKind.Boosts && card.Change != CardChange.None ? candidate => card.HoldsBesidesChange(candidate) ?? true : null,
        Shows: card.Kind != AffinityKind.Modifies ? null : card.Change switch
        {
            CardChange.Upgrade => candidate => candidate.IsUpgraded,
            CardChange.Enchant => candidate => candidate.Enchantment?.GetType().Name == card.Enchantment,
            _ => null,
        });

    /// <summary>Every relic of the loaded game that has a card rule.</summary>
    public static IReadOnlyList<CardAffinityRule> Rules => RelicReadings.LoadedRelics.Order(StringComparer.Ordinal).Select(For).OfType<CardAffinityRule>().ToList();

    /// <summary>
    /// Whether a rule applies to a card as you'd get it with your relics: the card itself, or (a boost looking for a change)
    /// the card after a held modifier makes that change (Molten Egg upgrades an Attack, so Miniature Cannon boosts it).
    /// <paramref name="upgraded"/>: the option upgrades the card it gives (Astrolabe), so an egg has nothing to do and
    /// Miniature Cannon applies to an upgradable Attack.
    /// </summary>
    public static bool AppliesWith(CardAffinityRule rule, CardModel card, IEnumerable<CardAffinityRule> held, bool upgraded = false)
    {
        if (upgraded && rule.Change == CardChange.Upgrade)
        {
            if (rule.Kind == AffinityKind.Modifies) return false;
            if ((rule.Base == null || Safe(rule.Base, card)) && card.IsUpgradable) return true;
        }
        if (AppliesTo(rule, card)) return true;
        // A boost also counts the cards this card makes when played (Begone!'s Minion Strike: a Strike and a Minion).
        if (rule.Kind == AffinityKind.Boosts && Made(card).Any(otherCard => AppliesTo(rule, otherCard))) return true;
        if (rule.Kind != AffinityKind.Boosts || rule.Change == CardChange.None) return false;
        if (rule.Base != null && !Safe(rule.Base, card)) return false;
        return held.Any(cardAffinityRule => cardAffinityRule.Kind == AffinityKind.Modifies && cardAffinityRule.Change == rule.Change && AppliesTo(cardAffinityRule, card));
    }

    /// <summary>The option effect that makes a boost's change to your cards (an upgrade for Miniature Cannon).</summary>
    public static ChoiceEffect ChangeEffect(CardAffinityRule rule) => rule.Kind != AffinityKind.Boosts ? ChoiceEffect.None : rule.Change switch
    {
        CardChange.Upgrade => ChoiceEffect.UpgradeCard,
        CardChange.Enchant => ChoiceEffect.EnchantCard,
        _ => ChoiceEffect.None,
    };

    private static readonly ConcurrentDictionary<Type, List<CardModel>> MadeCache = new();

    /// <summary>
    /// The cards a card makes (canonical), read from its own code: the card types it names in <c>CreateCard&lt;T&gt;</c> or
    /// <c>ModelDb.Card&lt;T&gt;</c>, in its methods and their lambdas and state machines. Cached per card type.
    /// </summary>
    public static List<CardModel> Made(CardModel card) => MadeCache.GetOrAdd(card.GetType(), type =>
    {
        var list = new List<CardModel>();
        foreach (var made in MadeTypes(type))
        {
            try { if (ModelDb.AllCards.FirstOrDefault(canonicalCard => canonicalCard.GetType() == made) is { } canonical) list.Add(canonical); }
            catch { /* not in the database */ }
        }
        return list;
    });

    /// <summary>The card types a card class makes (see <see cref="Made"/>).</summary>
    internal static HashSet<Type> MadeTypes(Type otherType)
    {
        var types = new HashSet<Type>();
        foreach (var type in new[] { otherType }.Concat(otherType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.IsAbstract) continue;
            List<CodeInstruction> code;
            try { code = SpireQoL.Diagnostics.GameCode.Instructions(method); } catch { continue; }
            foreach (var ins in code)
            {
                if (ins.operand is MethodInfo { IsGenericMethod: true } otherMethod && otherMethod.Name is "CreateCard" or "Card"
                    && otherMethod.GetGenericArguments()[0] is { } made && typeof(CardModel).IsAssignableFrom(made) && !made.IsAbstract && made != otherType)
                    types.Add(made);
            }
        }
        return types;
    }

    private static bool Safe(Func<CardModel, bool> test, CardModel card)
    {
        try { return test(card); }
        catch { return true; }
    }

    /// <summary>Whether a rule applies to a card, safely (a check that throws on an odd card counts as applying: the icon stays).</summary>
    public static bool AppliesTo(CardAffinityRule rule, CardModel card)
    {
        try { return rule.Applies(card); }
        catch { return true; }
    }
}
