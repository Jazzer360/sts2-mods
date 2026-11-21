namespace SpireQoL.Sim;

/// <summary>
/// The chance tab of a card that acts on what it draws (decision #119), from its play's outcomes (SimState.DrawnTally, each
/// outcome weighted by its share of the rolls): Escape Plan, the chance its draw is a Skill (its block); Pillage and Scrape, the
/// most likely number of Attacks drawn, or of cards drawn that cost 0, with its chance (a tie goes to the higher number).
/// </summary>
public static class DrawnChance
{
    /// <summary>Each tally and its share of the outcomes (null: the play made no such draw).</summary>
    public static SortedDictionary<int, double>? Distribution(SimOutcome outcome)
    {
        var withTally = outcome.Branches.Where(branch => branch.DrawnTally >= 0).ToList();
        if (withTally.Count == 0) return null;
        var d = new SortedDictionary<int, double>();
        foreach (var state in withTally) d[state.DrawnTally] = d.GetValueOrDefault(state.DrawnTally) + state.Weight;
        return d;
    }

    /// <summary>The chance the tally is above 0 (Escape Plan: it drew a Skill).</summary>
    public static double ChanceOfAny(SortedDictionary<int, double> d) => d.Where(kv => kv.Key > 0).Sum(kv => kv.Value) / Total(d);

    /// <summary>The most likely tally and its chance; a tie goes to the higher tally.</summary>
    public static (int Tally, double Chance) MostLikely(SortedDictionary<int, double> d)
    {
        var best = d.OrderByDescending(kv => Math.Round(kv.Value, 9)).ThenByDescending(kv => kv.Key).First();
        return (best.Key, best.Value / Total(d));
    }

    private static double Total(SortedDictionary<int, double> d) => Math.Max(1e-12, d.Values.Sum());
}
