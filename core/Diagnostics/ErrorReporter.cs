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

    /// <summary>Reports are being sent: a client exists and the player's switch is on.</summary>
    public bool Enabled => _client != null && Active;

    /// <summary>The player's on/off switch (ReportErrors). Can be flipped while the game runs.</summary>
    public bool Active { get; set; }

    /// <summary>
    /// Why nothing may be sent from this game (null: nothing stops it), read before each report: another mod
    /// that changes the game is loaded, so a mismatch or crash may be its doing. Said once in the log.
    /// </summary>
    public Func<string?>? SkipReason { get; set; }

    private bool _skipLogged;

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
                    AttachStacktrace = true,
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
        Send(new[] { "crash", where, ex.GetType().Name, top }, evt =>
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
        Send(new[] { "mismatch", audit }.Concat(fingerprint).ToArray(), evt =>
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

    private void Send(string[] fingerprint, Action<SentryEvent> fill, Exception? ex)
    {
        if (_client == null || !Active) return;
        string? skip;
        try { skip = SkipReason?.Invoke(); } catch { skip = null; }
        if (skip != null)
        {
            if (!_skipLogged) _log.Info($"error reports not sent: {skip}");
            _skipLogged = true;
            return;
        }
        var key = string.Join("|", fingerprint);
        lock (_lock)
        {
            string budget;
            try { budget = BudgetKey?.Invoke() ?? ""; } catch { budget = ""; }
            var used = _counts.TryGetValue(budget, out var n) ? n : 0;
            if (used >= MaxPerSession || !_sent.Add(key)) return;
            _counts[budget] = used + 1;
        }
        try
        {
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
            fill(evt);
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
            evt.SetExtra("recent_log", string.Join("\n", _log.Recent()));
            _client.CaptureEvent(evt);
        }
        catch (Exception sendEx)
        {
            // Reporting must never break the game.
            _log.Warn($"error report couldn't be sent: {sendEx.Message}");
        }
    }

    /// <summary>Waits briefly for queued reports (call when the game quits).</summary>
    public void Flush()
    {
        try { _client?.FlushAsync(TimeSpan.FromSeconds(2)).Wait(TimeSpan.FromSeconds(3)); } catch { }
    }
}
