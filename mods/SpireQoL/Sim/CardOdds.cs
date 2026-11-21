namespace SpireQoL.Sim;

/// <summary>The cards of one rarity a reward can offer: how many, and how many of them a relic applies to.</summary>
public readonly record struct RarityGroup(int Total, int Matching);

/// <summary>
/// The chance a card reward offers at least one card a relic applies to (docs/CARD_ODDS.md), from the game's rules
/// (decompile v0.113.1, CardFactory.CreateForReward), with no game types:
///  - each slot rolls a rarity with the base odds (events use RollWithBaseOdds: no rarity offset), and a rarity with no card
///    left goes to the next one up, wrapping Rare to Common (GetNextAllowedRarity);
///  - the card is uniform within the rarity, and no card is offered twice (the cards already offered are left out);
///  - uniform odds (CardRarityOddsType.Uniform) skip the rarity roll: every card is equally likely.
/// Exact, not sampled: every rarity path is followed, carrying only the "nothing matched yet" branch.
/// </summary>
public static class CardOdds
{
    /// <param name="groups">Common, Uncommon and Rare, in that order.</param>
    /// <param name="rare">The base chance of a Rare per slot.</param>
    /// <param name="uncommon">The base chance of an Uncommon per slot (Common is the rest).</param>
    /// <param name="count">Cards offered.</param>
    public static double AtLeastOne(IReadOnlyList<RarityGroup> groups, double rare, double uncommon, int count)
    {
        if (groups.Count != 3) throw new ArgumentException("Common, Uncommon and Rare", nameof(groups));
        var totals = groups.Select(rarityGroup => rarityGroup.Total).ToArray();
        var matching = groups.Select(rarityGroup => rarityGroup.Matching).ToArray();
        var odds = new[] { Math.Max(0, 1 - rare - uncommon), uncommon, rare };
        return 1 - NoneMatch(totals, matching, odds, count);
    }

    private static double NoneMatch(int[] totals, int[] matching, double[] odds, int slots)
    {
        if (slots <= 0) return 1;
        var none = 0.0;
        for (var rolled = 0; rolled < 3; rolled++)
        {
            if (odds[rolled] <= 0) continue;
            // GetNextAllowedRarity: Common → Uncommon → Rare → Common, until one has a card left.
            var r = rolled;
            var tries = 0;
            while (totals[r] <= 0 && tries++ < 3) r = (r + 1) % 3;
            if (totals[r] <= 0) return 1; // nothing left to offer: no more slots
            var miss = (double)(totals[r] - matching[r]) / totals[r];
            if (miss <= 0) continue;
            totals[r]--;
            none += odds[rolled] * miss * NoneMatch(totals, matching, odds, slots - 1);
            totals[r]++;
        }
        return none;
    }

    /// <summary>Uniform odds: <paramref name="count"/> different cards from <paramref name="total"/>, <paramref name="matching"/> of them good.</summary>
    public static double AtLeastOneUniform(int total, int matching, int count)
    {
        if (matching <= 0 || total <= 0) return 0;
        count = Math.Min(count, total);
        // 1 − C(total − matching, count) / C(total, count), as a product.
        var none = 1.0;
        for (var i = 0; i < count; i++) none *= (double)(total - matching - i) / (total - i);
        return 1 - Math.Max(0, none);
    }

    /// <summary>Several independent rewards (Colorful Philosophers' three): the chance at least one of them matches.</summary>
    public static double AnyOf(IEnumerable<double> chances) => 1 - chances.Aggregate(1.0, (none, chance) => none * (1 - chance));
}
