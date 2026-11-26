using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A card's CardCmd.Enchant of a card it makes is no action only for an enchantment that acts on its own card's play alone (decision #349:
/// SimAdapter.EnchantmentActsOnlyOnItsPlay, read from the class's overrides in the installed game): the ones whose code answers a hook while their card
/// sits in a pile (a draw, the flush, a shuffle) are told apart, so one a game update adds is read the same way, never named. StepTranslationTests holds
/// every card's Enchant step to a card it makes.
/// </summary>
public class EnchantmentListenerTests
{
    [Fact]
    public void InkyActsOnlyOnItsCardsPlay() => Assert.True(SimAdapter.EnchantmentActsOnlyOnItsPlay("Inky"));

    [Fact]
    public void AnEnchantmentListeningElsewhereIsToldApart()
    {
        Assert.False(SimAdapter.EnchantmentActsOnlyOnItsPlay("Slither")); // AfterCardDrawn
        Assert.False(SimAdapter.EnchantmentActsOnlyOnItsPlay("SlumberingEssence")); // BeforeFlush
        Assert.False(SimAdapter.EnchantmentActsOnlyOnItsPlay("PerfectFit")); // ModifyShuffleOrder
        Assert.False(SimAdapter.EnchantmentActsOnlyOnItsPlay("NoSuchEnchantment"));
    }
}
