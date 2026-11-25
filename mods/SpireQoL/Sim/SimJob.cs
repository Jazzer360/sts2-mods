using System.Collections.Concurrent;
using System.Diagnostics;

namespace SpireQoL.Sim;

/// <summary>
/// One search on a worker thread (decision #107). The frame waits a few milliseconds for it at most (SimWait, decision #104); a
/// search that takes longer carries on in the background, and its result (Latest) comes once it's over: every outcome, or where it
/// stopped at the memory cap. Cancelled when the board changes. Readers only ever see the finished outcome, which never changes.
/// </summary>
public sealed class SimJob
{
    /// <summary>Copies a search makes before it waits for a turn in the background (most finish well inside it).</summary>
    public const long FirstBudget = 20_000;

    /// <summary>The game's thread already waited for this job on some frame (SimWait): it isn't waited for again.</summary>
    public bool Waited;

    /// <summary>
    /// Searches going on past FirstBudget at once: half the cores, so a heavy board (every card, every target) leaves the game's own
    /// threads room. Searches before that aren't limited: most finish there, inside the frame's wait.
    /// </summary>
    private static readonly SemaphoreSlim Background = new(Math.Max(1, Environment.ProcessorCount / 2));

    /// <summary>Jobs searching now: what the progress widget shows (decision #89).</summary>
    private static readonly ConcurrentDictionary<SimJob, byte> Running = new();
    /// <summary>Jobs started and finished since the last time none was running: how far along they are together.</summary>
    private static int _started, _done;
    private static long _allocated;

    /// <summary>
    /// Memory every search has allocated on its worker thread since launch, in bytes (GC.GetAllocatedBytesForCurrentThread around
    /// each run): the dev dashboard's simulation share, which the frame's per-feature counts can't see.
    /// </summary>
    public static long AllocatedBytes => Interlocked.Read(ref _allocated);

    /// <summary>The searches going on now, and how far along they are together: the share of this busy spell's searches done. None: (0, 1).</summary>
    public static (int Count, double Progress) Progress()
    {
        var count = Running.Count;
        if (count == 0) return (0, 1);
        var started = Volatile.Read(ref _started);
        return (count, started == 0 ? 0 : Math.Clamp((double)Volatile.Read(ref _done) / started, 0, 1));
    }

    /// <summary>Searches a cap stopped since the last CapSummary, by cap (decision #104: how often they bind).</summary>
    private static readonly ConcurrentDictionary<string, int> Caps = new();

    /// <summary>The caps hit since the last call ("the memory cap ×2, the work cap ×1"), or null; starts counting again.</summary>
    public static string? CapSummary()
    {
        if (Caps.IsEmpty) return null;
        var line = string.Join(", ", Caps.OrderByDescending(entry => entry.Value).Select(entry => $"{entry.Key} ×{entry.Value}"));
        Caps.Clear();
        return line;
    }

    /// <summary>A cap hit, sent from the game's thread (FlushLog): what was searched, which cap, the log line, the start state.</summary>
    public sealed record CapHit(string Label, string Caps, string Line, SimState Start);

    private static readonly ConcurrentQueue<CapHit> CapsHit = new();

    /// <summary>Log lines from worker threads, written out on the main thread (FlushLog).</summary>
    private static readonly ConcurrentQueue<(string Line, Exception? Error)> Pending = new();

    private readonly SimSearch? _ex;
    /// <summary>A projection's start, turns and move rules (decisions #304, #309: the Intent Forecast's later turns) in place of a search.</summary>
    private readonly (SimState Start, IReadOnlyList<IReadOnlyList<ProjectedChoice>> Turns, IReadOnlyDictionary<int, ProjectedRules>? Rules)? _projection;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _label;
    private readonly long _firstBudget;
    private volatile SimOutcome? _latest;
    private volatile Projection? _projected;
    private long _context;

    /// <summary>
    /// The fight and your turn the boards being searched now belong to (Telemetry.SearchDone.ContextOf; 0 outside your turn), set by
    /// the game's thread each frame (SessionWatch). Each job keeps the value it started with, for the session's counts (decision #108).
    /// </summary>
    internal static long Context;

    private SimJob(SimSearch? ex, (SimState, IReadOnlyList<IReadOnlyList<ProjectedChoice>>, IReadOnlyDictionary<int, ProjectedRules>?)? projection, string label, long firstBudget)
    {
        _ex = ex;
        _projection = projection;
        _label = label;
        _firstBudget = firstBudget;
    }

    /// <summary>The state it searches from (a cap report carries it).</summary>
    private SimState StartState => _ex?.Start ?? _projection!.Value.Start;

    /// <summary>Starts searching on a worker thread (waiting for a background turn after FirstBudget copies unless told otherwise: tests).</summary>
    public static SimJob Start(SimSearch ex, string label, long firstBudget = FirstBudget) => Begin(new SimJob(ex, projection: null, label, firstBudget));

