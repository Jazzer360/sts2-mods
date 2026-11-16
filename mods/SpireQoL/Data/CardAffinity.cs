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

/// <param name="Relic">The relic's class name.</param>
/// <param name="Applies">Whether it applies to a card as you'd get it (a canonical card: not upgraded, no enchantment).</param>
/// <param name="What">What it does to those cards, for the docs and the settings menu.</param>
public sealed record CardAffinityRule(string Relic, AffinityKind Kind, Func<CardModel, bool> Applies, string What);

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
        new(nameof(MoltenEgg), AffinityKind.Modifies, c => Upgrades(c, CardType.Attack), "upgrades Attacks you get"),
        new(nameof(FrozenEgg), AffinityKind.Modifies, c => Upgrades(c, CardType.Power), "upgrades Powers you get"),
        new(nameof(ToxicEgg), AffinityKind.Modifies, c => Upgrades(c, CardType.Skill), "upgrades Skills you get"),
        // EnchantValidCards / TryModifyCardBeingAddedToDeck: Nimble.CanEnchant (a card that gains block, not a status or curse).
        new(nameof(FresnelLens), AffinityKind.Modifies, c => ModelDb.Enchantment<Nimble>().CanEnchant(c), "enchants cards that gain block with Nimble"),
        // ModifyDamageAdditive on the card's powered attacks.
        new(nameof(StrikeDummy), AffinityKind.Boosts, c => c.Tags.Contains(CardTag.Strike), "Strikes deal more damage"),
        new(nameof(FakeStrikeDummy), AffinityKind.Boosts, c => c.Tags.Contains(CardTag.Strike), "Strikes deal more damage"),
        new(nameof(MiniatureCannon), AffinityKind.Boosts, c => c.IsUpgraded, "upgraded cards deal more damage"),
        new(nameof(MysticLighter), AffinityKind.Boosts, c => c.Enchantment != null, "enchanted cards deal more damage"),
        // ModifyDamageMultiplicative / ModifyBlockMultiplicative: double for Minion cards.
        new(nameof(VitruvianMinion), AffinityKind.Boosts, c => c.Tags.Contains(CardTag.Minion), "Minion cards deal double damage and give double block"),
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

    private static readonly Dictionary<string, CardAffinityRule> ByRelic = Rules.ToDictionary(r => r.Relic);

    public static CardAffinityRule? For(string relic) => ByRelic.GetValueOrDefault(relic);

    private static bool Upgrades(CardModel c, CardType type) => c.Type == type && c.IsUpgradable && c.CurrentUpgradeLevel < 1;

    /// <summary>Whether a rule applies to a card, safely (a check that throws on an odd card counts as applying: the icon stays).</summary>
    public static bool AppliesTo(CardAffinityRule rule, CardModel card)
    {
        try { return rule.Applies(card); }
        catch { return true; }
    }
}
