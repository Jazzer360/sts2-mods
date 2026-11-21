using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpireQoL.Telemetry;

/// <summary>
/// One play session's counts (decision #108), from the event stream: turns and how each went, the audits' checks and why some were
/// skipped, the simulator's searches, the frame's waits and the mod's frame time. Sent once per session as a summary (SessionWatch),
/// so how often a cap or a mismatch comes up can be weighed against how often it could have.
///
/// A turn is classified when the next fight starts (or when the summary is taken: a check can come a frame or two after its fight
/// ended, for the killing play), worst first:
/// <list type="bullet">
/// <item>mismatched: some check of the turn (its plays, or the enemy turn after it) didn't match;</item>
/// <item>capped: some search for the turn's boards stopped at a cap;</item>
/// <item>unchecked: some check was skipped (a gap, decision #92), or the forecast was never checked though the fight went on (an
/// expected skip, CheckSkipped.Expected, is counted with its reason but doesn't make the turn unchecked);</item>
/// <item>clean: everything checked and matched (the fight's last turn has no enemy turn: its plays decide).</item>
/// </list>
/// Thread-safe: searches post from worker threads.
/// </summary>
public sealed class SessionStats
{
    /// <summary>Reason lists keep this many (the most frequent): a summary stays small whatever happens.</summary>
    public const int MaxReasons = 15;
    /// <summary>A frame where the mod took longer than this is slow (OverlayHost's own threshold for logging one).</summary>
    public const double SlowModFrameMs = 30;
    /// <summary>A gap between frames longer than this, with the mod's share small, is the game's own stall.</summary>
    public const double GameStallMs = 50;

    private sealed class TurnRecord
    {
        public bool ForecastSeen, Mismatch, Skipped, Capped;
    }

    private sealed class AuditCounts
    {
        public int Checked, Matched, Skipped;
        public readonly Dictionary<string, int> SkipReasons = new(), Mismatches = new();
    }

    private readonly object _lock = new();
    private readonly DateTime _startedUtc;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly (int G0, int G1, int G2, long Alloc, TimeSpan Pause) _gcAtStart;

    private int _combat; // the current fight's number (1 for the first), 0 before any
    private readonly Dictionary<int, TurnRecord> _turns = new();
    private int _combats, _turnsTotal, _clean, _unchecked, _capped, _mismatched, _players;
    private string _characters = "";
    private readonly AuditCounts _forecast = new(), _plays = new();
    /// <summary>The audits outside the turns (Audits.PerTurn false), by name: counted, never part of a turn's class.</summary>
    private readonly Dictionary<string, AuditCounts> _others = new();
    private readonly Dictionary<string, int> _caps = new(), _reports = new();
    private int _searches, _background, _cancelled;
    private long _copies, _cancelledCopies, _maxHeld;
    private string _slowestSearch = "";
    private readonly Histogram _searchMs = new(), _waitMs = new(), _unsettledMs = new(), _modFrameMs = new();
    private int _waitsMissed, _slowFrames, _gameStalls;

