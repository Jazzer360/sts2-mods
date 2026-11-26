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
