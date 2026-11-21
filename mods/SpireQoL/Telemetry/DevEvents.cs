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

    internal static string KindOf(Type type)
    {
        var name = type.Name;
        var text = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) text.Append('-');
            text.Append(char.ToLowerInvariant(name[i]));
        }
        return text.ToString();
    }
}

/// <summary>A fight began. <paramref name="Characters"/>: every player's character, yours first.</summary>
public sealed record CombatStarted(string Encounter, int Players, string Characters) : DevEvent;

/// <summary>The fight is over (won, lost, or left: save and quit).</summary>
public sealed record CombatEnded : DevEvent;

/// <summary>Your turn <paramref name="Turn"/> of the current fight began.</summary>
public sealed record TurnStarted(int Turn) : DevEvent;

/// <summary>
/// An audit compared a prediction with what happened. <paramref name="Audit"/>: one of <see cref="Audits"/>. <paramref name="Turn"/>:
/// your turn, for the per-turn audits (0 for the others). <paramref name="Detail"/>: what was checked (the card, the potion), or the
/// first issue's kind when it didn't match.
/// </summary>
public sealed record CheckDone(string Audit, int Turn, bool Matched, string? Detail) : DevEvent;

/// <summary>
/// An audit left a check (or part of one) undone, with why (decision #92: a skipped check is a gap, not a pass). A whole forecast
/// skipped posts only this; a partial skip (a teammate's part) posts this and the CheckDone that follows.
/// <paramref name="Expected"/>: a skip no model could close, which the session counts don't hold against the turn (still counted, as
/// "(expected)"): the play that ends the fight, whose state after it is gone by the time it finishes (the fight's cleanup runs first).
/// </summary>
public sealed record CheckSkipped(string Audit, int Turn, string Reason, string? Detail, bool Expected = false) : DevEvent;

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
/// The audits that post checks (decision #92, rule 1: every prediction is paired with its outcome, every skip counted). The per-turn
/// two decide how each turn of a fight is classified (SessionStats); the others are counted on their own, outside the turns.
/// </summary>
public static class Audits
{
    /// <summary>The enemy turn after your turn, against Incoming Damage, the Death Skull and the forecast behind them.</summary>
    public const string Forecast = "forecast";
    /// <summary>A card's play: its tabs, and the state after it.</summary>
    public const string Play = "play";
    /// <summary>Intent Forecast's next turn, each time the game rolls an enemy's move.</summary>
    public const string Intent = "intent";
    /// <summary>Potion tabs, after you drink a potion.</summary>
    public const string Potion = "potion";
    /// <summary>Run Reminders: event options, card rewards, shop prices and the card rules.</summary>
    public const string Reminders = "reminders";
    /// <summary>Rest Preview: HP after resting or Mend, and at the boss fight's start.</summary>
    public const string Rest = "rest";

    public static bool PerTurn(string audit) => audit is Forecast or Play;

    /// <summary>A check outside the turns' audits ran.</summary>
    public static void Done(string audit, bool matched, string? detail) => DevEvents.Post(new CheckDone(audit, Turn: 0, matched, detail));

    /// <summary>A check outside the turns' audits was left undone: say why (no names or amounts in the reason, so it adds up).</summary>
    public static void Skipped(string audit, string reason, string? detail = null) => DevEvents.Post(new CheckSkipped(audit, Turn: 0, reason, detail));
}

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
    internal static Action<Exception>? OnError { get; set; }
    private static int _errorSaid;

    public static void Post(DevEvent devEvent)
    {
        var handlers = Posted;
        if (handlers == null) return;
        try { handlers(devEvent); }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _errorSaid, 1) == 0) OnError?.Invoke(ex);
        }
    }
}
