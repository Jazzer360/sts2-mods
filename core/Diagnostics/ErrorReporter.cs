using Sentry;

namespace Sts2Mods.Core.Diagnostics;

/// <summary>
/// Sends crashes and audit mismatches to the mod's own Sentry project, with enough context to
/// reproduce them. A GitHub Action turns each new Sentry issue into a GitHub issue
/// (.github/workflows/sentry-issues.yml).
///
/// Uses the Sentry .NET SDK the game already ships (data_sts2_windows_x86_64/Sentry.dll), but
/// through a separate SentryClient with our own DSN: SentrySdk.Init is never called, so the
/// game's own reporting (which it turns off for modded sessions) is untouched, and no global
/// exception handlers are installed. Nothing personal is sent: no server/machine name, no user.
///
/// Every call is wrapped so reporting can never break the game. Within a session each
/// fingerprint is sent once, and at most MaxPerSession events are sent per kind of game (BudgetKey:
/// single-player and multiplayer each have their own, so a noisy co-op session can't use up the reports
/// a single-player fight would send).
/// </summary>
public sealed class ErrorReporter
{
    public const int MaxPerSession = 40;

    public static ErrorReporter? Instance { get; private set; }

    private readonly SentryClient? _client;
    private readonly ModLog _log;
    private readonly HashSet<string> _sent = new();
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _counts = new();

    /// <summary>Which budget a report counts against (e.g. "multiplayer"); one shared budget when null.</summary>
    public Func<string>? BudgetKey { get; set; }

    /// <summary>Mod-specific snapshot of the game state (combat, hand, relics...), added to every report.</summary>
    public Func<IReadOnlyDictionary<string, string>>? ContextProvider { get; set; }

    /// <summary>Tags added to every report (mod version, game version...).</summary>
    public Dictionary<string, string> Tags { get; } = new();

    /// <summary>Tags read when a report is sent (who's in the fight: multiplayer or not, characters).</summary>
    public Func<IReadOnlyDictionary<string, string>>? TagProvider { get; set; }

    /// <summary>Reports are being made: sent (a client exists and the player's switch is on), or kept locally (dev mode: Local).</summary>
    public bool Enabled => Local != null || (_client != null && Active);

    /// <summary>The player's on/off switch (ReportErrors). Can be flipped while the game runs.</summary>
    public bool Active { get; set; }

    /// <summary>
    /// Why nothing may be sent from this game (null: nothing stops it), read before each report: another mod
    /// that changes the game is loaded, so a mismatch or crash may be its doing. Said once in the log.
    /// </summary>
    public Func<string?>? SkipReason { get; set; }

    /// <summary>Why a mismatch may not be sent (null: nothing stops it): another mod that changes the game is loaded.</summary>
    public Func<string?>? MismatchSkipReason { get; set; }

    /// <summary>
    /// Why a crash may not be sent (null: nothing stops it): another mod that changes the game is loaded and its code is in the
    /// crash's stack. A crash only our code (and the game's) is in is sent whatever else is loaded.
    /// </summary>
    public Func<Exception, string?>? CrashSkipReason { get; set; }

    private readonly HashSet<string> _skipsLogged = new();

    /// <summary>
    /// A report was handed to Sentry: its fingerprint and the event id Sentry gave it (decision #108: the session's counts, and the
    /// dev dashboard's inbox later). Raised on a worker thread.
    /// </summary>
    public event Action<string[], string>? Sent;

    private ErrorReporter(SentryClient? client, ModLog log)
    {
        _client = client;
        _log = log;
    }

    /// <summary>
    /// Creates the reporter. With a DSN the client always exists (creating it sends nothing), so
    /// reporting can be switched on later from the settings menu; <paramref name="enabled"/> is
    /// the starting value of <see cref="Active"/>.
    /// </summary>
    public static ErrorReporter Init(string dsn, bool enabled, string release, ModLog log)
    {
        SentryClient? client = null;
        if (!string.IsNullOrWhiteSpace(dsn))
        {
            try
            {
                client = new SentryClient(new SentryOptions
                {
                    Dsn = dsn,
                    Release = release,
                    Environment = "player",
                    ServerName = "player",          // never the machine name
                    SendDefaultPii = false,
                    AutoSessionTracking = false,
                    // No stack captured for an event without an exception (a mismatch): symbolicating the game's stack on its own
                    // thread can stall it (a 10.7 s freeze in a forecast report, decision #101). Crashes keep their exception's.
                    AttachStacktrace = false,
                    MaxBreadcrumbs = 0,
                    SendClientReports = false,
                });
                log.Info(enabled ? "error reporting on (Sentry)" : "error reporting off (ReportErrors = false)");
            }
            catch (Exception ex)
            {
                log.Warn($"error reporting couldn't start: {ex.Message}");
            }
        }
        else
        {
            log.Info("error reporting off (no DSN in this build)");
        }
        Instance = new ErrorReporter(client, log) { Active = enabled };
        return Instance;
    }

