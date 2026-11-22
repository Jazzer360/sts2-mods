using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpireQoL.Telemetry;

// ---------------------------------------------------------------- events only the dev dashboard reads

/// <summary>A launch's stream begins: the format, the mod version, its build (BuildInfo.Label: version, commit, uncommitted), the game.</summary>
public sealed record StreamStarted(int Format, string Mod, string Game, DateTime StartedUtc, string Build) : DevEvent;

/// <summary>
/// The frames of the last moment (about 250 ms), rolled up: how many, the mod's time per frame (median, 95th percentile, largest),
/// the longest gap between frames, slow frames (the mod over 30 ms), each feature's mean and largest time and mean memory per frame
/// (the mod's code in the game's hooks too, as "hook:" entries), the whole process's memory, allocation and collections in that
/// moment, the simulations' allocation on their worker threads, the engine's shader compiles (pipeline compilations: a first draw
/// of something new stalls drawing while they run) and its node counts (orphans: nodes freed from the tree but never deleted), the
/// simulator's states held by every search and the searches running.
/// </summary>
public sealed record FrameRollup(double Seconds, int Frames, double ModP50, double ModP95, double ModMax, double GapMax, int Slow,
    Dictionary<string, double> FeatureMean, Dictionary<string, double> FeatureMax, Dictionary<string, double> FeatureKb,
    double HeapMb, double AllocatedMb, int Gen0, int Gen1, int Gen2, double GcPauseMs, long HeldStates, int Searching,
    double SimMb, int Compiles, int Nodes, int OrphanNodes, bool Quiet = false, int DevChecks = 0) : DevEvent;

/// <summary>
/// What the garbage was made of over the last few seconds (AllocationSampler, decision #176): KB by "thread|type" (thread: game or
/// other), from the runtime's allocation ticks, split by whether the moment was quiet (no input, no board change) or active.
/// </summary>
public sealed record AllocationSample(double Seconds, double QuietSeconds, Dictionary<string, double> QuietKb, Dictionary<string, double> ActiveKb) : DevEvent;

/// <summary>A line of the mod's own log ([SpireQoL] lines).</summary>
public sealed record LogLine(string Level, string Text) : DevEvent;

/// <summary>A report saved in the dev folder instead of sent (dev mode): its file is <c>reports/{Id}.json</c>.</summary>
public sealed record ReportSaved(string Id, string What, string Title, string[] Fingerprint) : DevEvent;

// ---------------------------------------------------------------- the rollup

/// <summary>
/// The frames since the last rollup (decision #108): fed every frame on the game's thread, taken about every 250 ms. Not thread-safe:
/// the game's thread does both.
/// </summary>
public sealed class FrameWindow
{
    private readonly List<double> _mod = new();
    private double _gapMax;
    private int _slow;
    private readonly Dictionary<string, (double Sum, double Max)> _features = new();
    private readonly Dictionary<string, double> _kb = new();

    public int Frames => _mod.Count;

    public void Add(double modMs, double gapMs, IEnumerable<(string Id, double Ms)> features, IEnumerable<(string Id, double Kb)> kb)
    {
        _mod.Add(modMs);
        if (gapMs > _gapMax) _gapMax = gapMs;
        if (modMs > SessionStats.SlowModFrameMs) _slow++;
        foreach (var (id, ms) in features)
        {
            var totals = _features.TryGetValue(id, out var feature) ? feature : (0, 0);
            _features[id] = (totals.Sum + ms, Math.Max(totals.Max, ms));
        }
        foreach (var (id, k) in kb) _kb[id] = _kb.GetValueOrDefault(id) + k;
    }

    /// <summary>The rollup, then starts again. <paramref name="extra"/>: the process and simulator numbers the caller read.</summary>
    public FrameRollup Take(double seconds, (double HeapMb, double AllocatedMb, int Gen0, int Gen1, int Gen2, double GcPauseMs, long Held, int Searching) extra,
        (double SimMb, int Compiles, int Nodes, int OrphanNodes, bool Quiet, int DevChecks) engine)
    {
        _mod.Sort();
        double At(double percentile) => _mod.Count == 0 ? 0 : _mod[Math.Clamp((int)Math.Ceiling(percentile * _mod.Count) - 1, 0, _mod.Count - 1)];
        static double R(double value) => Math.Round(value, 2);
        var count = Math.Max(1, _mod.Count);
        var frameRollup = new FrameRollup(R(seconds), _mod.Count, R(At(percentile: 0.5)), R(At(percentile: 0.95)), R(_mod.Count == 0 ? 0 : _mod[^1]), R(_gapMax), _slow,
            _features.ToDictionary(entry => entry.Key, entry => R(entry.Value.Sum / count)), _features.ToDictionary(entry => entry.Key, entry => R(entry.Value.Max)),
            _kb.ToDictionary(entry => entry.Key, entry => R(entry.Value / count)),
            R(extra.HeapMb), R(extra.AllocatedMb), extra.Gen0, extra.Gen1, extra.Gen2, R(extra.GcPauseMs), extra.Held, extra.Searching,
            R(engine.SimMb), engine.Compiles, engine.Nodes, engine.OrphanNodes, engine.Quiet, engine.DevChecks);
        _mod.Clear();
        _gapMax = 0;
        _slow = 0;
        _features.Clear();
        _kb.Clear();
        return frameRollup;
    }
}

// ---------------------------------------------------------------- the writer

