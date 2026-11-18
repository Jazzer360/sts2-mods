using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SpireQoL.Sim;

/// <summary>
/// One exploration on a worker thread (decision #87): a first pass up to FirstBudget copies (what the frame waits for: Ready),
/// then it keeps exploring in chunks, publishing a snapshot after each (Latest), until every outcome is explored, a cap is hit
/// or it's cancelled (the board changed). Readers only ever see whole snapshots, which never change.
/// </summary>
public sealed class SimJob
{
    /// <summary>Copies the first pass may make before the frame gets a result (most fights finish well inside it).</summary>
    public const long FirstBudget = 20_000;
    /// <summary>Copies between two snapshots after that.</summary>
    public const long ChunkBudget = 100_000;

    /// <summary>
    /// Explorations going on past their first pass at once: half the cores, so a heavy board (every card, every target) leaves
    /// the game's own threads room. First passes aren't limited: the frame waits for them, as it always did.
    /// </summary>
    private static readonly SemaphoreSlim Background = new(Math.Max(1, Environment.ProcessorCount / 2));

    /// <summary>Jobs exploring in the background now (past their first pass): what the progress widget shows (decision #89).</summary>
    private static readonly ConcurrentDictionary<SimJob, byte> Running = new();

    /// <summary>
    /// The explorations going on in the background now, and how far along they are together: the mean share of the rolls each has
    /// explored (0 to 1). None: (0, 1).
    /// </summary>
    public static (int Count, double Progress) Progress()
    {
        var jobs = Running.Keys.ToList();
        if (jobs.Count == 0) return (0, 1);
        var shares = jobs.Select(j => j._latest?.Explored is { } e && !double.IsNaN(e) ? Math.Clamp(e, 0, 1) : 0).ToList();
        return (jobs.Count, shares.Average());
    }

    /// <summary>Log lines from worker threads, written out on the main thread (FlushLog).</summary>
    private static readonly ConcurrentQueue<(string Line, Exception? Error)> Pending = new();

    private readonly SimExploration _ex;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _label;
    private volatile SimOutcome? _latest;
    private volatile bool _finished;

    private SimJob(SimExploration ex, string label)
    {
        _ex = ex;
        _label = label;
    }

    /// <summary>Starts exploring on a worker thread.</summary>
    public static SimJob Start(SimExploration ex, string label)
    {
        var job = new SimJob(ex, label);
        Task.Run(job.Work);
        return job;
    }

    /// <summary>Completes once the first pass has a snapshot (or failed: Fault).</summary>
    public Task Ready => _ready.Task;
    /// <summary>The latest snapshot (null before Ready).</summary>
    public SimOutcome? Latest => _latest;
    /// <summary>Nothing more will be explored: every outcome is in Latest, or a cap stopped it (Latest stays partial).</summary>
    public bool Finished => _finished;
    /// <summary>Kept exploring after the board changed (the forecast check's frozen forecast): not cancelled with the rest.</summary>
    public volatile bool Pinned;

    public void Cancel() => _cts.Cancel();

    private void Work()
    {
        var clock = Stopwatch.StartNew();
        double firstMs = 0;
        try
        {
            _ex.Explore(FirstBudget, _cts.Token);
            _latest = _ex.Snapshot();
            firstMs = clock.Elapsed.TotalMilliseconds;
            _finished = _ex.Finished;
            _ready.TrySetResult(true);
            if (!_ex.Finished)
            {
                Running[this] = 0;
                Background.Wait(_cts.Token);
                try
                {
                    while (!_ex.Finished && !_cts.IsCancellationRequested)
                    {
                        _ex.Explore(ChunkBudget, _cts.Token);
                        _latest = _ex.Snapshot();
                    }
                }
                finally
                {
                    Background.Release();
                }
            }
            _finished = _ex.Finished;
        }
        catch (OperationCanceledException)
        {
            // cancelled while waiting for a turn in the background
            Running.TryRemove(this, out _);
        }
        catch (Exception ex)
        {
            Running.TryRemove(this, out _);
            // Before the first result, the frame gets it (Ready faults); after, it goes to the log and the error reports.
            if (!_ready.TrySetException(ex)) Pending.Enqueue(($"[sim] exploring {_label} failed in the background: {ex.GetType().Name}: {ex.Message}", ex));
            return;
        }
        Running.TryRemove(this, out _);
        // Only explorations that needed slicing are logged: the rest are one run, as before.
        if (_ex.Runs > 1)
        {
            var end = _ex.Done ? "complete" : _ex.Capped ? "capped (stays partial)" : "cancelled (the board changed)";
            Pending.Enqueue(($"[sim] explored {_label}: {_ex.Runs} slices, depth {_ex.MaxDepth}, {_ex.Copies} copies, {_latest?.Branches.Count} branches, " +
                            $"{_ex.ExploredWeight:P1} of the rolls; first result {firstMs:F1} ms, {end} after {clock.Elapsed.TotalMilliseconds:F1} ms", null));
        }
    }

    /// <summary>Writes the workers' log lines, and reports their failures (call from the main thread).</summary>
    public static void FlushLog(Action<string> log, Action<Exception>? report = null)
    {
        while (Pending.TryDequeue(out var e))
        {
            log(e.Line);
            if (e.Error != null) report?.Invoke(e.Error);
        }
    }
}
