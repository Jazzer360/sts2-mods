using MegaCrit.Sts2.Core.Logging;

namespace Sts2Mods.Core;

/// <summary>
/// Writes to the game's log with a "[ModId]" prefix so mod lines are easy to grep, and keeps the
/// last lines in memory so error reports can include what led up to them.
/// </summary>
public sealed class ModLog
{
    private const int Keep = 80;
    private readonly string _prefix;
    private readonly Queue<string> _recent = new();
    private readonly object _lock = new();

    public ModLog(string modId) => _prefix = $"[{modId}] ";

    public void Info(string text) { Remember("INFO", text); Log.Info(_prefix + text, skipFrames: 3); }
    public void Warn(string text) { Remember("WARN", text); Log.Warn(_prefix + text, skipFrames: 3); }
    public void Error(string text) { Remember("ERROR", text); Log.Error(_prefix + text, skipFrames: 3); }

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
    }
}
