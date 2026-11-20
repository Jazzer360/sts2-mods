namespace SpireQoL.Sim;

/// <summary>
/// Chances shown (strictly between 0 and 100%) and what happened, added up (decision #95): one outcome can't judge "40%", many can.
/// The forecast check keeps one for the death chances, the play check one for the drawn-card chances (decision #119).
/// </summary>
public sealed class OddsTally
{
    public int Count, Happened;
    public double Low, High, Variance;

    public void Add(SimChance c, bool happened)
    {
        Count++;
        if (happened) Happened++;
        Low += c.Low;
        High += c.High;
        var p = (c.Low + c.High) / 2;
        Variance += p * (1 - p);
    }

    public string Text() => $"{Count} shown, {Happened} happened, expected {Low:0.#}{(High - Low > 0.05 ? $"–{High:0.#}" : "")}";

    /// <summary>
    /// What happened is far outside what the chances allow: below their lowest or above their highest by more than 4 standard
    /// deviations (about 1 in 30,000 by chance), over at least 30 of them.
    /// </summary>
    public bool Drifted => Count >= 30 && (Happened < Low - 4 * Math.Sqrt(Variance) || Happened > High + 4 * Math.Sqrt(Variance));
}
