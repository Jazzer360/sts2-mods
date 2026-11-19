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
        var line = string.Join(", ", Caps.OrderByDescending(k => k.Value).Select(k => $"{k.Key} ×{k.Value}"));
        Caps.Clear();
        return line;
    }

    /// <summary>A cap hit, sent from the game's thread (FlushLog): what was searched, which cap, the log line, the start state.</summary>
    public sealed record CapHit(string Label, string Caps, string Line, SimState Start);

    private static readonly ConcurrentQueue<CapHit> CapsHit = new();

    /// <summary>Log lines from worker threads, written out on the main thread (FlushLog).</summary>
    private static readonly ConcurrentQueue<(string Line, Exception? Error)> Pending = new();

    private readonly SimExploration _ex;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _label;
    private readonly long _firstBudget;
    private volatile SimOutcome? _latest;
    private long _context;

    /// <summary>
    /// The fight and your turn the boards being searched now belong to (Telemetry.SearchDone.ContextOf; 0 outside your turn), set by
    /// the game's thread each frame (SessionWatch). Each job keeps the value it started with, for the session's counts (decision #108).
    /// </summary>
    public static long Context;

    private SimJob(SimExploration ex, string label, long firstBudget)
    {
        _ex = ex;
        _label = label;
        _firstBudget = firstBudget;
    }

    /// <summary>Starts searching on a worker thread (waiting for a background turn after FirstBudget copies unless told otherwise: tests).</summary>
    public static SimJob Start(SimExploration ex, string label, long firstBudget = FirstBudget)
    {
        var job = new SimJob(ex, label, firstBudget) { _context = Interlocked.Read(ref Context) };
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
    /// <summary>The search is over: every outcome is in Latest, or the cap stopped it (Latest is Truncated).</summary>
    public bool Finished => _latest != null;
    /// <summary>Kept searching after the board changed (the forecast check's frozen forecast): not cancelled with the rest.</summary>
    public volatile bool Pinned;

    public void Cancel() => _cts.Cancel();

    private void Work()
    {
        var clock = Stopwatch.StartNew();
        var gated = false;
        var work = new SimWork { Cancel = _cts.Token, HeavyAt = _firstBudget };
        work.OnHeavy = () =>
        {
            Background.Wait(_cts.Token);
            gated = true;
        };
        try
        {
            var o = _ex.Run(work);
            _latest = o;
            _ready.TrySetResult(true);
            Telemetry.DevEvents.Post(new Telemetry.SearchDone(_label, _context, o.Steps, o.Copies, o.PeakHeld, clock.Elapsed.TotalMilliseconds,
                gated, o.Truncated ? o.CapHit ?? "a cap" : null, Cancelled: false));
            if (o.Truncated)
            {
                var cap = o.CapHit ?? "a cap";
                Caps.AddOrUpdate(cap, 1, (_, n) => n + 1);
                var line = $"[sim] cap: searching {_label} hit {cap} ({SimWork.MaxHeldStates:N0} states held by every search together, {SimWork.MaxCopies:N0} copies each): " +
                           $"stopped after {o.Steps} steps, {o.Copies} copies, holding at most {o.PeakHeld} states, after {clock.Elapsed.TotalMilliseconds:F1} ms";
                Pending.Enqueue((line, null));
                CapsHit.Enqueue(new CapHit(_label, cap, line, _ex.Start));
            }
            else if (gated)
                Pending.Enqueue(($"[sim] searched {_label} in the background: {o.Steps} steps, {o.Copies} copies, at most {o.PeakHeld} states, " +
                                 $"{o.Branches.Count} outcomes, {clock.Elapsed.TotalMilliseconds:F1} ms", null));
        }
        catch (OperationCanceledException)
        {
            // The board changed: the work it had done is counted as thrown away.
            Telemetry.DevEvents.Post(new Telemetry.SearchDone(_label, _context, work.Steps, work.Copies, work.PeakHeld, clock.Elapsed.TotalMilliseconds,
                gated, null, Cancelled: true));
        }
        catch (Exception ex)
        {
            // The frame gets it (Ready faults: its frame loop reports it).
            _ready.TrySetException(ex);
        }
        finally
        {
            if (gated) Background.Release();
            Running.TryRemove(this, out _);
            Interlocked.Increment(ref _done);
        }
    }

    /// <summary>Writes the workers' log lines, and reports their failures (call from the main thread).</summary>
    public static void FlushLog(Action<string> log, Action<Exception>? report = null, Action<CapHit>? cap = null)
    {
        while (Pending.TryDequeue(out var e))
        {
            log(e.Line);
            if (e.Error != null) report?.Invoke(e.Error);
        }
        while (CapsHit.TryDequeue(out var c)) cap?.Invoke(c);
    }
}
