using MegaCrit.Sts2.Core.Logging;

namespace Sts2Mods.Core;

/// <summary>Writes to the game's log with a "[ModId]" prefix so mod lines are easy to grep.</summary>
public sealed class ModLog
{
    private readonly string _prefix;

    public ModLog(string modId) => _prefix = $"[{modId}] ";

    public void Info(string text) => Log.Info(_prefix + text, skipFrames: 3);
    public void Warn(string text) => Log.Warn(_prefix + text, skipFrames: 3);
    public void Error(string text) => Log.Error(_prefix + text, skipFrames: 3);
}
