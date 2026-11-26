using System.Reflection;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The event options are read at load once the game has made its models (decision #352): ModEntry polls OneTimeInitialization._state, a private static
/// enum field, for Done. A game update that renames the field or the value would leave the poll reading "can't tell" and the options read before the
/// models are made, so the field and its Done are held here, on the loaded game and on each branch's assembly.
/// </summary>
public class GameInitStateTests
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    private static void HasDone(Type? initialization, string branch)
    {
        Assert.True(initialization != null, $"{branch}: OneTimeInitialization is gone");
        var state = initialization!.GetField("_state", Static);
        Assert.True(state != null, $"{branch}: OneTimeInitialization._state is gone");
        Assert.True(state!.FieldType.IsEnum, $"{branch}: OneTimeInitialization._state isn't an enum ({state.FieldType.Name})");
        Assert.Contains("Done", state.FieldType.GetEnumNames());
    }

    [Fact]
    public void TheLoadedGameHasTheStateItPolls() => HasDone(typeof(MegaCrit.Sts2.Core.Helpers.OneTimeInitialization), "loaded");

    [Theory]
    [InlineData("beta")]
    [InlineData("main")]
    public void EachBranchHasTheStateItPolls(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile of this branch here: GameBranchTests says so
        HasDone(BranchCode.Of(path).Assembly.GetType("MegaCrit.Sts2.Core.Helpers.OneTimeInitialization"), branch);
    }
}
