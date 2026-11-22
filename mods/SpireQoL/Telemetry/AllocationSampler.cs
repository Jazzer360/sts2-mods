using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;

namespace SpireQoL.Telemetry;

/// <summary>
/// What the garbage is made of (decision #176; dev mode only): the .NET runtime reports an allocation tick about every 100 KB allocated
/// (GCAllocationTick, with the type of the object that crossed it), so the types seen, weighted by the amount each tick stands for,
/// are a sample of what every thread allocates. Each is counted as the game's thread's or another's, and as quiet (DevDashboard.Quiet:
/// no input and no board change for a while, when nothing should be allocating) or active. Taken every few seconds into an
/// AllocationSample for the dev stream. The ticks arrive on the runtime's own thread a little late; a tick's quiet or active is told
/// from its own time.
/// </summary>
public sealed class AllocationSampler : EventListener
{
    private const string RuntimeSource = "Microsoft-Windows-DotNETRuntime";
    /// <summary>The runtime's GC keyword: allocation ticks among its events.</summary>
    private const EventKeywords GcKeyword = (EventKeywords)0x1;

    private readonly object _gate = new();
    private Dictionary<string, double> _quietKb = new(), _activeKb = new();
    private readonly long _gameThread;

    /// <summary>When the current quiet stretch began (UTC; MaxValue while not quiet), set by the dashboard every frame.</summary>
    private long _quietSinceTicks = DateTime.MaxValue.Ticks;

    /// <param name="gameThread">The game's thread as the operating system numbers it (0: unknown, every tick counted as another's).</param>
    public AllocationSampler(long gameThread) => _gameThread = gameThread;

    /// <summary>The operating system's number for the calling thread (Windows; 0 elsewhere, where the split by thread is left out).</summary>
    public static long CurrentOsThread()
    {
        try { return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? GetCurrentThreadId() : 0; }
        catch { return 0; }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>Quiet from now (true) or not any more (false): the dashboard's per-frame call.</summary>
    public void SetQuiet(bool quiet)
    {
        var since = Interlocked.Read(ref _quietSinceTicks);
        if (quiet && since == DateTime.MaxValue.Ticks) Interlocked.Exchange(ref _quietSinceTicks, DateTime.UtcNow.Ticks);
        else if (!quiet && since != DateTime.MaxValue.Ticks) Interlocked.Exchange(ref _quietSinceTicks, DateTime.MaxValue.Ticks);
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == RuntimeSource) EnableEvents(eventSource, EventLevel.Verbose, GcKeyword);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName == null || !eventData.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal) || eventData.Payload == null) return;
        string? type = null;
        double bytes = 0;
        for (var i = 0; i < eventData.Payload.Count; i++)
        {
            switch (eventData.PayloadNames?[i])
            {
                case "TypeName": type = eventData.Payload[i] as string; break;
                case "AllocationAmount64": bytes = Convert.ToDouble(eventData.Payload[i]); break;
                case "AllocationAmount" when bytes == 0: bytes = Convert.ToDouble(eventData.Payload[i]); break;
            }
        }
        if (string.IsNullOrEmpty(type) || bytes <= 0) return;
        var thread = _gameThread != 0 && eventData.OSThreadId == _gameThread ? "game" : "other";
        var quiet = eventData.TimeStamp.ToUniversalTime().Ticks >= Interlocked.Read(ref _quietSinceTicks);
        var key = thread + "|" + type;
        lock (_gate)
        {
            var into = quiet ? _quietKb : _activeKb;
            into[key] = into.GetValueOrDefault(key) + bytes / 1024;
        }
    }

    /// <summary>What was sampled since the last call, quiet and active (KB by "thread|type"), and starts again.</summary>
    public (Dictionary<string, double> Quiet, Dictionary<string, double> Active) Take()
    {
        lock (_gate)
        {
            var taken = (_quietKb, _activeKb);
            _quietKb = new();
            _activeKb = new();
            return taken;
        }
    }
}
