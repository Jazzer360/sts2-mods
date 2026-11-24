using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// PlayFacts is gone (decision #194): a card's effects are its program's steps, translated into the simulator's actions; its counts on
/// the board are the program's (Procs/Programs: BoardCounts, MadeCards); its prediction is CardPrediction, the play check's counts its
/// simulation's log. A facts object decided before the play, beside the simulator, doesn't come back under that name or another file.
/// </summary>
public class PlayFactsGoneTests
{
    [Fact]
    public void PlayFactsStaysGone()
    {
        Assert.False(File.Exists(Path.Combine(RepoFiles.Root(), "mods", "SpireQoL", "Procs", "PlayFacts.cs")));
        var named = RepoFiles.Sources("mods").Concat(RepoFiles.Sources("core"))
            .SelectMany(file => file.Lines.Select((line, index) => (file.Path, Line: index + 1, Text: line)))
            .Where(line => !line.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && line.Text.Contains("PlayFacts", StringComparison.Ordinal))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text.Trim()}").ToList();
        Assert.True(named.Count == 0, "PlayFacts in the mod's code (decision #194: its roles are the program's, CardPrediction's and the simulation's):\n" + string.Join("\n", named));
    }
}
