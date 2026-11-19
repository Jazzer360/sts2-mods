using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The chance a reward offers a card a relic applies to (CardOdds, decompile v0.113.1 CardFactory.CreateForReward): a
/// rarity per slot from the base odds (a rarity with no card left goes up, Rare wrapping to Common), uniform within it, no
/// card twice; uniform odds skip the rarity roll.
/// </summary>
public class CardOddsTests
{
    private static RarityGroup[] G(int c, int cm, int u, int um, int r, int rm) => new[] { new RarityGroup(c, cm), new RarityGroup(u, um), new RarityGroup(r, rm) };

    [Fact]
    public void One_card_of_one_rarity_is_matches_over_total()
    {
        // Colorful Philosophers' rare reward, one card: every slot is a Rare (the others are empty and wrap to it).
        Assert.Equal(4.0 / 16, CardOdds.AtLeastOne(G(0, 0, 0, 0, 16, 4), 0.03, 0.37, 1), 10);
    }

    [Fact]
    public void Several_cards_of_one_rarity_are_drawn_without_repeats()
    {
        // 3 of 20 commons, 5 good: 1 − C(15,3)/C(20,3) = 1 − 455/1140.
        Assert.Equal(1 - 455.0 / 1140, CardOdds.AtLeastOne(G(20, 5, 0, 0, 0, 0), 0.03, 0.37, 3), 10);
        Assert.Equal(1 - 455.0 / 1140, CardOdds.AtLeastOneUniform(20, 5, 3), 10);
    }

    [Fact]
    public void Rarity_rolls_weight_each_rarity()
    {
        // One card: Σ P(rarity) × matching/total.
        var p = CardOdds.AtLeastOne(G(20, 2, 30, 6, 10, 5), 0.03, 0.37, 1);
        Assert.Equal(0.60 * 2 / 20 + 0.37 * 6 / 30 + 0.03 * 5 / 10, p, 10);
    }

    [Fact]
    public void Matches_simulated_rewards()
    {
        // The game's draw, simulated: the exact answer must agree.
        var groups = G(5, 1, 3, 2, 2, 1);
        var exact = CardOdds.AtLeastOne(groups, 0.1, 0.3, 4);
        var rng = new Random(7);
        int hits = 0, runs = 200_000;
        for (var run = 0; run < runs; run++)
        {
            var left = groups.Select(g => Enumerable.Range(0, g.Total).Select(i => i < g.Matching).ToList()).ToArray();
            var any = false;
            for (var slot = 0; slot < 4; slot++)
            {
                var x = rng.NextDouble();
                var r = x < 0.1 ? 2 : x < 0.4 ? 1 : 0;
                for (var t = 0; t < 3 && left[r].Count == 0; t++) r = (r + 1) % 3;
                if (left[r].Count == 0) break;
                var pick = rng.Next(left[r].Count);
                any |= left[r][pick];
                left[r].RemoveAt(pick);
            }
            if (any) hits++;
        }
        Assert.Equal(exact, (double)hits / runs, 2);
    }

    [Fact]
    public void Nothing_matching_is_zero_and_everything_is_one()
    {
        Assert.Equal(0, CardOdds.AtLeastOne(G(10, 0, 10, 0, 10, 0), 0.03, 0.37, 3), 10);
        Assert.Equal(1, CardOdds.AtLeastOne(G(10, 10, 10, 10, 10, 10), 0.03, 0.37, 3), 10);
        Assert.Equal(0, CardOdds.AtLeastOneUniform(10, 0, 3), 10);
    }

    [Fact]
    public void More_cards_than_the_pool_holds_takes_them_all()
    {
        Assert.Equal(1, CardOdds.AtLeastOneUniform(3, 1, 5), 10);
        Assert.Equal(1, CardOdds.AtLeastOne(G(2, 1, 0, 0, 0, 0), 0.03, 0.37, 5), 10);
    }

    [Fact]
    public void Several_rewards_combine()
    {
        Assert.Equal(1 - 0.5 * 0.75, CardOdds.AnyOf(new[] { 0.5, 0.25 }), 10);
    }
}
