using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;

namespace SpireQoL.Telemetry;

/// <summary>
/// How much a dev-mode performance trace records (decision #258). Off: nothing. Memory (a developer's default): the runtime's
/// collections (generation, pause, heap sizes) and its allocation ticks (about one per 100 KB allocated, each with its type and the
/// allocating stack). Full: Memory and CPU samples (every managed thread's stack about every millisecond, which stops the threads to
/// walk them: it costs more and writes far larger files).
/// </summary>
public enum TracingLevel
{
    Off,
    Memory,
    Full,
}

/// <summary>A performance trace started recording (decision #258): its level ("memory", "full") and its file.</summary>
public sealed record TraceStarted(string Level, string Path) : DevEvent;

/// <summary>
/// A performance trace stopped: its level, its file, why ("setting": the level was changed; "exit": the game closed; "budget": the
/// traces filled the disk budget; "ended": dotnet-trace stopped by itself, with its last line), and the file's size in MB.
/// </summary>
public sealed record TraceStopped(string Level, string Path, string Why, double Mb) : DevEvent;

/// <summary>Finding Microsoft's dotnet-trace (decision #258): on PATH, or where `dotnet tool install -g` puts it.</summary>
public static class TraceTool
{
    public const string InstallCommand = "dotnet tool install -g dotnet-trace";

    /// <summary>The tool's file name on this system.</summary>
    public static string FileName => OperatingSystem.IsWindows() ? "dotnet-trace.exe" : "dotnet-trace";

