using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The chance a reward offers a card a relic applies to (CardOdds, decompile v0.113.1 CardFactory.CreateForReward): a
/// rarity per slot from the base odds (a rarity with no card left goes up, Rare wrapping to Common), uniform within it, no
/// card twice; uniform odds skip the rarity roll.
/// </summary>
public class CardOddsTests
{
    private static RarityGroup[] G(int common, int commonMatching, int uncommon, int uncommonMatching, int rare, int rareMatching) => new[] { new RarityGroup(common, commonMatching), new RarityGroup(uncommon, uncommonMatching), new RarityGroup(rare, rareMatching) };

    [Fact]
    public void One_card_of_one_rarity_is_matches_over_total()
    {
        // Colorful Philosophers' rare reward, one card: every slot is a Rare (the others are empty and wrap to it).
        Assert.Equal(4.0 / 16, CardOdds.AtLeastOne(G(common: 0, commonMatching: 0, uncommon: 0, uncommonMatching: 0, rare: 16, rareMatching: 4), rare: 0.03, uncommon: 0.37, count: 1), 10);
    }

    [Fact]
    public void Several_cards_of_one_rarity_are_drawn_without_repeats()
    {
        // 3 of 20 commons, 5 good: 1 − C(15,3)/C(20,3) = 1 − 455/1140.
        Assert.Equal(1 - 455.0 / 1140, CardOdds.AtLeastOne(G(common: 20, commonMatching: 5, uncommon: 0, uncommonMatching: 0, rare: 0, rareMatching: 0), rare: 0.03, uncommon: 0.37, count: 3), 10);
        Assert.Equal(1 - 455.0 / 1140, CardOdds.AtLeastOneUniform(total: 20, matching: 5, count: 3), 10);
    }

    [Fact]
    public void Rarity_rolls_weight_each_rarity()
    {
        // One card: Σ P(rarity) × matching/total.
        var atLeastOne = CardOdds.AtLeastOne(G(common: 20, commonMatching: 2, uncommon: 30, uncommonMatching: 6, rare: 10, rareMatching: 5), rare: 0.03, uncommon: 0.37, count: 1);
        Assert.Equal(0.60 * 2 / 20 + 0.37 * 6 / 30 + 0.03 * 5 / 10, atLeastOne, 10);
    }

    [Fact]
    public void Matches_simulated_rewards()
    {
        // The game's draw, simulated: the exact answer must agree.
        var groups = G(common: 5, commonMatching: 1, uncommon: 3, uncommonMatching: 2, rare: 2, rareMatching: 1);
        var exact = CardOdds.AtLeastOne(groups, rare: 0.1, uncommon: 0.3, count: 4);
        var rng = new Random(7);
        int hits = 0, runs = 200_000;
        for (var run = 0; run < runs; run++)
        {
            var left = groups.Select(rarityGroup => Enumerable.Range(0, rarityGroup.Total).Select(index => index < rarityGroup.Matching).ToList()).ToArray();
            var any = false;
            for (var slot = 0; slot < 4; slot++)
            {
                var nextDouble = rng.NextDouble();
                var rarity = nextDouble < 0.1 ? 2 : nextDouble < 0.4 ? 1 : 0;
                for (var attempt = 0; attempt < 3 && left[rarity].Count == 0; attempt++) rarity = (rarity + 1) % 3;
                if (left[rarity].Count == 0) break;
                var pick = rng.Next(left[rarity].Count);
                any |= left[rarity][pick];
                left[rarity].RemoveAt(pick);
            }
            if (any) hits++;
        }
        Assert.Equal(exact, (double)hits / runs, 2);
    }

    [Fact]
    public void Nothing_matching_is_zero_and_everything_is_one()
    {
        Assert.Equal(0, CardOdds.AtLeastOne(G(common: 10, commonMatching: 0, uncommon: 10, uncommonMatching: 0, rare: 10, rareMatching: 0), rare: 0.03, uncommon: 0.37, count: 3), 10);
        Assert.Equal(1, CardOdds.AtLeastOne(G(common: 10, commonMatching: 10, uncommon: 10, uncommonMatching: 10, rare: 10, rareMatching: 10), rare: 0.03, uncommon: 0.37, count: 3), 10);
        Assert.Equal(0, CardOdds.AtLeastOneUniform(total: 10, matching: 0, count: 3), 10);
    }

    [Fact]
    public void More_cards_than_the_pool_holds_takes_them_all()
    {
        Assert.Equal(1, CardOdds.AtLeastOneUniform(total: 3, matching: 1, count: 5), 10);
        Assert.Equal(1, CardOdds.AtLeastOne(G(common: 2, commonMatching: 1, uncommon: 0, uncommonMatching: 0, rare: 0, rareMatching: 0), rare: 0.03, uncommon: 0.37, count: 5), 10);
    }

    [Fact]
    public void Several_rewards_combine()
    {
        Assert.Equal(1 - 0.5 * 0.75, CardOdds.AnyOf(new[] { 0.5, 0.25 }), 10);
    }
}
