using Sts2Mods.Core.Game;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A member Sts2State.Private couldn't read (missing, or not of the type asked for) is what a game update renaming or changing it
/// looks like: the read gives its fallback, and the open read scope says so, so the adapter marks what it built unknown.
/// </summary>
public class ReadScopeTests
{
    private sealed class Probe
    {
        private readonly string _text = "Probe";
        private int Number { get; } = 4;
        public override string ToString() => _text + Number;
    }

    [Fact]
    public void AMissingOrMistypedMemberIsCountedInTheOpenScope()
    {
        using var reads = Sts2State.TrackReads();
        Assert.Equal(4, Sts2State.Private(new Probe(), "Number", fallback: 0));
        Assert.Empty(reads.Unread);
        Assert.Equal(3, Sts2State.Private(new Probe(), "Gone", fallback: 3));
        Assert.Equal(5, Sts2State.Private(new Probe(), "_text", fallback: 5)); // there, but a string
        Assert.Equal(new[] { "Probe.Gone", "Probe._text" }, reads.Unread);
    }

    [Fact]
    public void AnInnerScopesMissesCountInTheOuterOneAndScopesEnd()
    {
        using var outer = Sts2State.TrackReads();
        using (var inner = Sts2State.TrackReads())
        {
            Sts2State.Private(new Probe(), "Gone", fallback: 0);
            Assert.Single(inner.Unread);
        }
        Assert.Single(outer.Unread);
        outer.Dispose();
        Sts2State.Private(new Probe(), "Gone", fallback: 0); // no scope open: nothing to tell
        Assert.Single(outer.Unread);
    }
}