    public SessionStats(DateTime? startedUtc = null)
    {
        _startedUtc = startedUtc ?? DateTime.UtcNow;
        _gcAtStart = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalAllocatedBytes(), GC.GetTotalPauseDuration());
    }

    /// <summary>The current fight's number, for SimJob.Context (0 before the first).</summary>
    public int Combat { get { lock (_lock) return _combat; } }

    /// <summary>Turns seen so far (the summary is sent only when there was one).</summary>
    public int Turns { get { lock (_lock) return _turnsTotal + _turns.Count; } }

    public void Post(DevEvent devEvent)
    {
        lock (_lock)
        {
            switch (devEvent)
            {
                case CombatStarted combatStarted:
                    CloseTurns();
                    _combat++;
                    _combats++;
                    _players = Math.Max(_players, combatStarted.Players);
                    if (_characters.Length == 0) _characters = combatStarted.Characters;
                    break;
                case CombatEnded:
                    break; // its turns close when the next fight starts: the last play's check may still come
                case TurnStarted turnStarted:
                    Turn(turnStarted.Turn);
                    break;
                case CheckDone checkDone:
                {
                    var auditCounts = Audit(checkDone.Audit);
                    auditCounts.Checked++;
                    if (!Audits.PerTurn(checkDone.Audit))
                    {
                        if (checkDone.Matched) auditCounts.Matched++;
                        else Count(auditCounts.Mismatches, checkDone.Detail ?? "?");
                        break;
                    }
                    var rec = Turn(checkDone.Turn);
                    if (checkDone.Audit == Audits.Forecast) rec.ForecastSeen = true;
                    if (checkDone.Matched) auditCounts.Matched++;
                    else
                    {
                        rec.Mismatch = true;
                        Count(auditCounts.Mismatches, checkDone.Detail ?? "?");
                    }
                    break;
                }
                case CheckSkipped checkSkipped:
                {
                    var auditCounts = Audit(checkSkipped.Audit);
                    auditCounts.Skipped++;
                    Count(auditCounts.SkipReasons, checkSkipped.Expected ? checkSkipped.Reason + " (expected)" : checkSkipped.Reason);
                    if (!Audits.PerTurn(checkSkipped.Audit)) break;
                    var rec = Turn(checkSkipped.Turn);
                    if (!checkSkipped.Expected) rec.Skipped = true;
                    if (checkSkipped.Audit == Audits.Forecast) rec.ForecastSeen = true;
                    break;
                }
                case SearchDone searchDone:
                    _searches++;
                    _copies += searchDone.Copies;
                    if (searchDone.Cancelled)
                    {
                        _cancelled++;
                        _cancelledCopies += searchDone.Copies;
                        break;
                    }
                    if (searchDone.Background) _background++;
                    _maxHeld = Math.Max(_maxHeld, searchDone.PeakHeld);
                    if (searchDone.Ms > _searchMs.Max) _slowestSearch = searchDone.Label;
                    _searchMs.Add(searchDone.Ms);
                    if (searchDone.Cap != null)
                    {
                        Count(_caps, searchDone.Cap);
                        if (searchDone.Turn > 0 && searchDone.Combat == _combat) Turn(searchDone.Turn).Capped = true;
                    }
                    break;
                case FrameWait frameWait:
                    _waitMs.Add(frameWait.Ms);
                    if (frameWait.Missed) _waitsMissed++;
                    break;
                case Unsettled unsettled:
                    _unsettledMs.Add(unsettled.Ms);
                    break;
                case ReportSent reportSent:
                    Count(_reports, reportSent.What);
                    break;
            }
        }
    }

    /// <summary>One frame: the mod's own time in it, and the time since the frame before (OverlayHost).</summary>
    public void Frame(double modMs, double gapMs)
    {
        lock (_lock)
        {
            _modFrameMs.Add(modMs);
            if (modMs > SlowModFrameMs) _slowFrames++;
            else if (gapMs > GameStallMs) _gameStalls++;
        }
    }

    private AuditCounts Audit(string name) => name switch
    {
        Audits.Forecast => _forecast,
        Audits.Play => _plays,
        _ => _others.TryGetValue(name, out var auditCounts) ? auditCounts : _others[name] = new AuditCounts(),
    };

    private TurnRecord Turn(int turn)
    {
        // A check with no turn (the fight just ended: no turn number to read) belongs to the fight's latest turn.
        if (turn <= 0 && _turns.Count > 0) turn = _turns.Keys.Max();
        if (!_turns.TryGetValue(turn, out var rec)) _turns[turn] = rec = new TurnRecord();
        return rec;
    }

    private static void Count(Dictionary<string, int> counts, string key) => counts[key] = (counts.TryGetValue(key, out var count) ? count : 0) + 1;

    /// <summary>What a turn counts as (see the class summary). <paramref name="last"/>: the fight's last turn so far.</summary>
    private static string Classify(TurnRecord turnRecord, bool last)
        => turnRecord.Mismatch ? "mismatched"
            : turnRecord.Capped ? "capped"
            : turnRecord.Skipped || (!turnRecord.ForecastSeen && !last) ? "unchecked"
            : "clean";

    private void Tally(string kind)
    {
        _turnsTotal++;
        switch (kind)
        {
            case "mismatched": _mismatched++; break;
            case "capped": _capped++; break;
            case "unchecked": _unchecked++; break;
            default: _clean++; break;
        }
    }

    private void CloseTurns()
    {
        if (_turns.Count == 0) return;
        var last = _turns.Keys.Max();
        foreach (var (turn, rec) in _turns)
        {
            var kind = Classify(rec, turn == last);
            // A turn never checked though the fight went on: the reason is that nothing said why (an audit switched off, a gap in
            // the audit itself). Counted so it shows.
            if (kind == "unchecked" && !rec.Skipped)
            {
                _forecast.Skipped++;
                Count(_forecast.SkipReasons, "no check came for this turn's enemy turn");
            }
            Tally(kind);
        }
        _turns.Clear();
    }

    /// <summary>
    /// The session so far: closed fights as classified, the open fight's turns as they stand (its last turn judged on its plays).
    /// Doesn't change the counts: safe to take mid-session (the file kept in case the game closes without saying so).
    /// </summary>
    public SessionSummary Summary(string ended, SessionInfo info)
    {
        lock (_lock)
        {
            int turns = _turnsTotal, clean = _clean, unchecked_ = _unchecked, capped = _capped, mismatched = _mismatched;
            var forecastSkipReasons = new Dictionary<string, int>(_forecast.SkipReasons);
            var forecastSkipped = _forecast.Skipped;
            if (_turns.Count > 0)
            {
                var last = _turns.Keys.Max();
                foreach (var (turn, rec) in _turns)
                {
                    turns++;
                    switch (Classify(rec, turn == last))
                    {
                        case "mismatched": mismatched++; break;
                        case "capped": capped++; break;
                        case "unchecked":
                            unchecked_++;
                            if (!rec.Skipped)
                            {
                                forecastSkipped++;
                                Count(forecastSkipReasons, "no check came for this turn's enemy turn");
                            }
                            break;
                        default: clean++; break;
                    }
                }
            }
            static Dictionary<string, int> Top(Dictionary<string, int> counts)
                => counts.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal).Take(MaxReasons)
                    .ToDictionary(entry => entry.Key, entry => entry.Value);
            AuditSummary Audit(AuditCounts auditCounts, int skipped, Dictionary<string, int> reasons)
                => new(auditCounts.Checked, auditCounts.Matched, skipped, Top(reasons), Top(auditCounts.Mismatches));
            var gcSummary = new GcSummary(
                GC.CollectionCount(0) - _gcAtStart.G0, GC.CollectionCount(1) - _gcAtStart.G1, GC.CollectionCount(2) - _gcAtStart.G2,
                Math.Round((GC.GetTotalPauseDuration() - _gcAtStart.Pause).TotalMilliseconds, 1),
                Math.Round((GC.GetTotalAllocatedBytes() - _gcAtStart.Alloc) / 1048576.0, 1));
            return new SessionSummary
            {
                Mod = info.Mod, Game = info.Game, Ended = ended, StartedUtc = _startedUtc,
                Minutes = Math.Round(_clock.Elapsed.TotalMinutes, 1), Players = Math.Max(_players, 1), Characters = _characters,
                Cores = Environment.ProcessorCount, OtherMods = info.OtherMods,
                Combats = _combats, Turns = turns, Clean = clean, Unchecked = unchecked_, Capped = capped, Mismatched = mismatched,
                Forecast = Audit(_forecast, forecastSkipped, forecastSkipReasons),
                Plays = Audit(_plays, _plays.Skipped, _plays.SkipReasons),
                Others = _others.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToDictionary(entry => entry.Key, entry => Audit(entry.Value, entry.Value.Skipped, entry.Value.SkipReasons)),
                Searches = new SearchSummary(_searches, _background, _cancelled, _copies, _cancelledCopies, _maxHeld, _slowestSearch,
                    _searchMs.Summary(), Top(_caps)),
                Waits = new WaitSummary(_waitsMissed, _waitMs.Summary()),
                Unsettled = _unsettledMs.Summary(),
                Frames = new FrameSummary(_slowFrames, _gameStalls, _modFrameMs.Summary()),
                Gc = gcSummary,
                Reports = Top(_reports),
            };
        }
    }
}

