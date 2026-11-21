using System.Reflection;
using Sts2Mods.Core.Game;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #96: with a mod that changes the game loaded, a crash is still sent unless that mod's code is in its stack. Here the
/// xunit assembly stands in for another mod.
/// </summary>
public class CrashFilterTests
{
    private static readonly IReadOnlyDictionary<Assembly, string> Xunit = new Dictionary<Assembly, string> { [typeof(Assert).Assembly] = "OtherMod" };

    private static Exception Caught(Action a)
    {
        try { a(); }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("didn't throw");
    }

    [Fact]
    public void OurOwnCrashIsSent() =>
        Assert.Null(OtherMods.InStack(Caught(() => throw new NullReferenceException()), Xunit, "SpireQoL."));

    [Fact]
    public void ACrashInAnotherModsCodeIsNot() =>
        Assert.Equal("OtherMod", OtherMods.InStack(Caught(() => Assert.Fail("their code")), Xunit, "SpireQoL."));

    [Fact]
    public void ItsCodeAsAnInnerExceptionCounts() =>
        Assert.Equal("OtherMod", OtherMods.InStack(new InvalidOperationException("wrapped", Caught(() => Assert.Fail("their code"))), Xunit, "SpireQoL."));
}

/// <summary>Decision #97: reporting.json's gameplayMods list.</summary>
public class GameplayModsListTests
{
    [Fact]
    public void TheListIsRead() =>
        Assert.Equal(new[] { "SomeMod", "Other" }, Sts2Mods.Core.Diagnostics.ReportFloor.ParseGameplayMods("""{ "oldestReported": { "SpireQoL": "1.0.0" }, "gameplayMods": ["SomeMod", "Other"] }"""));

    [Fact]
    public void NoListIsEmpty()
    {
        Assert.Empty(Sts2Mods.Core.Diagnostics.ReportFloor.ParseGameplayMods("""{ "oldestReported": { "SpireQoL": "1.0.0" } }"""));
        Assert.Empty(Sts2Mods.Core.Diagnostics.ReportFloor.ParseGameplayMods("not json"));
    }

    [Fact]
    public void TheRepositorysFileHasTheList()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "reporting.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "reporting.json"));
        Assert.Contains("\"gameplayMods\"", text);
        Assert.NotNull(Sts2Mods.Core.Diagnostics.ReportFloor.Parse(text, "SpireQoL"));
    }
}
