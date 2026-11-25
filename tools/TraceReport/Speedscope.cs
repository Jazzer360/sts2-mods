using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.Stacks.Formats;

namespace TraceReport;

/// <summary>A Full trace's CPU samples as a speedscope file (per thread, managed frames), as dotnet-trace's own convert writes it.</summary>
internal static class Speedscope
{
    public static void Write(string tracePath, string outputPath)
    {
        var etlx = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(tracePath)}-{Guid.NewGuid():N}.etlx");
        try
        {
            TraceLog.CreateFromEventPipeDataFile(tracePath, etlx, new TraceLogOptions { ContinueOnError = true });
            using var log = new TraceLog(etlx);
            using var symbols = new SymbolReader(TextWriter.Null) { SymbolPath = "" };
            var stackSource = new MutableTraceEventStackSource(log) { OnlyManagedCodeStacks = true };
            new SampleProfilerThreadTimeComputer(log, symbols).GenerateThreadTimeStacks(stackSource);
            SpeedScopeStackSourceWriter.WriteStackViewAsJson(stackSource, outputPath);
        }
        finally
        {
            try { File.Delete(etlx); } catch (IOException) { }
        }
    }
}

/// <summary>
/// Records this program's own process with a Full trace's providers (no dotnet-trace needed), while it allocates and works: checks
/// the reading end to end on a machine without the game.
/// </summary>
internal static class SelfRecord
{
    public static string Run(string folder, TimeSpan length)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"trace-{DateTime.Now:yyyyMMdd-HHmmss}-full.nettrace");
        var providers = new[]
        {
            new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, 0x8019),
            new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
        };
        var client = new DiagnosticsClient(Environment.ProcessId);
        using var session = client.StartEventPipeSession(providers, requestRundown: true);
        var copy = Task.Run(() =>
        {
            using var file = File.Create(path);
            session.EventStream.CopyTo(file);
        });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var kept = new List<string>();
        while (clock.Elapsed < length)
        {
            Work.Allocate(kept);
            Work.Spin();
        }
        session.Stop();
        copy.Wait(TimeSpan.FromSeconds(30));
        return path;
    }

    private static class Work
    {
        public static void Allocate(List<string> kept)
        {
            for (var i = 0; i < 20_000; i++) kept.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + kept.Count);
            if (kept.Count > 200_000) kept.Clear();
        }

        public static double Spin()
        {
            double sum = 0;
            for (var i = 1; i < 2_000_000; i++) sum += Math.Sqrt(i);
            return sum;
        }
    }
}
