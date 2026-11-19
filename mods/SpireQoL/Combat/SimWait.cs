using System.Diagnostics;
using SpireQoL.Sim;

namespace SpireQoL.Combat;

/// <summary>
/// How long the game's thread waits for simulations (decision #104): at most FrameMs a frame, shared by the turn forecast and every
/// card's simulations, and each job only on the first frame it's asked for. What isn't ready by then is left to the background:
/// what it feeds keeps showing its last result (decision #105; Incoming Damage with a ? past a moment) or shows as pending
/// (no tab yet), with the progress bar under End Turn, until the search is over, a frame or a few later (decision #107). A job that missed its frame never makes a later frame wait.
/// The audit's own predictions (a play as it begins, the forecast with teammates' hands) wait on a budget of their own, so a
/// check never takes the display's.
/// </summary>
internal static class SimWait
{
    /// <summary>The most a frame waits for simulations: about one frame at 120 Hz.</summary>
    public const int FrameMs = 8;

    private static ulong _frame = ulong.MaxValue;
    private static long _deadline;

    // Per-combat measurements, for the combat summary.
    private static int _waits, _missed;
    private static double _longestMs, _totalMs;

    /// <summary>Waits for one job; true once it has its result.</summary>
    public static bool Ready(SimJob job, bool ownBudget = false) => All(new[] { job }, ownBudget);

    /// <summary>
    /// Waits for these jobs' results, out of this frame's budget (or a budget of its own: the audit's), each only on the first
    /// frame it's asked for. True when every one has a result.
    /// </summary>
    public static bool All(IReadOnlyList<SimJob> jobs, bool ownBudget = false)
    {
        var fresh = jobs.Where(j => !j.Ready.IsCompleted && !j.Waited).ToList();
        if (fresh.Count > 0)
        {
            foreach (var j in fresh) j.Waited = true;
            var ms = ownBudget ? FrameMs : Left();
            var clock = Stopwatch.StartNew();
            if (ms > 0)
            {
                try { Task.WaitAll(fresh.Select(j => j.Ready).ToArray(), ms); }
                catch (AggregateException) { /* a failed search is rethrown by its reader */ }
            }
            var waited = clock.Elapsed.TotalMilliseconds;
            _waits++;
            _totalMs += waited;
            _longestMs = Math.Max(_longestMs, waited);
            if (fresh.Any(j => !j.Ready.IsCompleted)) _missed++;
        }
        return jobs.All(j => j.Ready.IsCompleted);
    }

    /// <summary>What's left of this frame's budget, in milliseconds.</summary>
    private static int Left()
    {
        var frame = Godot.Engine.GetProcessFrames();
        var now = Stopwatch.GetTimestamp();
        if (frame != _frame)
        {
            _frame = frame;
            _deadline = now + FrameMs * Stopwatch.Frequency / 1000;
        }
        return (int)Math.Max(0, (_deadline - now) * 1000 / Stopwatch.Frequency);
    }

    /// <summary>This combat's waits for the combat summary (null when nothing waited), and starts counting again.</summary>
    public static string? Summary()
    {
        if (_waits == 0) return null;
        var line = $"{_waits} waits for a result, {_totalMs / _waits:F1} ms on average, longest {_longestMs:F1} ms; " +
                   $"{_missed} left to the background past the {FrameMs} ms wait";
        _waits = _missed = 0;
        _longestMs = _totalMs = 0;
        return line;
    }
}
