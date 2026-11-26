using System.Diagnostics;
using SpireQoL.Telemetry;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>Dev mode's performance trace (decision #258): finding dotnet-trace, its command line, starting and stopping it, the disk budget.</summary>
public class TraceRecorderTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 10, 30, 5, DateTimeKind.Local);

    /// <summary>A dotnet-trace that runs until it's told to stop (its Finish event) or killed.</summary>
    private sealed class FakeProcess : ITraceProcess
    {
        public bool Exited { get; set; }
        public bool StopsWhenAsked { get; init; } = true;
        public bool Killed { get; private set; }
        public bool HasExited => Exited;
        public bool WaitForExit(TimeSpan timeout) => Exited;
        public void Kill() { Killed = true; Exited = true; }
        public string LastLine { get; set; } = "";
        public void Dispose() { }
    }

    private sealed class Harness
    {
        public readonly List<ProcessStartInfo> Started = new();
        public readonly List<FakeProcess> Processes = new();
        public readonly List<DevEvent> Posted = new();
        public readonly List<string> Warnings = new();
        public readonly List<string> Finished = new();
        public readonly string Folder = Path.Combine(Path.GetTempPath(), "spireqol-trace-" + Guid.NewGuid().ToString("N"));
        public bool StopsWhenAsked = true;
        public TraceRecorder Recorder = null!;

        public Harness(string? tool = "C:/tools/dotnet-trace.exe", long budgetMb = TraceRecorder.DefaultBudgetMb)
        {
            Recorder = new TraceRecorder(Folder, processId: 4242, tool, startInfo =>
            {
                Started.Add(startInfo);
                var process = new FakeProcess { StopsWhenAsked = StopsWhenAsked };
                Processes.Add(process);
                return process;
            }, Posted.Add, Warnings.Add, id =>
            {
                Finished.Add(id);
                foreach (var process in Processes.Where(process => process.StopsWhenAsked)) process.Exited = true;
            }, () => Now, budgetMb, background: false);
        }
    }

    [Fact]
    public void TheToolIsFoundOnPathOrInTheGlobalTools()
    {
        // A root with no PATH separator in it (Linux's is ':', so a "C:" root would split there: GitHub runs these on Linux).
        var root = Path.Combine(Path.GetTempPath(), "trace-tool");
        var onPath = Path.Combine(root, "bin", "dotnet-trace.exe");
        var inTools = Path.Combine(root, "Users", "dev", ".dotnet", "tools", "dotnet-trace.exe");
        var pathVariable = string.Join(Path.PathSeparator, Path.Combine(root, "nothing"), Path.Combine(root, "bin"));
        Assert.Equal(onPath, TraceTool.Find(pathVariable, Path.Combine(root, "Users", "dev"), file => file == onPath || file == inTools, "dotnet-trace.exe"));
        Assert.Equal(inTools, TraceTool.Find(pathVariable, Path.Combine(root, "Users", "dev"), file => file == inTools, "dotnet-trace.exe"));
        Assert.Equal(inTools, TraceTool.Find(pathVariable: null, Path.Combine(root, "Users", "dev"), file => file == inTools, "dotnet-trace.exe"));
        Assert.Null(TraceTool.Find(pathVariable, Path.Combine(root, "Users", "dev"), file => false, "dotnet-trace.exe"));
    }

    [Fact]
    public void TheCommandAttachesToTheGameAndStopsOnItsOwnEvent()
    {
        var file = TraceCommand.FileFor(Path.Combine("C:", "dev", "traces"), Now, TracingLevel.Memory);
        Assert.Equal(Path.Combine("C:", "dev", "traces", "trace-20261124-103005-memory.nettrace"), file);
        var arguments = TraceCommand.Arguments(processId: 4242, TracingLevel.Memory, file);
        Assert.Equal(new[]
        {
            "collect", "--process-id", "4242",
            "--providers", "Microsoft-Windows-DotNETRuntime:0x8019:5,SpireQoL-Trace:0xFFFFFFFFFFFFFFFF:4",
            "--output", file, "--format", "NetTrace",
            "--stopping-event-provider-name", "SpireQoL-Trace", "--stopping-event-event-name", "Finish",
            "--stopping-event-payload-filter", "id:trace-20261124-103005-memory",
        }, arguments);
        // Full adds the CPU sampler; Memory hasn't it.
        Assert.Contains("Microsoft-DotNETCore-SampleProfiler", TraceCommand.Providers(TracingLevel.Full));
        Assert.DoesNotContain("SampleProfiler", TraceCommand.Providers(TracingLevel.Memory));
        Assert.Equal("", TraceCommand.Providers(TracingLevel.Off));
    }

    [Fact]
    public void TheStopEventIsTheOneTheCommandWaitsFor()
    {
        // dotnet-trace matches the stopping event by its provider's and event's names: the mod's event source must be named so.
        Assert.Equal(TraceCommand.StopProvider, System.Diagnostics.Tracing.EventSource.GetName(typeof(TraceStopSource)));
        Assert.NotNull(typeof(TraceStopSource).GetMethod(TraceCommand.StopEvent));
        Assert.Equal(TraceCommand.StopField, typeof(TraceStopSource).GetMethod(TraceCommand.StopEvent)!.GetParameters().Single().Name);
    }

    [Fact]
    public void TurningItOnTwiceRecordsOnce()
    {
        var harness = new Harness();
        harness.Recorder.Apply(TracingLevel.Memory);
        harness.Recorder.Reconcile();
        harness.Recorder.Apply(TracingLevel.Memory);
        harness.Recorder.Reconcile();
        harness.Recorder.Reconcile();
        Assert.Equal(1, harness.Recorder.Starts);
        Assert.Equal(TracingLevel.Memory, harness.Recorder.Recording);
        Assert.Equal("C:/tools/dotnet-trace.exe", harness.Started.Single().FileName);
        Assert.Contains("4242", harness.Started.Single().ArgumentList);
        var started = Assert.IsType<TraceStarted>(Assert.Single(harness.Posted));
        Assert.Equal("memory", started.Level);
    }

    [Fact]
    public void OffStopsItWithItsEventAndANewLevelStartsANewFile()
    {
        var harness = new Harness();
        harness.Recorder.Apply(TracingLevel.Memory);
        harness.Recorder.Reconcile();
        var first = harness.Recorder.Path!;
        harness.Recorder.Apply(TracingLevel.Full);
        harness.Recorder.Reconcile();
        Assert.Equal(new[] { TraceCommand.IdOf(first) }, harness.Finished);
        Assert.False(harness.Processes[0].Killed);
        Assert.Equal(2, harness.Recorder.Starts);
        Assert.Equal(TracingLevel.Full, harness.Recorder.Recording);
        Assert.EndsWith("-full.nettrace", harness.Recorder.Path);

        harness.Recorder.Apply(TracingLevel.Off);
        harness.Recorder.Reconcile();
        Assert.Equal(TracingLevel.Off, harness.Recorder.Recording);
        Assert.Null(harness.Recorder.Path);
        var stops = harness.Posted.OfType<TraceStopped>().ToList();
        Assert.Equal(new[] { ("memory", "setting"), ("full", "setting") }, stops.Select(stop => (stop.Level, stop.Why)));
    }

    [Fact]
    public void OneThatDoesntStopIsKilled()
    {
        var harness = new Harness { StopsWhenAsked = false };
        harness.Recorder.Apply(TracingLevel.Memory);
        harness.Recorder.Reconcile();
        harness.Recorder.Close(TimeSpan.Zero);
        Assert.True(harness.Processes.Single().Killed);
        var stop = harness.Posted.OfType<TraceStopped>().Single();
        Assert.StartsWith("exit", stop.Why);
        Assert.Contains("killed", stop.Why);
        // Closed: nothing starts again.
        harness.Recorder.Apply(TracingLevel.Full);
        harness.Recorder.Reconcile();
        Assert.Equal(1, harness.Recorder.Starts);
    }

    [Fact]
    public void WithoutTheToolNothingStartsAndTheRowSaysHowToInstallIt()
    {
        var harness = new Harness(tool: null);
        harness.Recorder.Apply(TracingLevel.Memory);
        Assert.Contains(TraceTool.InstallCommand, harness.Recorder.Problem);
        harness.Recorder.Reconcile();
        harness.Recorder.Reconcile();
        Assert.Empty(harness.Started);
        Assert.Single(harness.Warnings);
        Assert.Contains(TraceTool.InstallCommand, harness.Warnings.Single());
        harness.Recorder.Apply(TracingLevel.Off);
        Assert.Null(harness.Recorder.Problem);
    }

    [Fact]
    public void OneThatEndsByItselfIsNotStartedAgainUntilTheSettingChanges()
    {
        var harness = new Harness();
        harness.Recorder.Apply(TracingLevel.Memory);
        harness.Recorder.Reconcile();
        harness.Processes[0].Exited = true;
        harness.Processes[0].LastLine = "[ERROR] no such process";
        harness.Recorder.Reconcile();
        harness.Recorder.Reconcile();
        Assert.Equal(1, harness.Recorder.Starts);
        Assert.Contains("no such process", harness.Recorder.Problem);
        Assert.Equal("ended: [ERROR] no such process", harness.Posted.OfType<TraceStopped>().Single().Why);
        harness.Recorder.Apply(TracingLevel.Full);
        harness.Recorder.Reconcile();
        Assert.Equal(2, harness.Recorder.Starts);
    }

    [Fact]
    public void TheOldestTracesPastTheBudgetAreDeleted()
    {
        var folder = Path.Combine(Path.GetTempPath(), "spireqol-traces-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string Make(string name, int kilobytes)
            {
                var file = Path.Combine(folder, name);
                File.WriteAllBytes(file, new byte[kilobytes * 1024]);
                return file;
            }
            var oldest = Make("trace-20261120-100000-memory.nettrace", kilobytes: 400);
            var older = Make("trace-20261121-100000-full.nettrace", kilobytes: 400);
            var newer = Make("trace-20261122-100000-memory.nettrace", kilobytes: 400);
            var recording = Make("trace-20261123-100000-memory.nettrace", kilobytes: 300);
            var report = Make("trace-20261120-100000-memory.json", kilobytes: 1);
            var deleted = TraceFiles.Prune(folder, budgetBytes: 1024 * 1024, keep: recording);
            Assert.Equal(new[] { older, oldest }, deleted);
            Assert.True(File.Exists(newer) && File.Exists(recording) && File.Exists(report));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
