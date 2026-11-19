using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using SpireQoL.Sim;
using Sts2Mods.Core.Diagnostics;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Settings;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Telemetry;

/// <summary>
/// Play sessions (decision #108): from starting or continuing a run until it's left (save and quit, back to the menu after a win or
/// a death, abandoning it) or the game closes. During one, the event stream feeds a SessionStats (fights, turns, checks, searches,
/// waits, frame times); at its end, if it had a turn, its summary goes to Sentry as one Info event (ErrorReporter.Summary), which the
/// Sentry sync adds up per mod version in a pinned GitHub issue. Nothing is drawn: it only reads the run, the fight and the turn.
///
/// The summary so far is kept in a file next to the settings, rewritten at each turn and fight: a session the mod never saw end (the
/// game crashed, or closed without running its exit handlers) is sent on the next launch, marked "unseen", so crashes don't drop
/// out of the counts.
/// </summary>
public sealed class SessionWatch : IFeature, IFrameFeature
{
    public string Id => "session-summary";
    public string DisplayName => "Session Summary (log only)";
    public bool EnabledByDefault => true;

    private static volatile SessionStats? _current;

    private object? _run;
    private object? _combat;
    private int _turn;
    private long _unsettledSince;
    private bool _abandoned, _gameOver, _endSeen, _won;

    /// <summary>RunManager.HasRunEnded: on the beta branch only (decision #78), so read when it's there.</summary>
    private static readonly PropertyInfo? HasRunEnded = typeof(RunManager).GetProperty("HasRunEnded", BindingFlags.Public | BindingFlags.Instance);

