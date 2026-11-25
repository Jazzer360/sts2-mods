using Microsoft.Diagnostics.Tracing.Analysis;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using TraceLog = Microsoft.Diagnostics.Tracing.Etlx.TraceLog;

namespace TraceReport;

/// <summary>A stack, read once: each frame's method and owner (leaf first), who it's charged to, and the nearest frame of each owner.</summary>
internal sealed record StackInfo(string[] Methods, Owner[] FrameOwners, Owner Charged, Dictionary<Owner, string> NearestSite)
{
    public string Leaf => Methods.Length > 0 ? Methods[0] : "?";
    public Owner LeafOwner => FrameOwners.Length > 0 ? FrameOwners[0] : Owner.Runtime;
}

/// <summary>One collection: when (ms after the trace started), its generation, its pause and the heap before and after.</summary>
internal sealed record Collection(double AtMs, int Generation, double PauseMs, double HeapBeforeMb, double HeapAfterMb, string Reason, string Type);

/// <summary>What one trace holds, added up: allocations by type and call site, CPU samples by method, the collections.</summary>
internal sealed class TraceAnalysis
{
    public DateTime StartedUtc { get; private set; }
    public double Seconds { get; private set; }

    // Allocations: bytes by type and owner; by (owner, call site, type).
    public long AllocationTicks { get; private set; }
    public readonly Dictionary<string, double[]> TypeBytes = new();
    public readonly Dictionary<(Owner Owner, string Site, string Type), (double Bytes, int Ticks)> SiteBytes = new();

    // CPU samples (Full): busy samples by owner (charged and leaf), exclusive and inclusive by method; a thread waiting apart. The
    // sampler's own managed/external flag isn't used: a thread stopped for its sample often reads as external even in a managed loop.
    public int Samples { get; private set; }
    public int BusySamples { get; private set; }
    public readonly double[] ChargedSamples = new double[Owners.All.Length];
    public readonly double[] LeafSamples = new double[Owners.All.Length];
    public readonly Dictionary<string, (Owner Owner, int Samples)> Exclusive = new();
    public readonly Dictionary<string, (Owner Owner, int Samples)> Inclusive = new();
    public readonly Dictionary<string, (Owner Owner, int Samples)> Waiting = new();

    public readonly List<Collection> Collections = new();

    private readonly Dictionary<CallStackIndex, StackInfo> _stacks = new();
    private TraceLog _log = null!;

    /// <summary>Reads a .nettrace (converted to a TraceLog in the temp folder, deleted after).</summary>
    public static TraceAnalysis Read(string tracePath)
    {
        var analysis = new TraceAnalysis();
        var etlx = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(tracePath)}-{Guid.NewGuid():N}.etlx");
        try
        {
            TraceLog.CreateFromEventPipeDataFile(tracePath, etlx, new TraceLogOptions { ContinueOnError = true });
            using var log = new TraceLog(etlx);
            analysis.ReadLog(log);
        }
        finally
        {
            try { File.Delete(etlx); } catch (IOException) { }
        }
        return analysis;
    }

    private void ReadLog(TraceLog log)
    {
        _log = log;
        StartedUtc = log.SessionStartTime.ToUniversalTime();
        Seconds = log.SessionDuration.TotalSeconds;
        using var source = log.Events.GetSource();
        source.NeedLoadedDotNetRuntimes();
        source.Clr.GCAllocationTick += OnAllocationTick;
        var samples = new SampleProfilerTraceEventParser(source);
        samples.ThreadSample += OnSample;
        source.Process();
        foreach (var process in source.Processes())
        {
            if (process.LoadedDotNetRuntime() is not { } runtime) continue;
            foreach (var collection in runtime.GC.GCs)
            {
                Collections.Add(new Collection(collection.PauseStartRelativeMSec > 0 ? collection.PauseStartRelativeMSec : collection.StartRelativeMSec,
                    collection.Generation, collection.PauseDurationMSec, collection.HeapSizeBeforeMB, collection.HeapSizeAfterMB,
                    collection.Reason.ToString(), collection.Type.ToString()));
            }
        }
        Collections.Sort((first, second) => first.AtMs.CompareTo(second.AtMs));
    }

    private void OnAllocationTick(GCAllocationTickTraceData tick)
    {
        var bytes = tick.AllocationAmount64 > 0 ? tick.AllocationAmount64 : tick.AllocationAmount;
        if (bytes <= 0) return;
        AllocationTicks++;
        var type = string.IsNullOrEmpty(tick.TypeName) ? "?" : tick.TypeName;
        var stack = Stack(tick.CallStackIndex());
        if (!TypeBytes.TryGetValue(type, out var byOwner)) TypeBytes[type] = byOwner = new double[Owners.All.Length];
        byOwner[(int)stack.Charged] += bytes;
        var site = stack.NearestSite.GetValueOrDefault(stack.Charged, stack.Leaf);
        var key = (stack.Charged, site, type);
        var (total, ticks) = SiteBytes.GetValueOrDefault(key);
        SiteBytes[key] = (total + bytes, ticks + 1);
    }

    private void OnSample(ClrThreadSampleTraceData sample)
    {
        Samples++;
        var stack = Stack(sample.CallStackIndex());
        if (stack.Methods.Length == 0) return;
        if (Owners.Waits(stack.Leaf))
        {
            // A thread blocked (a wait, a sleep, a read): not CPU. Its last managed frame names it.
            var (owner, count) = Waiting.GetValueOrDefault(stack.Leaf, (stack.LeafOwner, 0));
            Waiting[stack.Leaf] = (owner, count + 1);
            return;
        }
        BusySamples++;
        ChargedSamples[(int)stack.Charged]++;
        LeafSamples[(int)stack.LeafOwner]++;
        var (exclusiveOwner, exclusive) = Exclusive.GetValueOrDefault(stack.Leaf, (stack.LeafOwner, 0));
        Exclusive[stack.Leaf] = (exclusiveOwner, exclusive + 1);
        var seen = new HashSet<string>();
        for (var i = 0; i < stack.Methods.Length; i++)
        {
            if (!seen.Add(stack.Methods[i])) continue; // a method recursing counts once
            var (inclusiveOwner, inclusive) = Inclusive.GetValueOrDefault(stack.Methods[i], (stack.FrameOwners[i], 0));
            Inclusive[stack.Methods[i]] = (inclusiveOwner, inclusive + 1);
        }
    }

    private StackInfo Stack(CallStackIndex index)
    {
        if (_stacks.TryGetValue(index, out var known)) return known;
        var methods = new List<string>();
        var frameOwners = new List<Owner>();
        var callStacks = _log.CallStacks;
        var codeAddresses = _log.CodeAddresses;
        for (var frame = index; frame != CallStackIndex.Invalid; frame = callStacks.Caller(frame))
        {
            var address = callStacks.CodeAddressIndex(frame);
            var methodIndex = codeAddresses.MethodIndex(address);
            var method = methodIndex != MethodIndex.Invalid ? codeAddresses.Methods.FullMethodName(methodIndex) : "?";
            var module = codeAddresses.ModuleFile(address)?.Name ?? "";
            methods.Add(method);
            frameOwners.Add(Owners.Of(method, module));
        }
        var nearest = new Dictionary<Owner, string>();
        for (var i = 0; i < methods.Count; i++) nearest.TryAdd(frameOwners[i], methods[i]);
        var stack = new StackInfo(methods.ToArray(), frameOwners.ToArray(), Owners.Charged(frameOwners), nearest);
        _stacks[index] = stack;
        return stack;
    }
}
