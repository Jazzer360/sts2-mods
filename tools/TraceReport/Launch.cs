using System.Text.Json;

namespace TraceReport;

/// <summary>A stretch of the launch at one tracing level (seconds since launch, the dev stream's clock).</summary>
internal sealed record Stretch(string Level, double From, double To);

/// <summary>The frames of the launch at one tracing level, from the dev stream's frame rollups (decision #258: what each level costs).</summary>
internal sealed class LevelFrames
{
    public double Seconds;
    public long Frames;
    public readonly List<double> ModMedians = new();
    public double ModP95Sum, AllocatedMb, GcPauseMs;
    public int Rollups;
}

/// <summary>
/// The launch a trace was recorded in, from its dev stream file (the one whose trace-started names the trace): when it started, the
/// stretches at each tracing level, the time the game's window was in the background (decision #238: the timeline counts time played),
/// and the mod's frames at each level.
/// </summary>
internal sealed class Launch
{
    public string Stream = "";
    public DateTime StartedUtc;
    public double End;
    public double? TraceFrom, TraceTo;
    public readonly List<Stretch> Stretches = new();
    public readonly List<(double From, double To)> Away = new();
    public readonly Dictionary<string, LevelFrames> Levels = new();

    /// <summary>The launch of <paramref name="tracePath"/>: the dev folder is its folder's parent. Null when no stream names it.</summary>
    public static Launch? Find(string tracePath)
    {
        var traceName = Path.GetFileName(tracePath);
        var devFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(tracePath)));
        if (devFolder == null || !Directory.Exists(devFolder)) return null;
        foreach (var stream in Directory.EnumerateFiles(devFolder, "stream-*.jsonl").OrderByDescending(file => file, StringComparer.Ordinal))
        {
            if (!Names(stream, traceName)) continue;
            return Read(stream, traceName);
        }
        return null;
    }

    private static bool Names(string stream, string traceName)
    {
        foreach (var line in ReadLines(stream))
            if (line.Contains("\"kind\":\"trace-started\"", StringComparison.Ordinal) && line.Contains(traceName, StringComparison.Ordinal)) return true;
        return false;
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        // The game may be appending to it: shared read.
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        while (reader.ReadLine() is { } line) yield return line;
    }

    private static Launch Read(string stream, string traceName)
    {
        var launch = new Launch { Stream = Path.GetFileName(stream) };
        var level = "off";
        double levelFrom = 0;
        double? awayFrom = null;
        double lastT = 0;
        foreach (var line in ReadLines(stream))
        {
            var kind = KindOf(line);
            if (kind is not ("stream-started" or "trace-started" or "trace-stopped" or "focus-changed" or "frame-rollup")) continue;
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var at = root.TryGetProperty("t", out var time) ? time.GetDouble() : lastT;
            lastT = Math.Max(lastT, at);
            switch (kind)
            {
                case "stream-started":
                    if (root.TryGetProperty("startedUtc", out var started)) launch.StartedUtc = started.GetDateTime().ToUniversalTime();
                    break;
                case "trace-started":
                    launch.Stretches.Add(new Stretch(level, levelFrom, at));
                    level = root.GetProperty("level").GetString() ?? "?";
                    levelFrom = at;
                    if (Path.GetFileName(root.GetProperty("path").GetString() ?? "") == traceName) launch.TraceFrom = at;
                    break;
                case "trace-stopped":
                    launch.Stretches.Add(new Stretch(level, levelFrom, at));
                    level = "off";
                    levelFrom = at;
                    if (Path.GetFileName(root.GetProperty("path").GetString() ?? "") == traceName) launch.TraceTo = at;
                    break;
                case "focus-changed":
                    var focused = root.GetProperty("focused").GetBoolean();
                    if (!focused) awayFrom ??= at;
                    else if (awayFrom is { } from)
                    {
                        launch.Away.Add((from, at));
                        awayFrom = null;
                    }
                    break;
                case "frame-rollup":
                    if (!launch.Levels.TryGetValue(level, out var frames)) launch.Levels[level] = frames = new LevelFrames();
                    frames.Rollups++;
                    frames.Seconds += root.GetProperty("seconds").GetDouble();
                    frames.Frames += root.GetProperty("frames").GetInt64();
                    frames.ModMedians.Add(root.GetProperty("modP50").GetDouble());
                    frames.ModP95Sum += root.GetProperty("modP95").GetDouble();
                    frames.AllocatedMb += root.TryGetProperty("allocatedMb", out var allocated) ? allocated.GetDouble() : 0;
                    frames.GcPauseMs += root.TryGetProperty("gcPauseMs", out var pause) ? pause.GetDouble() : 0;
                    break;
            }
        }
        launch.End = lastT;
        launch.Stretches.Add(new Stretch(level, levelFrom, lastT));
        launch.Stretches.RemoveAll(stretch => stretch.To <= stretch.From);
        if (awayFrom is { } stillAway) launch.Away.Add((stillAway, lastT));
        return launch;
    }

    /// <summary>A line's kind, without parsing it: the stream writes <c>"kind":"..."</c> right after the time.</summary>
    private static string KindOf(string line)
    {
        const string marker = "\"kind\":\"";
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "";
        start += marker.Length;
        var end = line.IndexOf('"', start);
        return end < 0 ? "" : line[start..end];
    }
}
