namespace SpireQoL.Sim;

/// <summary>
/// A chance from the simulator (SimOutcome.Chance, decision #88): Low and High are equal when the search finished. A search stopped
/// at a cap, or with a branch not modelled (decisions #107, #92), gives a range: Low counts the outcomes it settled, High adds every
/// one it didn't. Seen: some settled outcome has it.
/// </summary>
public readonly record struct SimChance(double Low, double High, bool Seen)
{
    public bool Exact => Low == High;
    public bool Certain => Low >= 1;
    public bool Possible => High > 0;

    /// <summary>"40%", "40–55%" while it isn't known exactly; never 0 when it can happen, never 100 when it might not.</summary>
    public string Text()
    {
        static string P(double p) => p <= 0 ? "0" : p < 0.01 ? "<1" : p >= 1 ? "100" : Math.Min(99, Math.Max(1, (int)Math.Round(p * 100))).ToString();
        return Exact || P(Low) == P(High) ? $"{P(Low)}%" : $"{P(Low)}–{P(High)}%";
    }

    /// <summary>For a tab's small corner: "40%", or the explored chance and a ? while it isn't exact ("40%?").</summary>
    public string ShortText() => Exact ? Text() : $"{Text().Split('–')[0].TrimEnd('%')}%?";
}
