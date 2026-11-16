using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The card scan (docs/CARD_ODDS.md): every relic in the installed game whose card hooks test a card (its tags, type, upgrade,
/// enchantment...) has a card rule (Data/CardAffinity) or a reviewed reason, and every rule names a real relic. Read from the
/// relics' compiled code, so relics new in a game update fail here before anyone holds one.
/// </summary>
public class CardAffinityTests
{
    private static List<Type> Relics() => typeof(RelicModel).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(RelicModel).IsAssignableFrom(t)).ToList();

    [Fact]
    public void EveryRelicThatTestsACardHasARuleOrAReason()
    {
        var missing = Relics()
            .Where(t => CardAffinity.For(t.Name) == null && !CardAffinity.Reviewed.ContainsKey(t.Name))
            .Select(t => (t.Name, Hooks: SpireQoL.Diagnostics.CoverageWatch.CardTestingHooks(t)))
            .Where(x => x.Hooks.Count > 0)
            .Select(x => $"{x.Name}: {string.Join(", ", x.Hooks)}").ToList();
        Assert.True(missing.Count == 0, "Relics that test a card with no CardAffinity rule or reason:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void TheScanFindsTheKnownRelics()
    {
        // An empty scan would pass silently: the relics the rules came from must be found by it.
        var byName = Relics().ToDictionary(t => t.Name);
        foreach (var name in new[] { "FresnelLens", "MoltenEgg", "StrikeDummy", "MiniatureCannon", "MysticLighter", "VitruvianMinion", "Glitter" })
            Assert.NotEmpty(SpireQoL.Diagnostics.CoverageWatch.CardTestingHooks(byName[name]));
    }

    [Fact]
    public void EveryRuleNamesARelic()
    {
        var names = Relics().Select(t => t.Name).ToHashSet();
        var unknown = CardAffinity.Rules.Select(r => r.Relic).Concat(CardAffinity.Reviewed.Keys).Where(n => !names.Contains(n)).ToList();
        Assert.Empty(unknown);
    }
}