    /// <summary>A crash: an exception in a feature, the frame loop or the renderer.</summary>
    public void Exception(Exception ex, string where)
    {
        var top = ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains(" at ") && !l.Contains("System."))?.Trim() ?? "";
        // A game member this build names isn't in the running game (another game version or branch: the code naming it fails to
        // compile, wherever it's called from): one report per member, not one per caller.
        var missing = ex is MissingMemberException or TypeLoadException;
        var fingerprint = missing ? new[] { "crash", "missing-member", ex.Message } : new[] { "crash", where, ex.GetType().Name, top };
        string? skip;
        try { skip = CrashSkipReason?.Invoke(ex); } catch { skip = null; }
        Send(fingerprint, skip, evt =>
        {
            evt.Level = SentryLevel.Error;
            evt.SetTag("kind", "crash");
            evt.SetTag("where", where);
        }, ex);
    }

    /// <summary>
    /// A prediction that didn't match what the game did (an audit mismatch). The fingerprint
    /// groups repeats into one issue; keep it free of amounts so the same bug groups together.
    /// </summary>
    /// <param name="tags">Searchable tags (what was played, which check, which source, the encounter): one issue
    /// groups by its fingerprint, and tags find every issue about the same card or source across groups.</param>
    public void Mismatch(string audit, string title, IEnumerable<string> fingerprint, string details, IReadOnlyDictionary<string, string>? extra = null,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        string? skip;
        try { skip = MismatchSkipReason?.Invoke(); } catch { skip = null; }
        Send(new[] { "mismatch", audit }.Concat(fingerprint).ToArray(), skip, evt =>
        {
            evt.Level = SentryLevel.Warning;
            evt.Message = new SentryMessage { Formatted = title };
            evt.SetTag("kind", "mismatch");
            evt.SetTag("audit", audit);
            evt.SetExtra("details", details);
            if (tags != null)
            {
                foreach (var (k, v) in tags)
                    if (!string.IsNullOrWhiteSpace(v)) evt.SetTag(k, v.Length <= 200 ? v : v[..197] + "...");
            }
            if (extra != null)
            {
                foreach (var (k, v) in extra) evt.SetExtra(k, v);
            }
        }, null);
    }

    private void Send(string[] fingerprint, string? kindSkip, Action<SentryEvent> fill, Exception? ex)
    {
        if (Local == null && (_client == null || !Active)) return;
        string? skip;
        try { skip = SkipReason?.Invoke(); } catch { skip = null; }
        skip ??= kindSkip;
        if (skip != null)
        {
            bool first;
            lock (_lock) first = _skipsLogged.Add(skip);
            if (first) _log.Info($"{fingerprint[0]} reports not sent: {skip}");
            return;
        }
        var key = string.Join("|", fingerprint);
        lock (_lock)
        {
            string budget;
            try { budget = BudgetKey?.Invoke() ?? ""; } catch { budget = ""; }
            var used = _counts.TryGetValue(budget, out var count) ? count : 0;
            if (used >= MaxPerSession || !_sent.Add(key)) return;
            _counts[budget] = used + 1;
        }
        try
        {
            // Each stage timed: a report that takes long to build on the game's thread says where (decision #101).
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var stages = new List<string>();
            void Stage(string name)
            {
                if (watch.ElapsedMilliseconds >= 20) stages.Add($"{name} {watch.ElapsedMilliseconds} ms");
                watch.Restart();
            }
            var evt = ex != null ? new SentryEvent(ex) : new SentryEvent();
            evt.SetFingerprint(fingerprint);
            foreach (var (k, v) in Tags) evt.SetTag(k, v);
            try
            {
                if (TagProvider?.Invoke() is { } live)
                {
                    foreach (var (k, v) in live) evt.SetTag(k, v);
                }
            }
            catch (Exception tex)
            {
                evt.SetExtra("tag_error", tex.Message);
            }
            Stage("tags");
            fill(evt);
            Stage("details");
            try
            {
                if (ContextProvider?.Invoke() is { } ctx)
                {
                    foreach (var (k, v) in ctx) evt.SetExtra(k, v);
                }
            }
            catch (Exception cex)
            {
                evt.SetExtra("context_error", cex.Message);
            }
            Stage("game state");
            evt.SetExtra("recent_log", string.Join("\n", _log.Recent()));
            if (stages.Count > 0) _log.Warn($"[perf] building a {fingerprint[0]} report took: {string.Join(", ", stages)}");
            if (Local is { } local)
            {
                // The developer's game (decision #108): kept on their machine for the dev dashboard, never sent.
                var report = ToLocal(evt, fingerprint, ex);
                _log.Info($"report saved locally ({report.Id}): {report.Title}");
                try { local(report); } catch (Exception lex) { _log.Warn($"the local report couldn't be saved: {lex.Message}"); }
                try { Sent?.Invoke(fingerprint, "local-" + report.Id); } catch { /* a listener's problem is never the report's */ }
                return;
            }
            Capture(evt, fingerprint);
        }
        catch (Exception sendEx)
        {
            // Reporting must never break the game.
            _log.Warn($"error report couldn't be sent: {sendEx.Message}");
        }
    }

    /// <summary>
    /// Handed to Sentry off the game's thread (the game state was read on it), so whatever the client does with the event (its stack
    /// trace, serializing, queueing) never stalls a frame.
    /// </summary>
    private void Capture(SentryEvent evt, string[] fingerprint)
    {
        var client = _client;
        if (client == null) return;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var id = client.CaptureEvent(evt);
                try { Sent?.Invoke(fingerprint, id.ToString()); } catch { /* a listener's problem is never the report's */ }
            }
            catch (Exception sendEx) { _log.Warn($"error report couldn't be sent: {sendEx.Message}"); }
        });
    }

    // ---- local reports (decision #108): in the developer's game, reports stay on their machine.

    /// <summary>
    /// A report kept on the developer's machine instead of sent: everything the Sentry event would have carried (its title, fingerprint,
    /// tags, extras: the details, the game state, the simulator's start state, the recent log; and the exception with its stack).
    /// </summary>
    public sealed record LocalReport(string Id, DateTime AtUtc, string Kind, string Title, string[] Fingerprint,
        IReadOnlyDictionary<string, string> Tags, IReadOnlyDictionary<string, string> Extra, string? Exception);

    /// <summary>
    /// Set (dev mode): every report goes here instead of to Sentry, on the thread that made it, whether Send error reports is on or not
    /// (the usual limits still apply: one per fingerprint, 40 a session). Session summaries aren't sent either.
    /// </summary>
    public Action<LocalReport>? Local { get; set; }

    private static LocalReport ToLocal(SentryEvent evt, string[] fingerprint, Exception? ex)
    {
        var title = evt.Message?.Formatted ?? (ex != null ? $"{ex.GetType().Name}: {ex.Message}" : string.Join(" ", fingerprint));
        var tags = evt.Tags.ToDictionary(entry => entry.Key, entry => entry.Value);
        var extra = evt.Extra.ToDictionary(entry => entry.Key, entry => entry.Value?.ToString() ?? "");
        return new LocalReport($"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}", DateTime.UtcNow,
            tags.GetValueOrDefault("kind") ?? fingerprint[0], title, fingerprint, tags, extra, ex?.ToString());
    }

    /// <summary>
    /// A play session's counts (decision #108): one Info event, grouped into one Sentry issue with every other session's (the
    /// fingerprint), outside the per-session limits (it's one per session). No game state and no log: the counts are in
    /// <paramref name="extra"/>. Sent only while reports are on and nothing stops them (the version floor, SkipReason); with
    /// <paramref name="flush"/> it waits for the send (the game is closing). True when it was handed to Sentry.
    /// </summary>
    public bool Summary(string title, string group, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, string> extra, bool flush = false)
    {
        if (_client == null || !Active || Local != null) return false;
        string? skip;
        try { skip = SkipReason?.Invoke(); } catch { skip = null; }
        if (skip != null)
        {
            _log.Info($"{group} not sent: {skip}");
            return false;
        }
        try
        {
            var evt = new SentryEvent { Level = SentryLevel.Info, Message = new SentryMessage { Formatted = title } };
            evt.SetFingerprint(new[] { group });
            foreach (var (k, v) in Tags) evt.SetTag(k, v);
            try
            {
                if (TagProvider?.Invoke() is { } live)
                {
                    foreach (var (k, v) in live) evt.SetTag(k, v);
                }
            }
            catch (Exception tex)
            {
                evt.SetExtra("tag_error", tex.Message);
            }
            evt.SetTag("kind", group);
            foreach (var (k, v) in tags) if (!string.IsNullOrWhiteSpace(v)) evt.SetTag(k, v.Length <= 200 ? v : v[..197] + "...");
            foreach (var (k, v) in extra) evt.SetExtra(k, v);
            var client = _client;
            if (flush)
            {
                client.CaptureEvent(evt);
                Flush();
            }
            else
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { client.CaptureEvent(evt); }
                    catch (Exception sendEx) { _log.Warn($"{group} couldn't be sent: {sendEx.Message}"); }
                });
            }
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"{group} couldn't be sent: {ex.Message}");
            return false;
        }
    }

    /// <summary>Waits briefly for queued reports (call when the game quits).</summary>
    public void Flush()
    {
        try { _client?.FlushAsync(TimeSpan.FromSeconds(2)).Wait(TimeSpan.FromSeconds(3)); } catch { }
    }
}
