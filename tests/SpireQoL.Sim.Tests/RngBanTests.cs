using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The mod never reads the game's RNG or seed (CLAUDE.md, "Only knowable information"). The build bans the types and properties
/// (BannedSymbols.txt, decision #149); reflection could still reach them by name (Sts2State.Private(runState, "Rng")), so no source
/// file names them in a string.
/// </summary>
public class RngBanTests
{
    private static readonly Regex ByName = new(@"""(_?[Rr]ng|PlayerRng|Seed|StringSeed|_seed|CombatTargets|CombatCardSelection)""");

    /// <summary>Lines that name it to read the game's code, not its state: telling apart a method's overloads by an Rng parameter.</summary>
    private static readonly string[] Reviewed = { "ParameterTypes.Contains(\"Rng\")" };

    [Fact]
    public void NoSourceReachesTheRngByName()
    {
        var found = RepoFiles.Sources("mods").Concat(RepoFiles.Sources("core"))
            .SelectMany(source => source.Lines.Select((line, index) => (source.Path, Line: index + 1, Text: line.Trim())))
            .Where(line => !line.Text.StartsWith("//", StringComparison.Ordinal) && ByName.IsMatch(line.Text) && !Reviewed.Any(line.Text.Contains))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text}").ToList();
        Assert.True(found.Count == 0, "A string naming the game's RNG or seed (never read them, not even by reflection):\n" + string.Join("\n", found));
    }

    [Fact]
    public void TheBanListNamesTheRngAndTheSeed()
    {
        var banned = File.ReadAllLines(Path.Combine(RepoFiles.Root(), "BannedSymbols.txt"));
        foreach (var symbol in new[] { "T:MegaCrit.Sts2.Core.Random.Rng;", "P:MegaCrit.Sts2.Core.Runs.IRunState.Rng;", "P:MegaCrit.Sts2.Core.Entities.Players.Player.PlayerRng;" })
            Assert.Contains(banned, line => line.StartsWith(symbol, StringComparison.Ordinal));
    }
}
