using System.Collections.Concurrent;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The preload watch (Cards.GamePreload): a game preload unfinished after 30 s is reported, saying whether the settings menu was open during it (the
/// 2026-11-25 black screen; the menu no longer waits for the preload: decision #397). GamePreload reads NAssetLoader's private fields; a game update
/// that renames them would quietly make Busy always false and the watch blind, so the fields it reads are held here, and on main's decompile.
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

    /// <summary>The stuck-preload report reads these of AssetLoadingSession (GamePreload.Describe): a rename would leave its report blank.</summary>
    [Fact]
    public void TheSessionsFieldsTheStuckReportReadsAreThere()
    {
        foreach (var name in new[] { "_name", "_toLoad", "_loading", "_finalizing", "_vfxScenes", "_vfxLoading", "_currentVfxPath" })
            Assert.True(AccessTools.Field(typeof(AssetLoadingSession), name) != null, $"AssetLoadingSession.{name} is gone");
    }

    [Fact]
    public void TheWatchDoesNothingWithNoLoader() => Cards.GamePreload.Watch();

    [Fact]
    public void TheMenuOpeningAndClosingIsKept()
    {
        Cards.GamePreload.MenuShown(open: true);
        Assert.True(Cards.GamePreload.SettingsMenuOpen);
        Cards.GamePreload.MenuShown(open: false);
        Assert.False(Cards.GamePreload.SettingsMenuOpen);
    }
}