/// <summary>What the game side knows about the session, for its summary.</summary>
public sealed record SessionInfo(string Mod, string Game, bool OtherMods);

/// <summary>
/// One session's summary as sent to Sentry (the event's "summary" extra, JSON) and kept on disk until it's sent (decision #108).
/// <see cref="V"/> is its format: the Sentry sync reads it, so a field's meaning never changes without bumping it.
/// </summary>
public sealed record SessionSummary
{
    public const int Format = 1;
    public int V { get; init; } = Format;
    public string Mod { get; init; } = "";
    public string Game { get; init; } = "";
    /// <summary>How it ended: "menu" (save and quit, or back to the menu after a run), "won", "died", "abandoned", "quit" (the game
    /// closed during a run), "unseen" (sent on the next launch: the game closed without the mod seeing it, a crash most likely).</summary>
    public string Ended { get; init; } = "";
    public DateTime StartedUtc { get; init; }
    public double Minutes { get; init; }
    public int Players { get; init; }
    public string Characters { get; init; } = "";
    public int Cores { get; init; }
    public bool OtherMods { get; init; }
    public int Combats { get; init; }
    public int Turns { get; init; }
    public int Clean { get; init; }
    public int Unchecked { get; init; }
    public int Capped { get; init; }
    public int Mismatched { get; init; }
    public AuditSummary Forecast { get; init; } = AuditSummary.Empty;
    public AuditSummary Plays { get; init; } = AuditSummary.Empty;
    /// <summary>The audits outside the turns (intent, potion, reminders, rest), by name.</summary>
    public Dictionary<string, AuditSummary> Others { get; init; } = new();
    public SearchSummary Searches { get; init; } = SearchSummary.Empty;
    public WaitSummary Waits { get; init; } = new(0, new(0, 0, 0, 0, 0));
    public TimingSummary Unsettled { get; init; } = new(0, 0, 0, 0, 0);
    public FrameSummary Frames { get; init; } = new(0, 0, new(0, 0, 0, 0, 0));
    public GcSummary Gc { get; init; } = new(0, 0, 0, 0, 0);
    public Dictionary<string, int> Reports { get; init; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Read by people and the Sentry sync, never put into a web page: apostrophes in reasons stay readable.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static SessionSummary? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<SessionSummary>(json, Options); }
        catch (JsonException) { return null; }
    }
}

public sealed record AuditSummary(int Checked, int Matched, int Skipped, Dictionary<string, int> SkipReasons, Dictionary<string, int> Mismatches)
{
    public static AuditSummary Empty => new(0, 0, 0, new(), new());
}

/// <summary>Searches that ended (Count includes the cancelled); Ms covers the ones not cancelled.</summary>
public sealed record SearchSummary(int Count, int Background, int Cancelled, long Copies, long CancelledCopies, long MaxHeld,
    string Slowest, TimingSummary Ms, Dictionary<string, int> Caps)
{
    public static SearchSummary Empty => new(0, 0, 0, 0, 0, 0, "", new(0, 0, 0, 0, 0), new());
}

public sealed record WaitSummary(int Missed, TimingSummary Ms);

/// <summary>The mod's own time per frame; Slow: frames over 30 ms; GameStalls: gaps over 50 ms with the mod's share small.</summary>
public sealed record FrameSummary(int Slow, int GameStalls, TimingSummary ModMs);

/// <summary>Garbage collections during the session (the whole process: the game's too), their pauses, and bytes allocated (MB).</summary>
public sealed record GcSummary(int Gen0, int Gen1, int Gen2, double PauseMs, double AllocatedMb);