    /// <summary>The tool's path, or null when it isn't installed.</summary>
    /// <param name="pathVariable">The PATH environment variable (null: none).</param>
    /// <param name="userProfile">The user's folder: the global tools are in its <c>.dotnet\tools</c>.</param>
    /// <param name="exists">Whether a file exists (File.Exists; tests pass their own).</param>
    public static string? Find(string? pathVariable, string userProfile, Func<string, bool> exists, string fileName)
    {
        var folders = (pathVariable ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(folder => folder.Trim('"'))
            .Append(Path.Combine(userProfile, ".dotnet", "tools"));
        foreach (var folder in folders)
        {
            string candidate;
            try { candidate = Path.Combine(folder, fileName); }
            catch (ArgumentException) { continue; } // a PATH entry with characters a path can't have
            if (exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The installed tool, or null.</summary>
    public static string? Find() => Find(Environment.GetEnvironmentVariable("PATH"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), File.Exists, FileName);
}

/// <summary>The command line that records a trace of a process (decision #258), and the trace files' names.</summary>
public static class TraceCommand
{
    /// <summary>The mod's own event source, which every trace listens to: its Finish event stops that trace (dotnet-trace's stopping event).</summary>
    public const string StopProvider = "SpireQoL-Trace", StopEvent = "Finish", StopField = "id";

    /// <summary>
    /// The runtime's events every level records, at Verbose: GC (0x1: the collections, their pauses and heap sizes, and the allocation
    /// ticks, Verbose only), Loader (0x8) and JIT (0x10: the methods compiled while recording, so their frames are named even if the
    /// rundown at the end is cut short), Exception (0x8000).
    /// </summary>
    public const string RuntimeProvider = "Microsoft-Windows-DotNETRuntime:0x8019:5";

    /// <summary>The runtime's CPU sampler (Full only): every managed thread's stack about every millisecond.</summary>
    public const string SampleProvider = "Microsoft-DotNETCore-SampleProfiler";

    public const string Extension = ".nettrace";

    /// <summary>The providers a level records (none for Off).</summary>
    public static string Providers(TracingLevel level) => level switch
    {
        TracingLevel.Memory => $"{RuntimeProvider},{StopProvider}:0xFFFFFFFFFFFFFFFF:4",
        TracingLevel.Full => $"{RuntimeProvider},{SampleProvider},{StopProvider}:0xFFFFFFFFFFFFFFFF:4",
        _ => "",
    };

    /// <summary>A level's name in files and the dev stream ("memory").</summary>
    public static string Name(TracingLevel level) => level.ToString().ToLowerInvariant();

    /// <summary>A new trace's file: <c>trace-yyyyMMdd-HHmmss-level.nettrace</c> in the folder.</summary>
    public static string FileFor(string folder, DateTime nowLocal, TracingLevel level) =>
        Path.Combine(folder, $"trace-{nowLocal:yyyyMMdd-HHmmss}-{Name(level)}{Extension}");

    /// <summary>What a trace's Finish event carries to stop it and no other: its file's name without the extension.</summary>
    public static string IdOf(string file) => Path.GetFileNameWithoutExtension(file);

    /// <summary>dotnet-trace's arguments: attach to the process, record the level's providers into the file, stop on the file's Finish event.</summary>
    public static IReadOnlyList<string> Arguments(int processId, TracingLevel level, string file) => new[]
    {
        "collect",
        "--process-id", processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--providers", Providers(level),
        "--output", file,
        "--format", "NetTrace",
        "--stopping-event-provider-name", StopProvider,
        "--stopping-event-event-name", StopEvent,
        "--stopping-event-payload-filter", $"{StopField}:{IdOf(file)}",
    };
}

/// <summary>The trace files kept (decision #258): the newest up to a size budget.</summary>
public static class TraceFiles
{
    /// <summary>
    /// Deletes the oldest traces in <paramref name="folder"/> until the rest fit in <paramref name="budgetBytes"/> (newest first, by
    /// name: the name starts with the time). <paramref name="keep"/> (the one recording) is never deleted and counts first.
    /// </summary>
    /// <returns>The files deleted.</returns>
    public static List<string> Prune(string folder, long budgetBytes, string? keep)
    {
        var deleted = new List<string>();
        if (!Directory.Exists(folder)) return deleted;
        var files = Directory.EnumerateFiles(folder, "trace-*" + TraceCommand.Extension)
            .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal).ToList();
        long total = keep != null && File.Exists(keep) ? new FileInfo(keep).Length : 0;
        foreach (var file in files)
        {
            if (keep != null && string.Equals(Path.GetFullPath(file), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)) continue;
            total += new FileInfo(file).Length;
            if (total <= budgetBytes) continue;
            try
            {
                File.Delete(file);
                deleted.Add(file);
            }
            catch (IOException) { } // open in a reader: next time
            catch (UnauthorizedAccessException) { }
        }
        return deleted;
    }
}

/// <summary>A running dotnet-trace, as the recorder needs it (tests pass their own).</summary>
public interface ITraceProcess : IDisposable
{
    bool HasExited { get; }
    bool WaitForExit(TimeSpan timeout);
    void Kill();
    /// <summary>Its last line of output (stderr first), for a warning when it stopped by itself.</summary>
    string LastLine { get; }
}

/// <summary>
/// The dev-mode performance trace (decision #258): Microsoft's dotnet-trace attached to the game's own process, at the level the
/// setting asks for, writing a new file in the traces folder. Everything happens on its own background thread: the game's thread
/// only says which level it wants (<see cref="Apply"/>). Stopped by the mod's own Finish event (dotnet-trace's stopping event), so
/// the trace ends cleanly with its rundown (the names of every method's code); killed only when it doesn't stop in time. At the
/// game's exit <see cref="Close"/> stops it and waits a few seconds; a child process the game's crash leaves is ended by the
/// operating system (Windows: a job that closes with the game).
/// </summary>
public sealed class TraceRecorder : IDisposable
{
    /// <summary>How long a stop waits for dotnet-trace to write its rundown and end, before it's killed (the setting going off).</summary>
    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(30);

    /// <summary>How often the background thread checks the process and the disk budget.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(5);

    public const long DefaultBudgetMb = 2048;

    private readonly string _folder;
    private readonly int _processId;
    private readonly string? _tool;
    private readonly Func<ProcessStartInfo, ITraceProcess> _start;
    private readonly Action<DevEvent> _post;
    private readonly Action<string> _warn;
    private readonly Action<string> _finish;
    private readonly Func<DateTime> _now;
    private readonly long _budgetBytes;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread? _thread;
    private volatile bool _closing;

    private volatile int _wanted;
    private ITraceProcess? _process;
    // Read on the game's thread without the lock (a stop holds it while it waits): volatile.
    private volatile int _recording;
    private volatile string? _path;
    /// <summary>The level that last failed to start or ran out of budget (NoLevel: none): not tried again until the setting changes.</summary>
    private volatile int _failedAt = NoLevel;
    private const int NoLevel = -1;

    /// <summary>How many recordings were started (tests).</summary>
    public int Starts { get; private set; }

    /// <summary>The level being recorded now (Off when none).</summary>
    public TracingLevel Recording => (TracingLevel)_recording;

    /// <summary>The file being recorded (null when none).</summary>
    public string? Path => _path;

    /// <summary>Why the wanted level isn't recording (dotnet-trace missing, stopped, out of budget), for the settings row; null when it is or Off.</summary>
    public string? Problem { get; private set; }

    /// <param name="folder">Where the traces go (created).</param>
    /// <param name="processId">The process to trace: the game's own.</param>
    /// <param name="tool">dotnet-trace's path (TraceTool.Find), or null when it isn't installed.</param>
    /// <param name="start">Starts the process (TraceProcess.Start; tests pass their own).</param>
    /// <param name="post">Posts to the dev stream.</param>
    /// <param name="warn">The log's warning (said once per launch).</param>
    /// <param name="finish">Writes the Finish event with a trace's id (TraceStopSource; tests pass their own).</param>
    /// <param name="background">Run on a background thread (tests call <see cref="Reconcile"/> by hand instead).</param>
    public TraceRecorder(string folder, int processId, string? tool, Func<ProcessStartInfo, ITraceProcess> start, Action<DevEvent> post,
        Action<string> warn, Action<string> finish, Func<DateTime> now, long budgetMb = DefaultBudgetMb, bool background = true)
    {
        _folder = folder;
        _processId = processId;
        _tool = tool;
        _start = start;
        _post = post;
        _warn = warn;
        _finish = finish;
        _now = now;
        _budgetBytes = Math.Max(1, budgetMb) * 1024 * 1024;
        if (!background) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SpireQoL trace", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>The level the setting asks for (any thread; the background thread starts or stops the recording).</summary>
    public void Apply(TracingLevel level)
    {
        var before = (TracingLevel)Interlocked.Exchange(ref _wanted, (int)level);
        if (before != level) _failedAt = NoLevel; // a change of the setting tries again
        if (level == TracingLevel.Off) Problem = null;
        else if (_tool == null) Problem = $"dotnet-trace isn't installed: {TraceTool.InstallCommand}";
        _wake.Set();
    }

    private void Loop()
    {
        while (!_closing)
        {
            _wake.WaitOne(CheckEvery);
            if (_closing) break;
            try { Reconcile(); }
            catch (Exception ex) { _warn($"[dev] the performance trace failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Brings the recording in line with the wanted level: stops one that ended by itself or ran out of budget, stops one at another
    /// level, starts the wanted one. The background thread's pass (tests call it by hand).
    /// </summary>
    public void Reconcile()
    {
        lock (_gate)
        {
            if (_closing) return;
            var wanted = (TracingLevel)_wanted;
            if (_process is { HasExited: true } ended)
            {
                var line = ended.LastLine;
                Finished("ended" + (line.Length > 0 ? ": " + line : ""));
                _failedAt = (int)wanted;
                Problem = "dotnet-trace stopped" + (line.Length > 0 ? ": " + line : "");
                _warn($"[dev] dotnet-trace stopped by itself{(line.Length > 0 ? ": " + line : "")}");
            }
            if (_process != null && _path != null && File.Exists(_path) && new FileInfo(_path).Length > _budgetBytes)
            {
                StopNow("budget", StopWait);
                _failedAt = (int)wanted;
                Problem = $"the trace filled its budget ({_budgetBytes / 1048576} MB, TraceBudgetMb)";
                _warn($"[dev] the performance trace filled its budget ({_budgetBytes / 1048576} MB, the setting TraceBudgetMb); stopped");
            }
            if (_process != null && Recording != wanted) StopNow("setting", StopWait);
            if (_process == null && wanted != TracingLevel.Off && _failedAt != (int)wanted) StartNow(wanted);
            // The older traces past the budget, the one recording counted first.
            if (_path is { } recording) TraceFiles.Prune(_folder, _budgetBytes, keep: recording);
        }
    }

    private void StartNow(TracingLevel level)
    {
        if (_tool == null)
        {
            _failedAt = (int)level;
            Problem = $"dotnet-trace isn't installed: {TraceTool.InstallCommand}";
            _warn($"[dev] a performance trace was asked for, but dotnet-trace isn't installed (on PATH or in .dotnet\\tools): {TraceTool.InstallCommand}");
            return;
        }
        Directory.CreateDirectory(_folder);
        var file = TraceCommand.FileFor(_folder, _now(), level);
        TraceFiles.Prune(_folder, _budgetBytes, keep: null);
        var startInfo = new ProcessStartInfo(_tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in TraceCommand.Arguments(_processId, level, file)) startInfo.ArgumentList.Add(argument);
        try
        {
            _process = _start(startInfo);
        }
        catch (Exception ex)
        {
            _failedAt = (int)level;
            Problem = $"dotnet-trace couldn't start: {ex.Message}";
            _warn($"[dev] dotnet-trace couldn't start: {ex.Message}");
            return;
        }
        Starts++;
        _recording = (int)level;
        _path = file;
        Problem = null;
        _post(new TraceStarted(TraceCommand.Name(level), file));
    }

    /// <summary>Asks the recording to stop (its Finish event), waits for dotnet-trace to end, kills it if it doesn't in time.</summary>
    private void StopNow(string why, TimeSpan wait)
    {
        if (_process is not { } process || _path is not { } path) return;
        if (!process.HasExited)
        {
            try { _finish(TraceCommand.IdOf(path)); }
            catch (Exception ex) { _warn($"[dev] the performance trace's stop event failed: {ex.Message}"); }
            if (!process.WaitForExit(wait))
            {
                try { process.Kill(); }
                catch (Exception) { } // ended meanwhile
                why += " (killed: it didn't stop in time)";
            }
        }
        Finished(why);
    }

    private void Finished(string why)
    {
        if (_process == null || _path == null) return;
        var megabytes = File.Exists(_path) ? Math.Round(new FileInfo(_path).Length / 1048576.0, 1) : 0;
        _post(new TraceStopped(TraceCommand.Name(Recording), _path, why, megabytes));
        _process.Dispose();
        _process = null;
        _path = null;
        _recording = (int)TracingLevel.Off;
    }

    /// <summary>The game is closing: stops the recording and waits for its file, at most <paramref name="wait"/>; nothing starts after.</summary>
    public void Close(TimeSpan wait)
    {
        _closing = true;
        _wake.Set();
        lock (_gate) StopNow("exit", wait);
    }

    public void Dispose()
    {
        Close(TimeSpan.FromSeconds(10));
        _wake.Dispose();
    }
}

/// <summary>The mod's event source that stops a trace (decision #258): dotnet-trace ends the recording its Finish event names.</summary>
[EventSource(Name = TraceCommand.StopProvider)]
internal sealed class TraceStopSource : EventSource
{
    public static readonly TraceStopSource Instance = new();

    /// <summary>Stops the trace whose file's name (without the extension) is <paramref name="id"/>.</summary>
    [Event(1, Level = EventLevel.Informational)]
    public void Finish(string id) => WriteEvent(1, id);
}

/// <summary>
/// A dotnet-trace child process (decision #258): its output read on the runtime's threads (the last line kept), and on Windows in a
/// job that ends it when the game's process ends, so a crash leaves no recorder behind.
/// </summary>
public sealed class TraceProcess : ITraceProcess
{
    private readonly Process _process;
    private volatile string _lastOut = "", _lastError = "";

    private TraceProcess(Process process) => _process = process;

    public static ITraceProcess Start(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo };
        var traceProcess = new TraceProcess(process);
        process.OutputDataReceived += (_, line) => { if (!string.IsNullOrWhiteSpace(line.Data)) traceProcess._lastOut = line.Data.Trim(); };
        process.ErrorDataReceived += (_, line) => { if (!string.IsNullOrWhiteSpace(line.Data)) traceProcess._lastError = line.Data.Trim(); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        ExitJob.Add(process);
        return traceProcess;
    }

    public bool HasExited => _process.HasExited;

    public bool WaitForExit(TimeSpan timeout) => _process.WaitForExit(timeout);

    public void Kill() => _process.Kill(entireProcessTree: true);

    public string LastLine => _lastError.Length > 0 ? _lastError : _lastOut;

    public void Dispose() => _process.Dispose();

    /// <summary>Windows: a job object that kills its processes when its last handle closes (the game's process ending, however it ends).</summary>
    private static class ExitJob
    {
        private const int ExtendedLimitInformation = 9;
        private const uint KillOnJobClose = 0x2000;
        private static IntPtr _job;

        public static void Add(Process process)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                if (_job == IntPtr.Zero)
                {
                    var job = CreateJobObject(IntPtr.Zero, null);
                    var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
                    if (job == IntPtr.Zero || !SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())) return;
                    _job = job;
                }
                AssignProcessToJobObject(_job, process.Handle);
            }
            catch (Exception) { } // no job: dotnet-trace still ends when the game's process does (its stream closes)
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, uint length);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    }
}
