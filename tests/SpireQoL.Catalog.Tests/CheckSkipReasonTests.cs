using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A check skipped because the play ended the fight is an expected skip (CheckSkip.Expected, decision #108): it says so with
/// CheckSkip.FightEnded, so it isn't held against the turn. The preview's own wording ("the play ended the fight") made every fight's
/// winning turn count as unchecked in the session summary.
/// </summary>
public class CheckSkipReasonTests
{
    [Fact]
    public void TheFightEndedIsAnExpectedSkip()
    {
        Assert.True(new CheckSkip("preview", CheckSkip.FightEnded).Expected);
        Assert.True(new CheckSkip("lethal", CheckSkip.FightEnded + ": who it killed wasn't read").Expected);
    }

    [Fact]
    public void NoCheckWordsTheFightEndingItsOwnWay()
    {
        var diagnostics = Path.Combine(RepoRoot(), "mods", "SpireQoL", "Diagnostics");
        var offenders = Directory.GetFiles(diagnostics, "*.cs")
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(entry => entry.line.Contains("Skip(", StringComparison.Ordinal) && entry.line.Contains("ended the fight\"", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.index + 1}")
            .ToList();
        Assert.True(offenders.Count == 0, "a skip for the fight ending must use CheckSkip.FightEnded: " + string.Join(", ", offenders));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
