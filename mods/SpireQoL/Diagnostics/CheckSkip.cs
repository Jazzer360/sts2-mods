namespace SpireQoL.Diagnostics;

/// <summary>
/// A check one play leaves out (decision #92, rule 1: a skipped check is a gap, not a pass): what wasn't checked, why, and the
/// detail. Counted by <see cref="Reason"/> in the combat summary and the dev stream, so <see cref="Why"/> stays free of names and
/// amounts (the same gap adds up across plays) and those go in <see cref="Detail"/>.
/// </summary>
/// <param name="What">The check ("block", "state after the play"), or empty when the skip covers every check of what it names.</param>
internal sealed record CheckSkip(string What, string Why, string? Detail = null)
{
    /// <summary>What it's counted under: "block: random auto-plays", or the why alone.</summary>
    public string Reason => What.Length > 0 ? $"{What}: {Why}" : Why;

    /// <summary>The log line: "block: not checked (random auto-plays): predicted 4, actual 7".</summary>
    public string Line => (What.Length > 0 ? $"{What}: not checked ({Why})" : $"not checked ({Why})") + (Detail == null ? "" : $": {Detail}");

    /// <summary>
    /// A skip no model could ever close (decision #108): the play ended the fight, so the game stopped logging (its history skips the
    /// killing hit on) or cleaned up before the check could look; or it ended your turn (Void Form), so the end of the turn ran
    /// before the check could look. Counted, but not held against the turn.
    /// </summary>
    public bool Expected => Why.StartsWith(FightEnded, StringComparison.Ordinal) || Why.StartsWith(TurnEnded, StringComparison.Ordinal);

    /// <summary>The reason a check is skipped when the play ended the fight.</summary>
    public const string FightEnded = "the fight ended";

    /// <summary>The reason a check is skipped when the play ended your turn.</summary>
    public const string TurnEnded = "the play ended your turn";
}

/// <summary>
/// A play check's [model] lines, logged after it: notes about our model of the card, and the checks the play leaves out, each a
/// <see cref="CheckSkip"/> (kept in <see cref="Skips"/> to be counted) and a line. <see cref="Skip"/> is the one way to write a skip.
/// </summary>
internal sealed class ModelLines : List<string>
{
    public List<CheckSkip> Skips { get; } = new();

    /// <summary>A check this play leaves out, and why (see <see cref="CheckSkip"/>).</summary>
    public void Skip(string what, string why, string? detail = null)
    {
        var skip = new CheckSkip(what, why, detail);
        Skips.Add(skip);
        Add(skip.Line);
    }
}
