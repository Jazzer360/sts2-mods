namespace SpireQoL.Telemetry;

/// <summary>
/// One thing worth counting or watching during development (decision #108): a turn, a check, a search, a report. Pure C# (no game
/// types), so the simulator's worker threads post them too and the tests can feed them by hand. Each record's fields are what a
/// consumer needs; nothing here reaches the screen.
/// </summary>
public abstract record DevEvent
{
    /// <summary>The event's name in the stream ("search-done"): its record name, kebab-cased.</summary>
    public string Kind => KindOf(GetType());

    internal static string KindOf(Type t)
    {
        var n = t.Name;
        var b = new System.Text.StringBuilder(n.Length + 4);
        for (var i = 0; i < n.Length; i++)
        {
            if (char.IsUpper(n[i]) && i > 0) b.Append('-');
            b.Append(char.ToLowerInvariant(n[i]));
        }
        return b.ToString();
    }
}

/// <summary>A fight began. <paramref name="Characters"/>: every player's character, yours first.</summary>
public sealed record CombatStarted(string Encounter, int Players, string Characters) : DevEvent;

/// <summary>The fight is over (won, lost, or left: save and quit).</summary>
public sealed record CombatEnded : DevEvent;

/// <summary>Your turn <paramref name="Turn"/> of the current fight began.</summary>
public sealed record TurnStarted(int Turn) : DevEvent;

/// <summary>
/// An audit compared a prediction with what happened. <paramref name="Audit"/>: "forecast" (the enemy turn after your turn) or
/// "play" (a card's tabs and state after it). <paramref name="Detail"/>: the card, or the first issue's kind when it didn't match.
/// </summary>
public sealed record CheckDone(string Audit, int Turn, bool Matched, string? Detail) : DevEvent;

/// <summary>
/// An audit left a check (or part of one) undone, with why (decision #92: a skipped check is a gap, not a pass). A whole forecast
/// skipped posts only this; a partial skip (a teammate's part) posts this and the CheckDone that follows.
/// </summary>
public sealed record CheckSkipped(string Audit, int Turn, string Reason, string? Detail) : DevEvent;

/// <summary>
/// A simulator search ended (decision #107): finished, stopped at a cap, or cancelled (the board changed). <paramref name="Context"/>
/// is SimJob.Context when it started: the fight and your turn it belongs to (0 outside your turn).
/// </summary>
public sealed record SearchDone(string Label, long Context, int Steps, long Copies, long PeakHeld, double Ms, bool Background,
    string? Cap, bool Cancelled) : DevEvent
{
    /// <summary>The fight (SessionWatch's count) the search's board was in.</summary>
    public int Combat => (int)(Context >> 32);
    /// <summary>Your turn the search's board was in (0: not your turn).</summary>
    public int Turn => (int)(Context & 0xffffffff);
    public static long ContextOf(int combat, int turn) => ((long)combat << 32) | (uint)turn;
}

/// <summary>The game's thread waited for simulations (SimWait): how long, and whether some were left to the background.</summary>
public sealed record FrameWait(double Ms, bool Missed) : DevEvent;

/// <summary>
/// A spell of your turn with searches still running (the progress bar under End Turn) is over: how long, from its first frame to
/// the first frame with none. The delay you feel when a board doesn't settle within its frame.
/// </summary>
public sealed record Unsettled(double Ms) : DevEvent;

/// <summary>A report went to Sentry. <paramref name="What"/>: "crash", or the audit of a mismatch ("proc", "forecast", "perf"...).</summary>
public sealed record ReportSent(string What, string EventId) : DevEvent;

/// <summary>
/// The stream (decision #108): audits, the simulator and the reporter post here; the session counter (and the dev dashboard's file,
/// later) read it. Subscribers run on the poster's thread (a worker's, for searches), so they must be quick and thread-safe.
/// </summary>
public static class DevEvents
{
    /// <summary>The stream's format; bumped when an event's fields change meaning, so a reader can tell old files from new.</summary>
    public const int Version = 1;

    public static event Action<DevEvent>? Posted;

    /// <summary>A subscriber threw: said once (the stream keeps going; a counter missing a few events is no reason to stop playing).</summary>
    public static Action<Exception>? OnError;
    private static int _errorSaid;

    public static void Post(DevEvent e)
    {
        var h = Posted;
        if (h == null) return;
        try { h(e); }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _errorSaid, 1) == 0) OnError?.Invoke(ex);
        }
    }
}
