using System.Diagnostics;
using SpireQoL.Combat;
using SpireQoL.Sim;
using Sts2Mods.Core.Diagnostics;
using Sts2Mods.Core.Game;
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
    private static double _lastSample;
    /// <summary>The performance trace (decision #258): dotnet-trace attached to the game, at the setting's level.</summary>
    private static TraceRecorder? _tracer;

    // ---- steady moments (decision #177, replacing #176's quiet ones): nothing the mod reads changed for a second, when no feature
    // should allocate anything. The rollups' and the sampler's "quiet" are these.
    private static readonly SteadyWatch Steady = new(new[] { "dev-dashboard", OverlayHost.DevChecksId });
    /// <summary>The watch's own entry: what working out the frame's key allocated (on the frame before), so it reports itself.</summary>
    private const string WatchId = "steady-watch";
    private static readonly List<(string Id, double Kb)> FrameKb = new();
    private static double _watchKb;
    private static int _unfocusedFrames;
    private static bool _quietAllWindow = true;
    private static int _checksAtRollup, _checksAtFrame;

    /// <summary>
    /// The frame's key: what the mod reads. The board probe (it moves when anything a prediction reads does), a fight or not, the settings,
    /// anything held, searches running, the game's screen and the control under the mouse (a hover changes what's shown; the mouse moving
    /// over nothing doesn't). In the background (another window in front) the game runs at 30 fps and nobody is watching: never steady.
    /// </summary>
    private static int FrameKey()
    {
        if (!Godot.DisplayServer.WindowIsFocused()) return unchecked(++_unfocusedFrames);
        var hash = new HashCode();
        hash.Add(BoardProbe.ChangedAtMs);
        hash.Add(Sts2State.InCombat);
        hash.Add(QolSettings.Revision);
        hash.Add(Godot.Input.IsAnythingPressed());
        hash.Add(SimWork.HeldEverywhere > 0);
        try
        {
            var screen = MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.GetCurrentScreen();
            hash.Add(screen == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(screen));
            var hovered = (Godot.Engine.GetMainLoop() as Godot.SceneTree)?.Root.GuiGetHoveredControl();
            hash.Add(hovered == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(hovered));
        }
        catch
        {
            hash.Add(-1);
        }
        return hash.ToHashCode();
    }

    /// <summary>Every frame: the frame's key and each feature's memory to the steady watch.</summary>
    private static void WatchSteady(OverlayHost host, double gapMs)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var key = FrameKey();
        var devCheck = ReuseCheck.ChecksRun != _checksAtFrame;
        _checksAtFrame = ReuseCheck.ChecksRun;
        var where = Sts2State.InCombat ? "fight" : MegaCrit.Sts2.Core.Runs.RunManager.Instance is { IsInProgress: true } ? "run" : "menu";
        FrameKb.Clear();
        var featureKb = host.FeatureKb;
        for (var i = 0; i < featureKb.Count; i++) FrameKb.Add(featureKb[i]);
        FrameKb.Add((WatchId, _watchKb));
        Steady.Frame(key, Clock.Elapsed.TotalSeconds, gapMs / 1000, where, devCheck, FrameKb);
        var quiet = Steady.Steady;
        _watchKb = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0;
        if (!quiet) _quietAllWindow = false;
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

    /// <summary>How long the game's exit waits for the performance trace to write its rundown and end (then it's killed).</summary>
    private static readonly TimeSpan ExitTraceWait = TimeSpan.FromSeconds(10);

    /// <summary>How often the steady sample is written (seconds).</summary>
    private const double SampleSeconds = 10;

    /// <summary>The traces' folder: <c>traces</c> in the dev folder.</summary>
    public static string TraceFolder => Path.Combine(Folder, "traces");

    /// <summary>
    /// The performance trace's level changed in the settings menu (decision #258): it starts or stops now, on its own thread. Nothing
    /// outside a developer's game (the recorder only exists there).
    /// </summary>
    public static void Trace(TracingLevel level) => _tracer?.Apply(level);

    /// <summary>What the settings row says about the trace: recording (its file), or why not (dotnet-trace missing); null outside dev mode.</summary>
    public static string? TraceStatus()
    {
        if (_tracer is not { } tracer) return null;
        if (tracer.Problem is { } problem) return problem;
        return tracer.Path is { } path ? $"Recording {Path.GetFileName(path)}." : null;
    }

    /// <summary>
    /// The dev folder: <c>SpireQoL-dev</c> in the user's folder, or the hidden setting DevFolder. Not under AppData (beside the game's
    /// logs): Chrome and Edge refuse to open any folder there ("contains system files"), and under Program Files too.
    /// </summary>
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SpireQoL-dev");

    public static string Folder { get; private set; } = DefaultFolder;

    /// <summary>The mod's timed work that isn't a feature, by the id its frame times are kept under (decision #185: the dashboard's names).</summary>
    private static readonly Dictionary<string, string> OtherNames = new()
    {
        ["widgets"] = "Widget layout",
        ["settings-menu"] = "Settings menu",
        ["card-tabs"] = "Card tags (drawing)",
        ["settings-save"] = "Saving settings",
        ["turn-forecast"] = "Turn forecast",
        ["draw-pile-memory"] = "Draw pile memory",
        ["dev-dashboard"] = "Dev dashboard",
        [OverlayHost.DevChecksId] = "Dev checks",
        [WatchId] = "Steady watch",
    };

    /// <param name="featureNames">Each feature's id and display name: the dashboard shows the names (decision #185).</param>
    /// <param name="settings">The dev folder (DevFolder), the performance trace's level and its disk budget (TraceLevel, TraceBudgetMb).</param>
    public static void Start(OverlayHost host, QolSettings settings, IReadOnlyDictionary<string, string> featureNames)
    {
        if (!ModEntry.DevMode || _sink != null) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.DevFolder)) Folder = Environment.ExpandEnvironmentVariables(settings.DevFolder);
            var sink = new DevSink(Folder, () => Clock.Elapsed.TotalSeconds, DateTime.UtcNow, warn: text => ModEntry.Log.Warn("[dev] " + text));
            var tags = ErrorReporter.Instance?.Tags;
            sink.Post(new StreamStarted(DevEvents.Version, tags?.GetValueOrDefault("mod_version") ?? "?", tags?.GetValueOrDefault("game_version") ?? "?", DateTime.UtcNow, BuildInfo.Label));
            sink.Post(FeatureNames.Of(featureNames, OtherNames));
            DevEvents.Posted += sink.Post;
            ModEntry.Log.Written += (level, text) => sink.Post(new LogLine(level, text));
            host.FrameMeasured += (modMs, gapMs) =>
            {
                CountCompiles();
                WatchSteady(host, gapMs);
                if (!_focused) return;
                if (_framesToSkip > 0) _framesToSkip--; // the first frame back in front is the switch's (decision #238)
                else Window.Add(modMs, gapMs, host.FeatureMs, host.FeatureKb);
            };
            // Who allocates and who uses the CPU, with stacks: dotnet-trace attached to the game, at the setting's level (decision #258).
            var tracer = new TraceRecorder(TraceFolder, Environment.ProcessId, TraceTool.Find(), TraceProcess.Start, sink.Post,
                text => ModEntry.Log.Warn(text), id => TraceStopSource.Instance.Finish(id), () => DateTime.Now, settings.TraceBudgetMb);
            tracer.Apply(settings.TraceLevel);
            _tracer = tracer;
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
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                // The trace first: it ends with its file's rundown, and its stop goes to the stream before the stream closes.
                tracer.Close(ExitTraceWait);
                sink.Dispose();
            };
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

    /// <summary>The game's window is in front (decision #179): while it isn't, nothing about frames is written.</summary>
    private static bool _focused = true;

    /// <summary>Frames still to leave out after the window came back to the front (decision #238): the first one is the switch's, not play's.</summary>
    private static int _framesToSkip;

    /// <summary>
    /// The game's window went to the background or came back (decision #179). In the background the game runs at 30 fps and nobody is
    /// playing: its frames would only blur the charts, so no rollups or samples are written (the performance trace keeps recording). The frames on
    /// either side of the switch are the switch's (decision #238): the ones since the last rollup are dropped as it goes, and the first one
    /// back is left out. Back in front, every running count starts from now, so the first rollup isn't the whole time away in one.
    /// </summary>
    private static void FocusChanged(DevSink sink, bool focused, double now)
    {
        _focused = focused;
        sink.Post(new FocusChanged(focused));
        Window.Clear();
        if (!focused) return;
        _framesToSkip = 1;
        _lastRollup = now;
        _gc = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalAllocatedBytes(), GC.GetTotalPauseDuration());
        _simAllocated = SimJob.AllocatedBytes;
        _compilesInWindow = 0;
        _checksAtRollup = ReuseCheck.ChecksRun;
        _quietAllWindow = true;
        _lastSample = now;
    }

    private static void Tick()
    {
        if (_sink is not { } sink) return;
        var now = Clock.Elapsed.TotalSeconds;
        var focused = Godot.DisplayServer.WindowIsFocused();
        if (focused != _focused) FocusChanged(sink, focused, now);
        if (focused && now - _lastRollup >= RollupSeconds)
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
            if (now - _lastSample >= SampleSeconds)
            {
                // What allocated while nothing changed: every feature, however little; each one the first time in the log too.
                var (steady, firstSeen) = Steady.Take();
                sink.Post(steady);
                foreach (var garbage in firstSeen) ModEntry.Log.Detail(SteadyWatch.Describe(garbage, steady.Steady.GetValueOrDefault(garbage.Where)?.Frames ?? 0));
                _lastSample = now;
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
            trace = _tracer is { } tracer
                ? new { level = TraceCommand.Name(tracer.Recording), file = tracer.Path is { } path ? Path.GetFileName(path) : null, problem = tracer.Problem }
                : null,
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
