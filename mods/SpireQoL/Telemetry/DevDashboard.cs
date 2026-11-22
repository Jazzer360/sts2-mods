using System.Diagnostics;
using SpireQoL.Combat;
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
    private static long _simAllocated;
    private static AllocationSampler? _sampler;
    private static double _lastSample, _quietSecondsInSample;

    // ---- quiet moments (decision #176): no input and no change on the board for QuietSeconds, when the mod should allocate nothing.
    private const double QuietSeconds = 2;
    private static Godot.Vector2I _lastMouse;
    private static ulong _lastInputMs;
    private static bool _quietAllWindow = true;
    private static int _checksAtRollup;

    /// <summary>Every frame: input (the mouse moved, anything held), the game's window not in front, and the board probe changing end a quiet moment.</summary>
    private static void WatchQuiet(double gapMs)
    {
        var now = Godot.Time.GetTicksMsec();
        var mouse = Godot.DisplayServer.MouseGetPosition();
        // In the background (another window focused) the game runs at 30 fps and nobody is watching: never quiet, and quiet starts over
        // only once it's back in front.
        if (mouse != _lastMouse || Godot.Input.IsAnythingPressed() || !Godot.DisplayServer.WindowIsFocused())
        {
            _lastMouse = mouse;
            _lastInputMs = now;
        }
        var settled = Math.Max(_lastInputMs, BoardProbe.ChangedAtMs);
        var quiet = now - settled >= QuietSeconds * 1000;
        _sampler?.SetQuiet(quiet);
        if (quiet) _quietSecondsInSample += gapMs / 1000;
        else _quietAllWindow = false;
    }
    /// <summary>The engine's shader compiles: its running total at the last frame, those in this rollup, those since the frame before.</summary>
    private static long _compilesTotal;
    private static int _compilesInWindow, _compilesLastFrame;

    private static readonly Godot.Performance.Monitor[] CompileMonitors =
    {
        Godot.Performance.Monitor.PipelineCompilationsCanvas, Godot.Performance.Monitor.PipelineCompilationsMesh,
        Godot.Performance.Monitor.PipelineCompilationsSurface, Godot.Performance.Monitor.PipelineCompilationsDraw,
        Godot.Performance.Monitor.PipelineCompilationsSpecialization,
    };

    /// <summary>
    /// The engine's shader compiles since the frame before (its pipeline compilations, a running total: the difference; a total that
    /// went down started over, so all of it is new). Read once a frame, dev mode only: five reads of the engine's counters.
    /// </summary>
    private static void CountCompiles()
    {
        long total = 0;
        foreach (var monitor in CompileMonitors) total += (long)Godot.Performance.GetMonitor(monitor);
        _compilesLastFrame = (int)(total >= _compilesTotal ? total - _compilesTotal : total);
        _compilesTotal = total;
        _compilesInWindow += _compilesLastFrame;
    }

    public static bool Running => _sink != null;

    /// <summary>How often the allocation sample is written (seconds).</summary>
    private const double SampleSeconds = 10;

    private static Dictionary<string, double> Rounded(Dictionary<string, double> kilobytes) => kilobytes.ToDictionary(entry => entry.Key, entry => Math.Round(entry.Value, 1));

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
            host.FrameMeasured += (modMs, gapMs) =>
            {
                CountCompiles();
                WatchQuiet(gapMs);
                Window.Add(modMs, gapMs, host.FeatureMs, host.FeatureKb);
            };
            // What the garbage is made of: the runtime's allocation ticks, by type (dev mode only).
            try { _sampler = new AllocationSampler(AllocationSampler.CurrentOsThread()); }
            catch (Exception ex) { ModEntry.Log.Warn($"[dev] the allocation sampler couldn't start: {ex.Message}"); }
            host.StallNote = () => _compilesLastFrame > 0 ? $"{_compilesLastFrame} shader compiles" : null;
            if (ErrorReporter.Instance is { } reporter)
            {
                reporter.Local = localReport =>
                {
                    sink.SaveReport(localReport.Id, DevSink.Json(localReport, indented: true));
                    sink.Post(new ReportSaved(localReport.Id, localReport.Kind, localReport.Title, localReport.Fingerprint));
                };
                // The reporter's own start-up line says Sentry (it starts before dev mode takes its reports over).
                ModEntry.Log.Info($"[dev] error reports kept in {Folder}, none sent to Sentry");
            }
            _gc = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalAllocatedBytes(), GC.GetTotalPauseDuration());
            _simAllocated = SimJob.AllocatedBytes;
            CountCompiles();
            _compilesInWindow = 0;
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
    public static void SaveSession(SessionSummary sessionSummary) => _sink?.SaveSession(sessionSummary.ToJson());

    private static void Tick()
    {
        if (_sink is not { } sink) return;
        var now = Clock.Elapsed.TotalSeconds;
        if (now - _lastRollup >= RollupSeconds)
        {
            var seconds = _lastRollup == 0 ? RollupSeconds : now - _lastRollup;
            _lastRollup = now;
            var collections = (G0: GC.CollectionCount(0), G1: GC.CollectionCount(1), G2: GC.CollectionCount(2), Alloc: GC.GetTotalAllocatedBytes(), Pause: GC.GetTotalPauseDuration());
            sink.Post(Window.Take(seconds, (GC.GetTotalMemory(false) / 1048576.0, (collections.Alloc - _gc.Alloc) / 1048576.0, collections.G0 - _gc.G0, collections.G1 - _gc.G1,
                collections.G2 - _gc.G2, (collections.Pause - _gc.Pause).TotalMilliseconds, SimWork.HeldEverywhere, SimJob.Progress().Count),
                ((SimJob.AllocatedBytes - _simAllocated) / 1048576.0, _compilesInWindow, (int)Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectNodeCount),
                    (int)Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectOrphanNodeCount), _quietAllWindow, ReuseCheck.ChecksRun - _checksAtRollup)));
            _quietAllWindow = true;
            _checksAtRollup = ReuseCheck.ChecksRun;
            _gc = collections;
            _simAllocated = SimJob.AllocatedBytes;
            if (_sampler != null && now - _lastSample >= SampleSeconds)
            {
                var (quietKb, activeKb) = _sampler.Take();
                sink.Post(new AllocationSample(Math.Round(now - _lastSample, 2), Math.Round(_quietSecondsInSample, 2), Rounded(quietKb), Rounded(activeKb)));
                _lastSample = now;
                _quietSecondsInSample = 0;
            }
            _compilesInWindow = 0;
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
            if (Combat.TurnEndModel.Compute() is not { } forecast) return null;
            var outcome = forecast.Outcome;
            return new
            {
                taken = forecast.Taken,
                takenMin = forecast.TakenMin,
                dies = forecast.Dies,
                dieChance = outcome.Branches.Count == 0 ? 0 : outcome.YouDieChance().Low,
                blockGain = forecast.BlockGain,
                branches = outcome.Branches.Count,
                truncated = outcome.Truncated,
                capHit = outcome.CapHit,
                steps = outcome.Steps,
                copies = outcome.Copies,
                peakHeld = outcome.PeakHeld,
                unmodelled = outcome.UnmodelledReasons.ToList(),
                spread = outcome.Branches.GroupBy(branch => branch.Taken).OrderBy(sameTaken => sameTaken.Key)
                    .Select(sameTaken => new { taken = sameTaken.Key, weight = Math.Round(sameTaken.Sum(branch => branch.Weight), 6), died = sameTaken.Any(branch => branch.Died) }).Take(60).ToList(),
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }
}