    /// <summary>
    /// Starts projecting the Intent Forecast's later turns from <paramref name="start"/> on a worker thread (decision #304: SimEngine.Project), as a
    /// search does: the frame waits for it as for any job (SimWait), it waits for a background turn past FirstBudget copies, and its result is
    /// <see cref="Projected"/> once it's over.
    /// </summary>
    public static SimJob StartProjection(SimState start, IReadOnlyList<IReadOnlyList<ProjectedChoice>> turns, string label, long firstBudget = FirstBudget,
        IReadOnlyDictionary<int, ProjectedRules>? rules = null) =>
        Begin(new SimJob(ex: null, (start, turns, rules), label, firstBudget));

    private static SimJob Begin(SimJob job)
    {
        job._context = Interlocked.Read(ref Context);
        if (Running.IsEmpty)
        {
            Interlocked.Exchange(ref _started, 0);
            Interlocked.Exchange(ref _done, 0);
        }
        Interlocked.Increment(ref _started);
        Running[job] = 0;
        Task.Run(job.Work);
        return job;
    }

    /// <summary>Completes once the search is over (or failed: Fault). Never completes when cancelled: nobody reads it then.</summary>
    public Task Ready => _ready.Task;
    /// <summary>The outcome (null before Ready).</summary>
    public SimOutcome? Latest => _latest;
    /// <summary>A projection's result (null before Ready, and for a search).</summary>
    public Projection? Projected => _projected;
    /// <summary>The search is over: every outcome is in Latest, or the cap stopped it (Latest is Truncated); a projection's is in Projected.</summary>
    public bool Finished => _latest != null || _projected != null;
    /// <summary>Kept searching after the board changed (the forecast check's frozen forecast): not cancelled with the rest.</summary>
    public volatile bool Pinned;

    public void Cancel() => _cts.Cancel();

    private void Work()
    {
        var clock = Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var gated = false;
        var work = new SimWork { Cancel = _cts.Token, HeavyAt = _firstBudget };
        work.OnHeavy = () =>
        {
            Background.Wait(_cts.Token);
            gated = true;
        };
        try
        {
            int steps, copies, peakHeld;
            string? capHit, results;
            if (_ex != null)
            {
                var outcome = _ex.Run(work);
                _latest = outcome;
                (steps, copies, peakHeld, capHit, results) = (outcome.Steps, outcome.Copies, outcome.PeakHeld, outcome.Truncated ? outcome.CapHit ?? "a cap" : null, $"{outcome.Branches.Count} outcomes");
            }
            else
            {
                var projected = SimEngine.Project(_projection!.Value.Start, _projection.Value.Turns, _projection.Value.Rules, work);
                _projected = projected;
                (steps, copies, peakHeld, capHit, results) = (projected.Steps, projected.Copies, projected.PeakHeld, projected.CapHit, $"{projected.Turns.Count} turns");
            }
            _ready.TrySetResult(true);
            Telemetry.DevEvents.Post(new Telemetry.SearchDone(_label, _context, steps, copies, peakHeld, clock.Elapsed.TotalMilliseconds, gated, capHit, Cancelled: false));
            if (capHit is { } cap)
            {
                Caps.AddOrUpdate(cap, 1, (_, index) => index + 1);
                var line = $"[sim] cap: searching {_label} hit {cap} ({SimWork.MaxHeldStates:N0} states held by every search together, {SimWork.MaxCopies:N0} copies each): " +
                           $"stopped after {steps} steps, {copies} copies, holding at most {peakHeld} states, after {clock.Elapsed.TotalMilliseconds:F1} ms";
                Pending.Enqueue((line, null));
                CapsHit.Enqueue(new CapHit(_label, cap, line, StartState));
            }
            else if (gated)
                Pending.Enqueue(($"[sim] searched {_label} in the background: {steps} steps, {copies} copies, at most {peakHeld} states, " +
                                 $"{results}, {clock.Elapsed.TotalMilliseconds:F1} ms", null));
        }
        catch (OperationCanceledException)
        {
            // The board changed: the work it had done is counted as thrown away.
            Telemetry.DevEvents.Post(new Telemetry.SearchDone(_label, _context, work.Steps, work.Copies, work.PeakHeld, clock.Elapsed.TotalMilliseconds,
                gated, Cap: null, Cancelled: true));
        }
        catch (Exception ex)
        {
            // The frame gets it (Ready faults: its frame loop reports it).
            _ready.TrySetException(ex);
        }
        finally
        {
            if (gated) Background.Release();
            Interlocked.Add(ref _allocated, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
            Running.TryRemove(this, out _);
            Interlocked.Increment(ref _done);
        }
    }

    /// <summary>Writes the workers' log lines, and reports their failures (call from the main thread).</summary>
    public static void FlushLog(Action<string> log, Action<Exception>? report = null, Action<CapHit>? cap = null)
    {
        while (Pending.TryDequeue(out var lineError))
        {
            log(lineError.Line);
            if (lineError.Error != null) report?.Invoke(lineError.Error);
        }
        while (CapsHit.TryDequeue(out var capHit)) cap?.Invoke(capHit);
    }
}
