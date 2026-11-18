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
        catch (Exception e) { return e; }
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
