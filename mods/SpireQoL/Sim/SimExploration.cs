namespace SpireQoL.Sim;

/// <summary>
/// One simulation to search (decision #107): the start state, the plays still resolving or queued, and the play. SimEngine.Run
/// searches it in one go, every step merging identical branches, so it either finishes with every outcome or stops at the memory
/// cap (SimOutcome.Truncated). SimJob runs it on a worker thread.
/// </summary>
public sealed class SimExploration
{
    private readonly IReadOnlyList<SimScript>? _leadIn;
    private readonly SimScript? _play;

    public SimExploration(SimState start, IReadOnlyList<SimScript>? leadIn, SimScript? play)
    {
        Start = start;
        _leadIn = leadIn;
        _play = play;
    }

    public static SimExploration Of(SimState start, SimPlay? play = null) => new(start, null, play == null ? null : SimScript.Of(play));

    /// <summary>The state it searches from (cap reports carry it: decision #105).</summary>
    public SimState Start { get; }

    /// <summary>Searches every outcome (up to the memory cap).</summary>
    internal SimOutcome Run(SimWork? work = null) => SimEngine.Run(Start, _leadIn, _play, work);

    /// <summary>Searches every outcome (up to the memory cap). For tests and checks that need the whole outcome at once.</summary>
    public SimOutcome ExploreAll() => Run();
}
