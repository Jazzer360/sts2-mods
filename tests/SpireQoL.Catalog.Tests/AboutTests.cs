using SpireQoL.Menu;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The settings menu's About page (decision #68) reads CHANGELOG.md, the README and the manifest built into the mod: every
/// release heading parses, the current version has notes, and the philosophy and author are there.
/// </summary>
public class AboutTests
{
    [Fact]
    public void EveryReleaseInTheChangelogParses()
    {
        var text = ReleaseNotes.Resource("CHANGELOG.md");
        Assert.NotNull(text);
        var headings = text!.Split('\n').Count(line => line.StartsWith("### " + ReleaseNotes.ModName + " ", StringComparison.Ordinal));
        var releases = ReleaseNotes.Releases;
        Assert.Equal(headings, releases.Count);
        Assert.All(releases, release => Assert.NotEmpty(release.Sections));
        Assert.Equal(releases.Count, releases.Select(release => release.Version).Distinct().Count());
    }

    [Fact]
    public void TheCurrentVersionComesFirst()
    {
        var current = typeof(ModEntry).Assembly.GetName().Version!.ToString(3);
        Assert.Equal(current, ReleaseNotes.Releases[0].Version);
    }

    [Fact]
    public void PhilosophyAndAuthorAreBuiltIn()
    {
        Assert.NotEmpty(ReleaseNotes.Philosophy);
        Assert.NotEqual("", ReleaseNotes.Author);
    }

    [Fact]
    public void ParsesGroupsTitlesNestingAndDates()
    {
        const string md = """
            ## [Unreleased]

            ### Spire QoL 1.2.0
            #### Added
            - **Bold** thing with `code` and a [link](https://example.com)
              continued here.
              - a nested point
            #### Fixed
            - A fix.

            ### Main Saves 1.0.0
            - not ours

            ### Spire QoL 0.2.0 (first test build)
            - Plain item.
            """;
        var releases = ReleaseNotes.Parse(md, "Spire QoL", ReleaseNotes.Dates("spireqol-v1.2.0 2026-11-16\n"));
        Assert.Equal(2, releases.Count);
        Assert.Equal("2026-11-16", releases[0].Date);
        Assert.Equal(new[] { "Added", "Fixed" }, releases[0].Sections.Select(releaseSection => releaseSection.Kind));
        Assert.Equal("Bold thing with code and a link continued here.", releases[0].Sections[0].Items[0]);
        Assert.Equal("  ◦ a nested point", releases[0].Sections[0].Items[1]);
        Assert.Equal("first test build", releases[1].Title);
        Assert.Null(releases[1].Date);
        Assert.Null(releases[1].Sections[0].Kind);
    }
}
