using Sts2Mods.Core.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// reporting.json (decision #81): the floor the mod reads from GitHub parses, and never silences the version being released.
/// </summary>
public class ReportFloorTests
{
    private static string RepoFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, name);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void TheFloorParsesAndTheCurrentVersionIsAtOrAboveIt()
    {
        var floor = ReportFloor.Parse(File.ReadAllText(RepoFile("reporting.json")), ModEntry.Id);
        Assert.NotNull(floor);
        Assert.True(typeof(ModEntry).Assembly.GetName().Version! >= floor, $"the floor {floor} would stop this release's reports");
    }

    [Fact]
    public void AMissingOrBrokenFloorIsNone()
    {
        Assert.Null(ReportFloor.Parse("{}", "SpireQoL"));
        Assert.Null(ReportFloor.Parse("{\"oldestReported\":{\"Other\":\"1.0.0\"}}", "SpireQoL"));
        Assert.Null(ReportFloor.Parse("not json", "SpireQoL"));
        Assert.Equal(new Version(1, 2, 3), ReportFloor.Parse("{\"oldestReported\":{\"SpireQoL\":\"1.2.3\"}}", "SpireQoL"));
    }
}

/// <summary>The releases links (Update Notice, About page) open Spire QoL's releases, not the repo's other mods'.</summary>
public class ReleasesLinkTests
{
    [Fact]
    public void LinksAreSpireQoLsOwn()
    {
        Assert.Equal("https://github.com/Jazzer360/sts2-mods/releases?q=spireqol", SpireQoL.Features.UpdateNoticeFeature.ReleasesLink(null));
        var behind = new Sts2Mods.Core.Game.GitHubReleases.Status(2, new Version(1, 15, 3), true);
        Assert.Equal("https://github.com/Jazzer360/sts2-mods/releases/tag/spireqol-v1.15.3", SpireQoL.Features.UpdateNoticeFeature.ReleasesLink(behind));
        var current = new Sts2Mods.Core.Game.GitHubReleases.Status(0, new Version(1, 15, 3), true);
        Assert.Equal("https://github.com/Jazzer360/sts2-mods/releases?q=spireqol", SpireQoL.Features.UpdateNoticeFeature.ReleasesLink(current));
    }
}