    /// <summary>RunManager's private State (the run itself: a new object per run), read every frame, so looked up once.
    /// Missing (a game update renamed it): Sts2State.Private reads it, and says so once.</summary>
    private static readonly PropertyInfo? RunStateProperty = typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance);

    private static string PendingPath => Path.Combine(
        Path.GetDirectoryName(JsonSettingsFile<QolSettings>.DefaultPathFor(typeof(ModEntry), ModEntry.Id)) ?? AppContext.BaseDirectory,
        ModEntry.Id + ".session.json");

    public void Apply(Harmony harmony)
    {
    }

    public void Attach(OverlayHost host)
    {
        DevEvents.OnError = ex => ModEntry.Log.Warn($"[session] the event stream's counter threw (later events still count): {ex.Message}");
        DevEvents.Posted += e => _current?.Post(e);
        host.FrameMeasured += (modMs, gapMs) => _current?.Frame(modMs, gapMs);
        if (ErrorReporter.Instance is { } reporter)
            reporter.Sent += (fp, id) => DevEvents.Post(new ReportSent(fp[0] == "mismatch" && fp.Length > 1 ? fp[1] : fp[0], id));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => End("quit", flush: true);
        SendUnseen();
    }

    public void OnFrame(OverlayHost host)
    {
        var rm = RunManager.Instance;
        var run = rm is { IsInProgress: true }
            ? RunStateProperty != null ? RunStateProperty.GetValue(rm) : Sts2State.Private<RunState?>(rm, "State", null)
            : null;
        if (!ReferenceEquals(run, _run))
        {
            if (_run != null) End(HowItEnded());
            _run = run;
            if (run != null) Begin();
        }
        if (_current is not { } stats || rm == null) return;

        if (rm.IsAbandoned) _abandoned = true;
        if (rm.IsGameOver) _gameOver = true;
        if (!_endSeen && HasRunEnded?.GetValue(rm) is true)
        {
            // A win ends the run before the false end kills every player; a death ends it with everyone dead already.
            _endSeen = true;
            _won = !rm.IsGameOver;
        }

        var cs = Sts2State.Combat;
        if (!ReferenceEquals(cs, _combat))
        {
            if (_combat != null) DevEvents.Post(new CombatEnded());
            _combat = cs;
            _turn = 0;
            if (cs != null)
            {
                var me = Sts2State.Me;
                var players = cs.Players.ToList();
                DevEvents.Post(new CombatStarted(cs.Encounter?.GetType().Name ?? "?", players.Count,
                    string.Join(", ", players.OrderBy(p => p == me ? 0 : 1).Select(p => p.Character.GetType().Name))));
                SavePending(stats);
            }
        }
        var playerTurn = cs != null && Sts2State.IsPlayerTurn;
        var turn = playerTurn ? Sts2State.TurnNumber : 0;
        if (playerTurn && turn != _turn)
        {
            _turn = turn;
            DevEvents.Post(new TurnStarted(turn));
            SavePending(stats);
        }
        Interlocked.Exchange(ref SimJob.Context, playerTurn ? SearchDone.ContextOf(stats.Combat, turn) : 0);

        // The progress bar's spells (searches still running on your turn): how long until the board settles.
        var running = playerTurn && SimJob.Progress().Count > 0;
        if (running && _unsettledSince == 0) _unsettledSince = Stopwatch.GetTimestamp();
        else if (!running && _unsettledSince != 0)
        {
            DevEvents.Post(new Unsettled(Stopwatch.GetElapsedTime(_unsettledSince).TotalMilliseconds));
            _unsettledSince = 0;
        }
    }

    private void Begin()
    {
        _current = new SessionStats();
        _combat = null;
        _turn = 0;
        _unsettledSince = 0;
        _abandoned = _gameOver = _endSeen = _won = false;
        ModEntry.Log.Info("[session] started");
    }

    private string HowItEnded()
        => _abandoned ? "abandoned" : _endSeen ? (_won ? "won" : "died") : _gameOver ? "died" : "menu";

    private void End(string how, bool flush = false)
    {
        var stats = _current;
        _current = null;
        Interlocked.Exchange(ref SimJob.Context, 0);
        if (stats == null) return;
        try
        {
            if (stats.Turns == 0)
            {
                DeletePending();
                ModEntry.Log.Info($"[session] ended ({how}) with no fight turns: no summary");
                return;
            }
            var summary = stats.Summary(how, Info());
            ModEntry.Log.Info($"[session] ended ({how}): {Line(summary)}");
            Send(summary, flush);
            DeletePending();
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[session] couldn't finish the session's summary: {ex.Message}");
        }
    }

    /// <summary>The summary's main counts on one line, for the game log.</summary>
    public static string Line(SessionSummary s)
        => $"{s.Combats} fights, {s.Turns} turns ({s.Clean} clean, {s.Unchecked} unchecked, {s.Capped} capped, {s.Mismatched} mismatched); " +
           $"forecasts {s.Forecast.Matched}/{s.Forecast.Checked} matched, {s.Forecast.Skipped} skipped; plays {s.Plays.Matched}/{s.Plays.Checked} matched, " +
           $"{s.Plays.Skipped} checks skipped; {s.Searches.Count} searches ({s.Searches.Background} in the background, {s.Searches.Cancelled} cancelled, " +
           $"slowest {s.Searches.Ms.Max} ms); mod frame time p95 {s.Frames.ModMs.P95} ms, {s.Frames.Slow} slow frames";

    private static SessionInfo Info()
    {
        var tags = ErrorReporter.Instance?.Tags;
        bool others;
        try { others = OtherMods.Loaded(typeof(ModEntry).Assembly).Count > 0; } catch { others = false; }
        return new SessionInfo(tags?.GetValueOrDefault("mod_version") ?? "?", tags?.GetValueOrDefault("game_version") ?? "?", others);
    }

    private static void Send(SessionSummary s, bool flush)
    {
        if (ErrorReporter.Instance is not { Enabled: true } reporter) return;
        var tags = new Dictionary<string, string>
        {
            ["ended"] = s.Ended,
            ["players"] = s.Players.ToString(),
            ["characters"] = s.Characters,
            // The session's own versions (a summary sent on a later launch may be from an older build).
            ["mod_version"] = s.Mod,
            ["game_version"] = s.Game,
        };
        reporter.Summary("Session summary", "session", tags,
            new Dictionary<string, string> { ["summary"] = s.ToJson(), ["summary_line"] = Line(s) }, flush);
    }

    /// <summary>The summary so far, in case the game closes without the mod seeing it (written off the game's thread).</summary>
    private static void SavePending(SessionStats stats)
    {
        string json;
        try { json = stats.Summary("unseen", Info()).ToJson(); }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[session] couldn't take the summary so far: {ex.Message}");
            return;
        }
        var path = PendingPath;
        Task.Run(() =>
        {
            try
            {
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                ModEntry.Log.Warn($"[session] couldn't keep the summary so far: {ex.Message}");
            }
        });
    }

    private static void DeletePending()
    {
        try { File.Delete(PendingPath); } catch { /* nothing to keep */ }
    }

    /// <summary>A session from an earlier launch that never ended in the mod's sight: sent now, then forgotten.</summary>
    private static void SendUnseen()
    {
        try
        {
            if (!File.Exists(PendingPath)) return;
            var s = SessionSummary.FromJson(File.ReadAllText(PendingPath));
            DeletePending();
            if (s is not { Turns: > 0 }) return;
            ModEntry.Log.Info($"[session] the last launch's session wasn't seen ending (a crash, or the game closed early): {Line(s)}");
            Send(s with { Ended = "unseen" }, flush: false);
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[session] couldn't read the last launch's session: {ex.Message}");
        }
    }
}
