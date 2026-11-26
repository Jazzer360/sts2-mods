using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The card scan (docs/CARD_ODDS.md): every relic in the installed game whose card hooks test a card (its tags, type, upgrade,
/// enchantment...) has a card rule (Data/CardAffinity) or a reviewed reason, and every rule names a real relic. Read from the
/// relics' compiled code, so relics new in a game update fail here before anyone holds one.
/// </summary>
[Collection(GameFightCollection.Name)]
public class CardAffinityTests
{
    /// <summary>The relics' rules are read from their code with the game's cards made (Data/RelicReadings).</summary>
    public CardAffinityTests() => GameModels.EnsureLoaded();

    private static List<Type> Relics() => typeof(RelicModel).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && typeof(RelicModel).IsAssignableFrom(type)).ToList();

    [Fact]
    public void EveryRelicThatTestsACardHasARuleOrAReason()
    {
        var missing = Relics()
            .Where(type => CardAffinity.For(type.Name) == null && !CardAffinity.Reviewed.ContainsKey(type.Name))
            .Select(type => (type.Name, Hooks: SpireQoL.Diagnostics.CoverageWatch.CardTestingHooks(type)))
            .Where(nameHooks => nameHooks.Hooks.Count > 0)
            .Select(nameHooks => $"{nameHooks.Name}: {string.Join(", ", nameHooks.Hooks)}").ToList();
        Assert.True(missing.Count == 0, "Relics that test a card with no CardAffinity rule or reason:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void TheScanFindsTheKnownRelics()
    {
        // An empty scan would pass silently: the relics the rules came from must be found by it.
        var byName = Relics().ToDictionary(type => type.Name);
        foreach (var name in new[] { "FresnelLens", "MoltenEgg", "StrikeDummy", "MiniatureCannon", "MysticLighter", "VitruvianMinion", "Glitter" })
            Assert.NotEmpty(SpireQoL.Diagnostics.CoverageWatch.CardTestingHooks(byName[name]));
    }

    [Fact]
    public void BegoneMakesAMinionStrike()
    {
        // Strike Dummy and Vitruvian Minion count the cards a card makes: Begone! makes a Minion Strike (a Strike and a Minion).
        var begone = typeof(CardModel).Assembly.GetTypes().Single(type => type.Name == "Begone" && typeof(CardModel).IsAssignableFrom(type));
        Assert.Contains(CardAffinity.MadeTypes(begone), type => type.Name == "MinionStrike");
    }

    [Fact]
    public void EveryRuleNamesARelic()
    {
        var names = Relics().Select(type => type.Name).ToHashSet();
        var unknown = CardAffinity.Rules.Select(cardAffinityRule => cardAffinityRule.Relic).Concat(CardAffinity.Reviewed.Keys).Where(name => !names.Contains(name)).ToList();
        Assert.Empty(unknown);
    }
}
