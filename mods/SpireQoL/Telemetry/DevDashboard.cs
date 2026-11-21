using System.Diagnostics;
using SpireQoL.Sim;
using Sts2Mods.Core.Diagnostics;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Telemetry;

/// <summary>
/// The dev dashboard's side in the game (decision #108; dev mode only, ModEntry.DevMode). Everything goes to files in the dev folder
/// (<c>%USERPROFILE%\SpireQoL-dev</c>), read by the page <c>tools/dashboard/index.html</c>; nothing listens inside the
/// game. Writes the event stream (DevSink: every DevEvent, the mod's log lines), a rollup of the frames every 250 ms (the mod's time
/// per frame and per feature, memory and collections, states held, searches running) and a state snapshot every 500 ms (where you
/// are, the session's counts so far, the forecast now with its spread). Reports and session summaries stay on the developer's
/// machine (ErrorReporter.Local; reports/ and sessions.jsonl): nothing from a dev game goes to Sentry, the developer fixes it here.
/// </summary>
public static class DevDashboard
{
    public const int StateFormat = 1;
    private const double RollupSeconds = 0.25, StateSeconds = 0.5;

    private static DevSink? _sink;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly FrameWindow Window = new();
    private static double _lastRollup, _lastState;
    private static (int G0, int G1, int G2, long Alloc, TimeSpan Pause) _gc;

    public static bool Running => _sink != null;

    /// <summary>
    /// The dev folder: <c>SpireQoL-dev</c> in the user's folder, or the hidden setting DevFolder. Not under AppData (beside the game's
    /// logs): Chrome and Edge refuse to open any folder there ("contains system files"), and under Program Files too.
    /// </summary>
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SpireQoL-dev");

    public static string Folder { get; private set; } = DefaultFolder;

    public static void Start(OverlayHost host, string? folder)
    {
        if (!ModEntry.DevMode || _sink != null) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(folder)) Folder = Environment.ExpandEnvironmentVariables(folder);
            var sink = new DevSink(Folder, () => Clock.Elapsed.TotalSeconds, DateTime.UtcNow, warn: text => ModEntry.Log.Warn("[dev] " + text));
            var tags = ErrorReporter.Instance?.Tags;
            sink.Post(new StreamStarted(DevEvents.Version, tags?.GetValueOrDefault("mod_version") ?? "?", tags?.GetValueOrDefault("game_version") ?? "?", DateTime.UtcNow, BuildInfo.Label));
            DevEvents.Posted += sink.Post;
            ModEntry.Log.Written += (level, text) => sink.Post(new LogLine(level, text));
            host.FrameMeasured += (modMs, gapMs) => Window.Add(modMs, gapMs, host.FeatureMs, host.FeatureKb);
            if (ErrorReporter.Instance is { } reporter)
            {
                reporter.Local = r =>
                {
                    sink.SaveReport(r.Id, DevSink.Json(r, indented: true));
                    sink.Post(new ReportSaved(r.Id, r.Kind, r.Title, r.Fingerprint));
                };
            }
            _gc = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalAllocatedBytes(), GC.GetTotalPauseDuration());
            host.AddLateFrame("dev-dashboard", Tick);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => sink.Dispose();
            _sink = sink;
            ModEntry.Log.Info($"[dev] dashboard stream: {sink.StreamPath} (open tools/dashboard/index.html and pick {Folder})");
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[dev] the dashboard stream couldn't start: {ex.Message}");
        }
    }

    /// <summary>A play session's summary, kept in sessions.jsonl instead of sent (dev mode).</summary>
    public static void SaveSession(SessionSummary s) => _sink?.SaveSession(s.ToJson());

    private static void Tick()
    {
        if (_sink is not { } sink) return;
        var now = Clock.Elapsed.TotalSeconds;
        if (now - _lastRollup >= RollupSeconds)
        {
            var seconds = _lastRollup == 0 ? RollupSeconds : now - _lastRollup;
            _lastRollup = now;
            var gc = (G0: GC.CollectionCount(0), G1: GC.CollectionCount(1), G2: GC.CollectionCount(2), Alloc: GC.GetTotalAllocatedBytes(), Pause: GC.GetTotalPauseDuration());
            sink.Post(Window.Take(seconds, (GC.GetTotalMemory(false) / 1048576.0, (gc.Alloc - _gc.Alloc) / 1048576.0, gc.G0 - _gc.G0, gc.G1 - _gc.G1,
                gc.G2 - _gc.G2, (gc.Pause - _gc.Pause).TotalMilliseconds, SimWork.HeldEverywhere, SimJob.Progress().Count)));
            _gc = gc;
        }
        if (now - _lastState >= StateSeconds)
        {
            _lastState = now;
            sink.SetState(DevSink.Json(State(sink)));
        }
    }

    private static object State(DevSink sink)
    {
        var tags = ErrorReporter.Instance?.Tags;
        return new
        {
            v = StateFormat,
            stream = DevEvents.Version,
            at = DateTime.UtcNow,
            seconds = Math.Round(Clock.Elapsed.TotalSeconds, 1),
            mod = tags?.GetValueOrDefault("mod_version") ?? "?",
            build = BuildInfo.Label,
            game = tags?.GetValueOrDefault("game_version") ?? "?",
            streamFile = Path.GetFileName(sink.StreamPath),
            where = SessionWatch.Where,
            session = SessionWatch.Live(),
            forecast = Forecast(),
        };
    }

    /// <summary>The turn forecast now (null when there's none or its result is on its way), with how the damage spreads over the rolls.</summary>
    private static object? Forecast()
    {
        try
        {
            if (Combat.TurnEndModel.Compute() is not { } f) return null;
            var o = f.Outcome;
            return new
            {
                taken = f.Taken,
                takenMin = f.TakenMin,
                dies = f.Dies,
                dieChance = o.Branches.Count == 0 ? 0 : o.YouDieChance().Low,
                blockGain = f.BlockGain,
                branches = o.Branches.Count,
                truncated = o.Truncated,
                capHit = o.CapHit,
                steps = o.Steps,
                copies = o.Copies,
                peakHeld = o.PeakHeld,
                unmodelled = o.UnmodelledReasons.ToList(),
                spread = o.Branches.GroupBy(b => b.Taken).OrderBy(g => g.Key)
                    .Select(g => new { taken = g.Key, weight = Math.Round(g.Sum(b => b.Weight), 6), died = g.Any(b => b.Died) }).Take(60).ToList(),
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }
}
