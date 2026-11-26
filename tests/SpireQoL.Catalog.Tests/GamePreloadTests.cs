using System.Collections.Concurrent;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The settings menu waits for the game's asset preload (Cards.GamePreload, 2026-11-25: opened during the main menu's "Common" preload, it left the
/// game's threaded loader stalled and the next run's start on a black screen). GamePreload reads NAssetLoader's private fields; a game update that
/// renames them would quietly make Busy always false, so the fields it reads are held here, and on main's decompile.
/// </summary>
public class GamePreloadTests
{
    [Fact]
    public void TheLoadersFieldsAreThereWithTheTypesItReads()
    {
        Assert.Equal(typeof(NAssetLoader), AccessTools.Field(typeof(NAssetLoader), "_instance")?.FieldType);
        Assert.Equal(typeof(AssetLoadingSession), AccessTools.Field(typeof(NAssetLoader), "_currentSession")?.FieldType);
        Assert.Equal(typeof(ConcurrentQueue<AssetLoadingSession>), AccessTools.Field(typeof(NAssetLoader), "_sessions")?.FieldType);
        Assert.NotNull(AccessTools.Property(typeof(AssetLoadingSession), nameof(AssetLoadingSession.IsCompleted)));
    }

    [Fact]
    public void MainsLoaderHasTheSameFields()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "git", "sts2-decompiled-main", "MegaCrit.Sts2.Core.Nodes", "NAssetLoader.cs");
        if (!File.Exists(path)) return; // no main decompile on this machine: GameBranchTests says so
        var source = File.ReadAllText(path);
        Assert.Contains("private static NAssetLoader? _instance;", source);
        Assert.Contains("private AssetLoadingSession? _currentSession;", source);
        Assert.Contains("ConcurrentQueue<AssetLoadingSession?> _sessions", source);
    }

    [Fact]
    public void NoLoaderIsNotBusy() => Assert.False(Cards.GamePreload.Busy);
}
