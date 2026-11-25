using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #125: the coverage check reports a game version only when its coverage wasn't reviewed: the ledger's own, or an
/// earlier one players still run (v0.113.0, the first beta build), and main's (decision #170). The version is matched whole.
/// </summary>
public class CoverageVersionTests
{
    [Theory]
    [InlineData("v0.113.1", true)]
    [InlineData("v0.113.0", true)]
    [InlineData("0.113.0", true)]
    [InlineData("v0.113.0 (beta)", true)]
    [InlineData("v0.111.1", true)]    // main's supported build (SupportedVersions.Main, decision #170)
    [InlineData("v0.111.0", true)]    // the main build before it
    [InlineData("v0.111.2", false)]   // a main build not reviewed yet
    [InlineData("v0.113.01", false)]
    [InlineData("v0.113.10", false)]
    [InlineData("v0.114.0", true)]    // beta's supported build (the ledger's)
    [InlineData("v0.114.1", false)]   // a beta build not reviewed yet
    [InlineData("v0.113.2", false)]
    public void OnlyUnreviewedVersionsAreReported(string game, bool reviewed)
        => Assert.Equal(reviewed, CoverageLedger.ReviewedVersion(game));
}
