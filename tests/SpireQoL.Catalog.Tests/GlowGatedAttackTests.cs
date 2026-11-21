using SpireQoL.Procs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Cards that attack on a condition and glow gold, read from their code (PlayFacts.AttacksConditionallyAndGlows): each is listed as
/// attacking only while it glows (Pact's End) or reviewed as not (Fetch, Flatten: Osty; Nail: its target). The card scan holds new ones.
/// </summary>
public class GlowGatedAttackTests
{
    [Fact]
    public void EveryCardThatAttacksConditionallyAndGlowsIsListedOrReviewed()
    {
        GameModels.EnsureLoaded();
        var found = MegaCrit.Sts2.Core.Models.ModelDb.AllCards.Select(card => card.GetType()).Distinct()
            .Where(PlayFacts.AttacksConditionallyAndGlows).Select(type => type.Name).OrderBy(name => name).ToList();
        Assert.Contains("PactsEnd", found);
        Assert.DoesNotContain("Spite", found); // its glow changes how many times it hits; it always attacks
        foreach (var name in found)
            Assert.True(PlayFacts.GlowGatedAttackCards.Contains(name) ^ PlayFacts.GlowNotTheAttack.ContainsKey(name), $"{name}: listed in neither, or in both");
        foreach (var name in PlayFacts.GlowGatedAttackCards.Concat(PlayFacts.GlowNotTheAttack.Keys))
            Assert.True(found.Contains(name), $"{name} no longer attacks on a condition and glows: take it off the list");
    }
}
