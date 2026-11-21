using MegaCrit.Sts2.Core.Logging;

namespace Sts2Mods.Core;

/// <summary>
/// Writes to the game's log with a "[ModId]" prefix so mod lines are easy to grep, and keeps the last lines in memory so error
/// reports can include what led up to them. The game's log is shared with the game and every other mod (decision #158): Error, Warn
/// and Info are for what's exceptional or rare (a failure, the version loaded), and a warning's text reaches it once per launch;
/// Detail is for routine diagnostics (each check's lines, summaries, timings), kept in memory and shown on the dev dashboard, and
/// written to the game's log only when DetailsToGameLog is on (dev mode).
/// </summary>
public sealed class ModLog
{
    private const int Keep = 80;
    private readonly string _prefix;
    private readonly Queue<string> _recent = new();
    private readonly object _lock = new();
    private readonly HashSet<string> _warned = new();

    public ModLog(string modId) => _prefix = $"[{modId}] ";

    /// <summary>Routine diagnostics reach the game's log too (dev mode: the developer's own game).</summary>
    public bool DetailsToGameLog { get; set; }

    public void Info(string text) { Remember("INFO", text); Log.Info(_prefix + text, skipFrames: 3); }

    /// <summary>A warning; the same text again in this launch is kept in memory only (dev mode: every time).</summary>
    public void Warn(string text)
    {
        Remember("WARN", text);
        bool first;
        lock (_lock) first = _warned.Add(text);
        if (first || DetailsToGameLog) Log.Warn(_prefix + text, skipFrames: 3);
    }

    /// <summary>Routine diagnostics: kept in memory (error reports, the dev dashboard); in the game's log only with DetailsToGameLog.</summary>
    public void Detail(string text)
    {
        Remember("INFO", text);
        if (DetailsToGameLog) Log.Info(_prefix + text, skipFrames: 3);
    }
    public void Error(string text) { Remember("ERROR", text); Log.Error(_prefix + text, skipFrames: 3); }

    /// <summary>Every line written (level, text), on the writer's thread: the dev dashboard's log tail (decision #108).</summary>
    public event Action<string, string>? Written;

    /// <summary>The most recent mod log lines, oldest first.</summary>
    public IReadOnlyList<string> Recent()
    {
        lock (_lock) return _recent.ToList();
    }

    private void Remember(string level, string text)
    {
        lock (_lock)
        {
            _recent.Enqueue($"{DateTime.Now:HH:mm:ss} {level} {text}");
            while (_recent.Count > Keep) _recent.Dequeue();
        }
        try { Written?.Invoke(level, text); } catch { /* a listener's problem is never the log's */ }
    }
}
