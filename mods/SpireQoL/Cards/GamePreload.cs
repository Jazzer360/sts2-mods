using System.Collections.Concurrent;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes;

namespace SpireQoL.Cards;

/// <summary>
/// Whether the game is preloading assets in the background (NAssetLoader's current session, or one queued after it). Godot's threaded loader can
/// stall for good when another load touches what it's loading (the game's own notes on AssetLoadingSession's VFX queue and
/// PreloadManager.EnsureAtlasesLoaded). The settings menu builds the game's card nodes and loads their frames, banners and icons; opened at the main
/// menu while the "Common" preload was still going, that left the preload unfinished, and the next run's preload waited on it forever: a black
/// screen (2026-11-25). So what the mod builds from the game's resources waits for these sessions to finish. Reads the game's nodes only: never
/// creates the loader (NAssetLoader.Instance would make one when there's none).
/// </summary>
internal static class GamePreload
{
    /// <summary>NAssetLoader._instance: the loader node once the game has made it (both branches).</summary>
    private static readonly AccessTools.FieldRef<NAssetLoader?>? LoaderOf = StaticField<NAssetLoader?>("_instance");
    /// <summary>NAssetLoader._currentSession: the session it's working through, or null.</summary>
    private static readonly AccessTools.FieldRef<NAssetLoader, AssetLoadingSession?>? CurrentOf = Field<AssetLoadingSession?>("_currentSession");
    /// <summary>NAssetLoader._sessions: the sessions waiting their turn.</summary>
    private static readonly AccessTools.FieldRef<NAssetLoader, ConcurrentQueue<AssetLoadingSession?>>? QueuedOf = Field<ConcurrentQueue<AssetLoadingSession?>>("_sessions");

    /// <summary>
    /// The game is preloading (a session unfinished, or one queued). False when the loader can't be read (a game update renamed its fields): the
    /// mod then builds as it did before.
    /// </summary>
    internal static bool Busy
    {
        get
        {
            try
            {
                if (LoaderOf?.Invoke() is not { } loader) return false;
                if (CurrentOf?.Invoke(loader) is { IsCompleted: false }) return true;
                return QueuedOf?.Invoke(loader) is { IsEmpty: false };
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>How long a preload may run before the watch reports it (the main menu's Common preload takes 2 to 3 s).</summary>
    private const ulong StuckAfterMs = 30_000;

    private static AssetLoadingSession? _watched;
    private static ulong _watchedSince;
    private static bool _reportedStuck;

    /// <summary>
    /// Every frame: a preload that hasn't finished in 30 s is reported once (the log and an error report: its name, what's left in each of its
    /// queues and the VFX scene it's on), so a black screen like 2026-11-25's leaves evidence of what it was stuck on. Reads two fields a frame;
    /// builds text only when it reports.
    /// </summary>
    internal static void Watch()
    {
        AssetLoadingSession? session;
        try { session = LoaderOf?.Invoke() is { } loader ? CurrentOf?.Invoke(loader) : null; }
        catch { return; }
        if (session == null || session.IsCompleted)
        {
            _watched = null;
            return;
        }
        var now = Godot.Time.GetTicksMsec();
        if (!ReferenceEquals(session, _watched))
        {
            (_watched, _watchedSince, _reportedStuck) = (session, now, false);
            return;
        }
        if (_reportedStuck || now - _watchedSince < StuckAfterMs) return;
        _reportedStuck = true;
        var details = Describe(session);
        ModEntry.Log.Warn($"[preload] the game's asset preload hasn't finished in {StuckAfterMs / 1000} s: {details}");
        Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("preload", "The game's asset preload hasn't finished in 30 s",
            new[] { Traverse.Create(session).Field<string>("_name").Value ?? "?" }, details);
    }

    /// <summary>A stuck session as text: its name, each queue's count, the VFX scene it's loading (AssetLoadingSession's own fields).</summary>
    private static string Describe(AssetLoadingSession session)
    {
        try
        {
            var fields = Traverse.Create(session);
            int Count(string name) => fields.Field(name).GetValue() is System.Collections.ICollection queue ? queue.Count : -1;
            return $"'{fields.Field<string>("_name").Value}': to load {Count("_toLoad")}, loading {Count("_loading")}, finalizing {Count("_finalizing")}, " +
                $"VFX left {Count("_vfxScenes")}, VFX loading {fields.Field<bool>("_vfxLoading").Value} ({fields.Field<string>("_currentVfxPath").Value ?? "none"}), " +
                $"settings menu waiting {SettingsMenuWaiting}, performance trace: {Telemetry.DevDashboard.TraceStatus() ?? "none"}";
        }
        catch (Exception error)
        {
            return $"(couldn't read the session: {error.Message})";
        }
    }

    /// <summary>The settings menu is waiting for the preload to open (set by SettingsMenu), for the stuck report.</summary>
    internal static bool SettingsMenuWaiting { get; set; }

    private static AccessTools.FieldRef<NAssetLoader, TField>? Field<TField>(string name)
    {
        try { return AccessTools.Field(typeof(NAssetLoader), name) != null ? AccessTools.FieldRefAccess<NAssetLoader, TField>(name) : null; }
        catch { return null; }
    }

    private static AccessTools.FieldRef<TField>? StaticField<TField>(string name)
    {
        try { return AccessTools.Field(typeof(NAssetLoader), name) != null ? AccessTools.StaticFieldRefAccess<TField>(AccessTools.Field(typeof(NAssetLoader), name)) : null; }
        catch { return null; }
    }
}
