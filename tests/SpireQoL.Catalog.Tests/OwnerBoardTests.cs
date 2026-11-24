using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Powers;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The owner board (OwnerBoard): which kinds of creature each power can be on, read from where both branches' code applies it, is the file the
/// simulator's parity test reads (tests/SpireQoL.Sim.Tests/ReadModels/power-owners.json; SPIREQOL_UPDATE_SNAPSHOT=1 regenerates it). A kind
/// on either branch counts, so a read model is compared on every owner either branch can give it and on no other.
/// </summary>
public class OwnerBoardTests
{
    private static string BoardFile => Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Sim.Tests", "ReadModels", "power-owners.json");

    /// <summary>
    /// Places that apply a power their code doesn't name (no ModelDb.Power&lt;T&gt;, no type argument), each with why that gives no power a new
    /// kind of owner. A new one fails until it's reviewed here.
    /// </summary>
    private static readonly Dictionary<string, string> UnnamedReviewed = new(StringComparer.Ordinal)
    {
        ["Misery.OnPlay"] = "copies each power on the enemy it hit onto the other enemies (foreach over the target's Powers, onto HittableEnemies): a power already on an enemy, onto an enemy",
    };

    [Fact]
    public void TheBoardIsTheCodes()
    {
        var branches = new[] { BranchCode.BetaPath, BranchCode.MainPath }.Where(File.Exists).ToList();
        if (branches.Count == 0) return; // no decompile here (docs/DEVELOPMENT.md "Decompiled game source")
        var kinds = new SortedDictionary<string, OwnerKinds>(StringComparer.Ordinal);
        foreach (var path in branches)
        {
            var code = BranchCode.Of(path);
            var board = code.Board();
            Assert.Equal(UnnamedReviewed.Keys.Order(StringComparer.Ordinal), board.UnnamedPowers);
            foreach (var (power, owners) in board.Powers) kinds[power] = kinds.GetValueOrDefault(power) | owners;
        }
        // One power a line, its kinds in a list: {"StranglePower": ["Enemy"], ...}.
        var lines = kinds.Select(pair => $"  {JsonSerializer.Serialize(pair.Key)}: {JsonSerializer.Serialize(Enum.GetValues<OwnerKinds>().Where(kind => kind is not (OwnerKinds.None or OwnerKinds.Any) && pair.Value.HasFlag(kind)).Select(kind => kind.ToString()))}");
        var text = "{\n" + string.Join(",\n", lines) + "\n}\n";
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(BoardFile, text);
            return;
        }
        Assert.True(File.Exists(BoardFile), $"missing {BoardFile}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        Assert.True(File.ReadAllText(BoardFile).ReplaceLineEndings("\n") == text, $"{BoardFile} isn't the board both branches' code gives: regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1 and explain the change");
    }

    [Fact]
    public void TheBoardReadsWhatTheGameGives()
    {
        // Known from the code: Strangle on an enemy only (the card aims it at one, and Mad Science's rider at its target), Sneaky on a player
        // only (the multiplayer card puts it on its player), Impaled on an enemy only (an arrow in the shot's target), Concoct on a player.
        if (!File.Exists(BranchCode.BetaPath)) return;
        var code = BranchCode.Of(BranchCode.BetaPath);
        var board = code.Board();
        Assert.Equal(OwnerKinds.Enemy, board.Powers[nameof(StranglePower)]);
        Assert.Equal(OwnerKinds.Player, board.Powers[nameof(SneakyPower)]);
        Assert.Equal(OwnerKinds.Enemy, board.Powers[nameof(ImpaledPower)]);
        Assert.Equal(OwnerKinds.Player, board.Powers[nameof(ConcoctPower)]);
        Assert.Equal(OwnerKinds.Enemy, board.Powers[nameof(EnragePower)]);
        Assert.True(board.Powers[nameof(VulnerablePower)].HasFlag(OwnerKinds.Player | OwnerKinds.Enemy));
    }
}
