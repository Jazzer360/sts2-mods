using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using SpireQoL.Procs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #160: what a card's numbers gain with each play is read from its code and its enchantment's (PlayGrowth), so a replay
/// has the numbers the game gives it. Every card and enchantment of the installed game is read here: a new one whose play changes
/// its own numbers shows up as a difference to review (readable growth, or not modelled on a replay).
/// </summary>
[Collection(GameFightCollection.Name)]
public class PlayGrowthTests
{
    [Fact]
    public void CardsWhosePlayChangesTheirOwnDamage()
    {
        GameModels.EnsureLoaded();
        var reads = ModelDb.AllCards.Select(card => card.GetType()).Distinct()
            .Select(type => (type.Name, Read: PlayGrowth.CardRead(type))).Where(read => read.Read != "None")
            .OrderBy(read => read.Name).Select(read => $"{read.Name}: {read.Read}").ToList();
        Assert.Equal(new[] { "Claw: DamageByIncrease", "Maul: DamageByIncrease", "Rampage: DamageByIncrease", "TheBall: DamageByIncrease", "Thrash: Unreadable" }, reads);
    }

    [Fact]
    public void EnchantmentsWhosePlayChangesTheirNumber()
    {
        GameModels.EnsureLoaded();
        var reads = typeof(EnchantmentModel).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(EnchantmentModel).IsAssignableFrom(type) && type.Namespace == typeof(Goopy).Namespace)
            .Select(type => (type.Name, Read: PlayGrowth.EnchantmentRead(type))).Where(read => read.Read != null)
            .OrderBy(read => read.Name).Select(read => $"{read.Name}: {read.Read}").ToList();
        // Vigorous turns itself off after a play (Status Disabled, and its bonus reads its Status): its replay loses the bonus.
        Assert.Equal(new[] { "Goopy: block by 1", "Momentum: damage by Amount", "Vigorous: damage on the first play only" }, reads);
    }
}