/// <summary>
/// Writes the dev folder (decision #108; dev mode only), all from a background thread so the game's thread only queues:
/// <list type="bullet">
/// <item><c>stream-yyyyMMdd-HHmmss.jsonl</c>: one per launch, one JSON line per event, <c>{"t": seconds since launch, "kind": ..., fields...}</c>;
/// the newest <see cref="KeepStreams"/> are kept;</item>
/// <item><c>state.json</c>: the latest state, replaced whole;</item>
/// <item><c>reports/{id}.json</c>: each report the game would have sent (kept until deleted by hand);</item>
/// <item><c>sessions.jsonl</c>: each play session's summary, one line each.</item>
/// </list>
/// The page writes <c>feedback.jsonl</c> itself; the game never touches it.
/// </summary>
public sealed class DevSink : IDisposable
{
    public const int KeepStreams = 20;
    public const string StatePath = "state.json", ReportsDir = "reports", SessionsPath = "sessions.jsonl", FeedbackPath = "feedback.jsonl";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };

    private readonly string _dir;
    private readonly Func<double> _seconds;
    private readonly ConcurrentQueue<(double T, DevEvent E)> _queue = new();
    private readonly ConcurrentQueue<(string Path, string Text, bool Append)> _files = new();
    private string? _state;
    private readonly Thread? _thread;
    private volatile bool _stopping;
    private readonly ManualResetEventSlim _stopped = new();
    private readonly Action<string>? _warn;
    private int _warned;

    public string Dir => _dir;
    public string StreamPath { get; }

    /// <param name="dir">The dev folder (created).</param>
    /// <param name="seconds">Seconds since launch, for each line's "t".</param>
    /// <param name="start">Start the writer thread (tests flush by hand instead).</param>
    public DevSink(string dir, Func<double> seconds, DateTime nowUtc, bool start = true, Action<string>? warn = null)
    {
        _dir = dir;
        _seconds = seconds;
        _warn = warn;
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, ReportsDir));
        StreamPath = Path.Combine(dir, $"stream-{nowUtc.ToLocalTime():yyyyMMdd-HHmmss}.jsonl");
        Rotate();
        if (!start) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SpireQoL dev stream", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>One event's line: <c>{"t":12.345,"kind":"search-done",...}</c>.</summary>
    public static string Line(double seconds, DevEvent devEvent)
    {
        var json = JsonSerializer.Serialize(devEvent, devEvent.GetType(), Options);
        var head = $"{{\"t\":{Math.Round(seconds, 3).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return json.Length <= 2 ? head + "}" : head + "," + json[1..];
    }

    public static string Json(object value, bool indented = false) => JsonSerializer.Serialize(value, value.GetType(), indented ? Indented : Options);

    /// <summary>Queues an event (any thread).</summary>
    public void Post(DevEvent devEvent) => _queue.Enqueue((_seconds(), devEvent));

    /// <summary>Sets the state the page reads (any thread); written on the writer's next pass.</summary>
    public void SetState(string json) => Interlocked.Exchange(ref _state, json);

    /// <summary>Queues a report's file, <c>reports/{id}.json</c> (any thread).</summary>
    public void SaveReport(string id, string json) => _files.Enqueue((Path.Combine(_dir, ReportsDir, Safe(id) + ".json"), json, false));

    /// <summary>Queues a session summary's line in <c>sessions.jsonl</c> (any thread).</summary>
    public void SaveSession(string json) => _files.Enqueue((Path.Combine(_dir, SessionsPath), json.ReplaceLineEndings(" ") + "\n", true));

    private static string Safe(string id) => string.Concat(id.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));

    private void Loop()
    {
        while (!_stopping)
        {
            Flush();
            Thread.Sleep(100);
        }
        Flush();
        _stopped.Set();
    }

    /// <summary>Writes what's queued and the state, now (the writer thread, or tests).</summary>
    public void Flush()
    {
        try
        {
            if (!_queue.IsEmpty)
            {
                using var streamWriter = new StreamWriter(new FileStream(StreamPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete));
                while (_queue.TryDequeue(out var item))
                {
                    string line;
                    try { line = Line(item.T, item.E); }
                    catch (Exception ex) { line = Line(item.T, new LogLine("WARN", $"[dev] couldn't write a {item.E.Kind} event: {ex.Message}")); }
                    streamWriter.Write(line);
                    streamWriter.Write('\n');
                }
            }
            while (_files.TryDequeue(out var pathText))
            {
                if (pathText.Append) File.AppendAllText(pathText.Path, pathText.Text);
                else File.WriteAllText(pathText.Path, pathText.Text);
            }
            if (Interlocked.Exchange(ref _state, null) is { } state)
            {
                var path = Path.Combine(_dir, StatePath);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, state);
                try { File.Move(tmp, path, overwrite: true); }
                // The page is reading it (Windows says so as "access denied" as often as "in use"): next pass.
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Interlocked.CompareExchange(ref _state, state, null); }
            }
        }
        catch (Exception ex)
        {
            Warn($"the dev folder couldn't be written ({ex.Message}); it keeps trying");
        }
    }

    /// <summary>Keeps the newest stream files (and this launch's).</summary>
    private void Rotate()
    {
        try
        {
            foreach (var old in Directory.EnumerateFiles(_dir, "stream-*.jsonl").OrderByDescending(enumerateFile => enumerateFile, StringComparer.Ordinal).Skip(KeepStreams - 1))
                File.Delete(old);
        }
        catch (Exception ex)
        {
            Warn($"old dev stream files couldn't be removed ({ex.Message})");
        }
    }

    private void Warn(string text)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0) _warn?.Invoke(text);
    }

    /// <summary>Stops the writer after writing everything queued (the game is closing).</summary>
    public void Dispose()
    {
        _stopping = true;
        if (_thread != null) _stopped.Wait(TimeSpan.FromSeconds(2));
        else Flush();
    }
}
