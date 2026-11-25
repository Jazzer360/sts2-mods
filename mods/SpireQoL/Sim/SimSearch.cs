namespace SpireQoL.Sim;

/// <summary>
/// One simulation to search (decision #107): the start state and the play. SimEngine.Run
/// searches it in one go, every step merging identical branches, so it either finishes with every outcome or stops at the memory
/// cap (SimOutcome.Truncated). SimJob runs it on a worker thread.
/// </summary>
public sealed class SimSearch
{
    private readonly SimScript? _play;
    private readonly bool _playOnly;

    public SimSearch(SimState start, SimScript? play, bool playOnly = false)
    {
        Start = start;
        _play = play;
        _playOnly = playOnly;
    }

    public static SimSearch Of(SimState start, SimPlay? play = null) => new(start, play == null ? null : SimScript.Of(play));

    /// <summary>The play alone, with its own results and log, no turn after it (a potion's use for its tags: decision #318).</summary>
    public static SimSearch PlayOnly(SimState start, SimPlay play) => new(start, SimScript.Of(play), playOnly: true);

    /// <summary>The state it searches from (cap reports carry it: decision #105).</summary>
    public SimState Start { get; }

    /// <summary>Searches every outcome (up to the memory cap).</summary>
    internal SimOutcome Run(SimWork? work = null) => SimEngine.Run(Start, _play, work, _playOnly);

    /// <summary>Searches every outcome (up to the memory cap). For tests and checks that need the whole outcome at once.</summary>
    public SimOutcome SearchAll() => Run();
}
