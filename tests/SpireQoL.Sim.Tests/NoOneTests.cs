using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// No one is Who.None (decision #142), never -1, which is you. A creature id compared with -1 or looked up by it reads as you; a
/// played card's aim is read through SimInputs.AimFor only (its own aim, or no one's), so a check can't miss the play by its key.
/// </summary>
public class NoOneTests
{
    private static readonly Regex[] MinusOneAsNoOne =
    {
        new(@"(?:TryGetValue|ContainsKey)\(-1\b"),                                     // a lookup by -1
        new(@"\b(?:[Tt]arget(?:Id)?|[Aa]im|allyId|channelTo|HexFrom|ArrowAwayAt)\s*[!=]=\s*-1\b"), // a creature id tested against -1
        new(@"\b(?:[Tt]arget(?:Id)?|[Aa]im|allyId|HexFrom|ArrowAwayAt)\s*=\s*-1\s*[;,]"), // or set to it
        new(@"\?\?\s*-1\b.*\b(?:Target|Enemy)\b|\b(?:Target|Enemy)\b.*\?\?\s*-1\b"),  // a missing target taken as -1
        new(@"PlayByTarget\.(?:TryGetValue|ContainsKey)\((?!aim\b|handAim\b|lethalAim\b|key\b|Who\.None\b)"), // a play looked up without AimFor
    };

    [Fact]
    public void NoOneIsNeverMinusOne()
    {
        var found = RepoFiles.Sources("mods/SpireQoL")
            .SelectMany(source => source.Lines.Select((line, index) => (source.Path, Line: index + 1, Text: line.Trim())))
            .Where(line => !line.Text.StartsWith("//", StringComparison.Ordinal))
            .Where(line => MinusOneAsNoOne.Any(pattern => pattern.IsMatch(line.Text)))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text}").ToList();
        Assert.True(found.Count == 0, "-1 as no one (use Who.None; a played card's aim through SimInputs.AimFor):\n" + string.Join("\n", found));
    }
}
