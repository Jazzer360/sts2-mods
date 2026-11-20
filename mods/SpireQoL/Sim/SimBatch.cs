namespace SpireQoL.Sim;

/// <summary>
/// A card's simulations, one per target: each searches on a worker (decision #107). Complete the moment every one of its jobs is
/// (what the frame waits for: SimWait waits on the jobs themselves), and Latest is their outcomes.
///
/// Read from the jobs, never through a Task.WhenAll of them: a job completes its Ready with continuations run asynchronously, so a
/// WhenAll completes on a thread-pool continuation a moment after the last job. Read right after the frame's wait, it could still
/// say pending though every search was over: the card's tabs held a frame longer than needed, and the audit's check of the tabs shown
/// skipped as "simulation still exploring" on searches that took 0.1 ms.
/// </summary>
public sealed class SimBatch
{
    public SimBatch(SimJob[] jobs) => Jobs = jobs;

    public SimJob[] Jobs { get; }

    /// <summary>Every search is over (or failed: Fault).</summary>
    public bool IsCompleted => Jobs.All(j => j.Ready.IsCompleted);

    /// <summary>The first search's failure, if one failed.</summary>
    public Exception? Fault => Jobs.Select(j => j.Ready).FirstOrDefault(t => t.IsFaulted)?.Exception?.GetBaseException();

    public SimOutcome[] Latest => Jobs.Select(j => j.Latest!).ToArray();
    public bool Finished => Jobs.All(j => j.Finished);
    public void Cancel() { foreach (var j in Jobs) j.Cancel(); }
}
