using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The decisions (decision #261): their full text in docs/decisions/, one file per hundred, and docs/DECISIONS.md the index of them that
/// scripts/decisions.mjs writes. The index lists every decision, in order, with its title and status as its heading says; every decision
/// the code cites by number is one of them.
/// </summary>
public class DecisionsIndexTests
{
    private static readonly Regex Heading = new(@"^### (\d+)\. (.*?) — (.+?)\s*$");

    private static List<(int Number, string File, string Title, string Status)> Decisions() =>
        Directory.EnumerateFiles(Path.Combine(RepoFiles.Root(), "docs", "decisions"), "*.md").Order(StringComparer.Ordinal)
            .SelectMany(file => File.ReadLines(file).Select(line => Heading.Match(line)).Where(match => match.Success)
                .Select(match => (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), Path.GetFileName(file), match.Groups[2].Value, match.Groups[3].Value)))
            .ToList();

    [Fact]
    public void TheIndexListsEveryDecisionAsItsHeadingSays()
    {
        var decisions = Decisions();
        Assert.NotEmpty(decisions);
        var index = File.ReadAllLines(Path.Combine(RepoFiles.Root(), "docs", "DECISIONS.md")).Where(line => line.StartsWith("- [#", StringComparison.Ordinal)).ToList();
        var expected = decisions.Select(decision => $"- [#{decision.Number}](decisions/{decision.File}#d{decision.Number}) {decision.Title} — {decision.Status}").ToList();
        var wrong = expected.Zip(index).Where(pair => !pair.Second.StartsWith(pair.First, StringComparison.Ordinal)).Select(pair => pair.First).ToList();
        Assert.True(index.Count == expected.Count && wrong.Count == 0,
            $"docs/DECISIONS.md isn't the index of docs/decisions/ (run node scripts/decisions.mjs): {index.Count} lines for {expected.Count} decisions; first wrong: {wrong.FirstOrDefault()}");
        // Numbered in order, none twice, none skipped, each in its hundred's file.
        Assert.Equal(Enumerable.Range(1, decisions.Count), decisions.Select(decision => decision.Number));
        Assert.All(decisions, decision => Assert.StartsWith(Math.Max(decision.Number / 100 * 100, 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture), decision.File));
    }

    /// <summary>
    /// Every decision's heading has its anchor on the line above (<c>&lt;a id="dN"&gt;&lt;/a&gt;</c>), which the index's links go to: #387 to #394
    /// were written without one, and their links went nowhere.
    /// </summary>
    [Fact]
    public void EveryDecisionHasTheAnchorTheIndexLinksTo()
    {
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoFiles.Root(), "docs", "decisions"), "*.md"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Heading.Match(lines[i]) is { Success: true } heading && (i == 0 || lines[i - 1] != $"<a id=\"d{heading.Groups[1].Value}\"></a>"))
                    missing.Add($"{Path.GetFileName(file)}: #{heading.Groups[1].Value}");
        }
        Assert.True(missing.Count == 0, "Decisions without the anchor above their heading:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryDecisionTheCodeCitesIsOne()
    {
        var numbers = Decisions().Select(decision => decision.Number).ToHashSet();
        var cited = new[] { "mods", "core", "tests", "scripts", "tools" }
            .Select(folder => Path.Combine(RepoFiles.Root(), folder)).Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".mjs", StringComparison.Ordinal) || file.EndsWith(".ps1", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"decisions? #(\d+)").Select(match => (File: Path.GetFileName(file), Number: int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))))
            .Where(citation => !numbers.Contains(citation.Number)).Select(citation => $"{citation.File}: #{citation.Number}").Distinct().ToList();
        Assert.True(cited.Count == 0, "Decisions the code cites that docs/decisions/ doesn't have:\n" + string.Join("\n", cited));
    }
}
