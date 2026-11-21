using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;

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
/// <param name="Applies">Whether it applies to a card as you'd get it (a canonical card: not upgraded, no enchantment).</param>
/// <param name="What">What it does to those cards, for the docs and the settings menu.</param>
/// <param name="Change">A modifier: the change it makes to the cards it applies to. A boost: the change it looks for, so it also
/// applies to a card a held modifier would change that way (an egg's upgraded Attack for Miniature Cannon), and to options
/// that make that change to your cards (an upgrade, an enchantment).</param>
/// <param name="Base">A boost: what the card must be besides the change (Miniature Cannon: an Attack).</param>
/// <param name="Shows">A modifier: whether a card shows its change (the check: an egg's card is upgraded, Fresnel Lens' has Nimble).</param>
public sealed record CardAffinityRule(string Relic, AffinityKind Kind, Func<CardModel, bool> Applies, string What,
    CardChange Change = CardChange.None, Func<CardModel, bool>? Base = null, Func<CardModel, bool>? Shows = null);

/// <summary>
/// Relics that care about certain cards, each with the test its own hook makes (decompile v0.113.1), written from the hook
/// bodies and using the game's own check where there is one. Modifiers run first: a boost sees the card after them.
/// Every relic whose card-creation or damage/block hooks test a card has a rule here or a reviewed reason in
/// <see cref="Reviewed"/>; the coverage check reports the rest.
/// </summary>
public static class CardAffinity
{
    public static readonly IReadOnlyList<CardAffinityRule> Rules = new CardAffinityRule[]
    {
        // EggRelicHelper.UpgradeValidCards / TryModifyCardBeingAddedToDeck: the type, upgradable, not upgraded yet.
        new(nameof(MoltenEgg), AffinityKind.Modifies, card => Upgrades(card, CardType.Attack), "upgrades Attacks you get", CardChange.Upgrade, Shows: card => card.IsUpgraded),
        new(nameof(FrozenEgg), AffinityKind.Modifies, card => Upgrades(card, CardType.Power), "upgrades Powers you get", CardChange.Upgrade, Shows: card => card.IsUpgraded),
        new(nameof(ToxicEgg), AffinityKind.Modifies, card => Upgrades(card, CardType.Skill), "upgrades Skills you get", CardChange.Upgrade, Shows: card => card.IsUpgraded),
        // EnchantValidCards / TryModifyCardBeingAddedToDeck: Nimble.CanEnchant (a card that gains block, not a status or curse).
        new(nameof(FresnelLens), AffinityKind.Modifies, card => ModelDb.Enchantment<Nimble>().CanEnchant(card), "enchants cards that gain block with Nimble", CardChange.Enchant, Shows: card => card.Enchantment is Nimble),
        // ModifyDamageAdditive on the card's powered attacks (IsPoweredAttack: an Attack's damage; read here as the card being an Attack).
        new(nameof(StrikeDummy), AffinityKind.Boosts, card => card.Tags.Contains(CardTag.Strike), "Strikes deal more damage"),
        new(nameof(FakeStrikeDummy), AffinityKind.Boosts, card => card.Tags.Contains(CardTag.Strike), "Strikes deal more damage"),
        new(nameof(MiniatureCannon), AffinityKind.Boosts, card => IsAttack(card) && card.IsUpgraded, "upgraded Attacks deal more damage", CardChange.Upgrade, IsAttack),
        new(nameof(MysticLighter), AffinityKind.Boosts, card => IsAttack(card) && card.Enchantment != null, "enchanted Attacks deal more damage", CardChange.Enchant, IsAttack),
        // ModifyDamageMultiplicative / ModifyBlockMultiplicative: double for Minion cards.
        new(nameof(VitruvianMinion), AffinityKind.Boosts, card => card.Tags.Contains(CardTag.Minion), "Minion cards deal double damage and give double block"),
    };

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

    private static readonly Dictionary<string, CardAffinityRule> ByRelic = Rules.ToDictionary(cardAffinityRule => cardAffinityRule.Relic);

    public static CardAffinityRule? For(string relic) => ByRelic.GetValueOrDefault(relic);

    private static bool Upgrades(CardModel card, CardType type) => card.Type == type && card.IsUpgradable && card.CurrentUpgradeLevel < 1;

    private static bool IsAttack(CardModel card) => card.Type == CardType.Attack;

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

    private static bool Safe(Func<CardModel, bool> f, CardModel card)
    {
        try { return f(card); }
        catch { return true; }
    }

    /// <summary>Whether a rule applies to a card, safely (a check that throws on an odd card counts as applying: the icon stays).</summary>
    public static bool AppliesTo(CardAffinityRule rule, CardModel card)
    {
        try { return rule.Applies(card); }
        catch { return true; }
    }
}
