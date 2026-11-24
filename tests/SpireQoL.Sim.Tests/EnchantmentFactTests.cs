using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What an enchantment's code says of its card outside a play, read from its code (decision #224): Perfect Fit's card on top when shuffled
/// (PerfectFit.ModifyShuffleOrder: Remove(base.Card), Insert(0, base.Card) on a shuffle that isn't the fight's first), and the cards Swift draws
/// when its card is played (Swift.OnPlay: CardPileCmd.Draw(base.Amount) while its Status is Normal), for the Return tags' hand size.
/// </summary>
public class EnchantmentFactTests
{
    private static SimCard Enchanted(string enchantment, int amount, int status = 0) =>
        new("Strike", SimCardType.Attack) { Enchantment = new SimEnchantment(enchantment, amount) { Fields = NamedCounts.Empty.With("Status", status) } };

    [Fact]
    public void PerfectFitsCardIsOnTopWhenShuffled()
    {
        Assert.True(Assert.IsType<ReadModel>(SimModels.Enchantment("PerfectFit")).Spec.OnTopWhenShuffled);
        Assert.False(Assert.IsType<ReadModel>(SimModels.Enchantment("Swift")).Spec.OnTopWhenShuffled);
    }

    [Fact]
    public void SwiftDrawsItsAmountWhileItsOn()
    {
        var swift = Assert.IsType<ReadModel>(SimModels.Enchantment("Swift"));
        Assert.Equal(2, swift.DrawsOnPlay(Enchanted("Swift", amount: 2)));
        Assert.Equal(0, swift.DrawsOnPlay(Enchanted("Swift", amount: 2, status: 1))); // Disabled after its first play
        // An enchantment whose OnPlay draws nothing.
        Assert.Equal(0, Assert.IsType<ReadModel>(SimModels.Enchantment("Adroit")).DrawsOnPlay(Enchanted("Adroit", amount: 3)));
    }
}
