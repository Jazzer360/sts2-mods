using SpireQoL.Diagnostics;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The players' next turn's start's listeners (decision #306): every power and relic class on both game branches whose code acts as a turn starts
/// (a turn's-start or SetupPlayerTurn hook it overrides, or the AutoPrePlay phase's) is followed there (read whole and compiled, or a hand-written
/// model answering that hook) or listed as a gap with why (ReadModels.TurnStartGaps: the state after that turn's start is unknown where it's on
/// the board), so a class a game update adds can't act there unseen. The simulator tests' copy (tests/SpireQoL.Sim.Tests/ReadModels/turn-start-gaps.json)
/// is the installed game's (SPIREQOL_UPDATE_SNAPSHOT=1 regenerates it).
/// </summary>
public class TurnStartGapTests
{
    private static string GapsFile => Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Sim.Tests", "ReadModels", "turn-start-gaps.json");

    private static readonly string[] TurnStartHooks =
    {
        "BeforeSideTurnStart", "AfterBlockCleared", "AfterEnergyReset", "AfterEnergyResetLate", "BeforeHandDraw", "BeforeHandDrawLate",
        "AfterPlayerTurnStartEarly", "AfterPlayerTurnStart", "AfterPlayerTurnStartLate", "AfterSideTurnStart", "AfterSideTurnStartLate", "AfterAutoPrePlayPhaseEntered",
    };

    [Theory]
    [MemberData(nameof(HookProgramTests.Branches), MemberType = typeof(HookProgramTests))]
    public void EveryClassActingAtATurnsStartIsFollowedOrMarked(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile here (docs/DEVELOPMENT.md "Decompiled game source")
        var code = BranchCode.Of(path);
        var types = code.Assembly.GetTypes();
        var read = code.Specs().Where(spec => spec.Unread == null).Select(spec => spec.Name).ToHashSet(StringComparer.Ordinal);
        var unread = code.Specs().Where(spec => spec.Unread != null).Select(spec => spec.Name).ToHashSet(StringComparer.Ordinal);
        var gaps = ReadModels.TurnStartGaps(types, code.Read).ToDictionary(gap => gap.Name, StringComparer.Ordinal);
        var unseen = new List<string>();
        foreach (var type in HookReader.Powers(types).Concat(HookReader.Relics(types)))
        {
            // Its own override of a turn's-start hook, from its code (not from its name or a list).
            var starts = HookReader.Hooks(type).Select(hook => hook.Name).Where(TurnStartHooks.Contains).ToList();
            if (starts.Count == 0 || gaps.ContainsKey(type.Name) || unread.Contains(type.Name)) continue;
            // A hand-written model answers each of them; a read one runs every hook it reads (one it runs no read model's isn't read whole).
            if (SimModels.HandWritten(type.Name) is { } handWritten ? starts.All(handWritten.Overrides) || !ActsThere(type, code) : read.Contains(type.Name)) continue;
            unseen.Add(type.Name);
        }
        Assert.True(unseen.Count == 0, $"{branch}: acts as your turn starts, neither followed nor marked: {string.Join(", ", unseen)}");
        // A listed class isn't followed there: no read model.
        Assert.DoesNotContain(gaps.Keys, name => read.Contains(name) && !SimModels.IsHandWritten(name));
        // Every gap that acts says why it isn't followed.
        Assert.All(gaps.Values.Where(gap => gap.ActsForAnEnemy || gap.ActsForAPlayer), gap => Assert.Contains("isn't simulated there", gap.Why, StringComparison.Ordinal));
    }

    /// <summary>A hand-written class not listed is one whose unanswered turn's-start hooks never act there (the scan read them so).</summary>
    private static bool ActsThere(Type type, BranchCode code) =>
        ReadModels.TurnStartGaps(new[] { type }, code.Read).Any(gap => gap.ActsForAnEnemy || gap.ActsForAPlayer);

    /// <summary>
    /// Mayhem (MayhemPower.AfterAutoPrePlayPhaseEntered: it plays the top card of its owner's draw pile) acts as its owner's turn starts, where no
    /// read model runs: a gap for a player. Plating's BeforeSideTurnStart is read and runs itself there: not listed.
    /// </summary>
    [Fact]
    public void WhoActsThereIsReadFromTheHooks()
    {
        var gaps = ReadModels.TurnStartGaps(CoverageLedger.ModelTypes(), RuntimeCode.Read).ToDictionary(gap => gap.Name, StringComparer.Ordinal);
        Assert.True(gaps[PowerId.Mayhem.Name].ActsForAPlayer);
        Assert.Contains("AfterAutoPrePlayPhaseEntered", gaps[PowerId.Mayhem.Name].Why, StringComparison.Ordinal);
        Assert.False(gaps.ContainsKey(PowerId.Plating.Name));
    }

    [Fact]
    public void TheSimulatorTestsGapsAreTheInstalledGamesCode()
    {
        var actual = HookProgramJson.WriteTurnStartGaps(ReadModels.TurnStartGaps(CoverageLedger.ModelTypes(), RuntimeCode.Read));
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(GapsFile, actual.ReplaceLineEndings("\n") + "\n");
            return;
        }
        Assert.True(File.Exists(GapsFile), $"missing {GapsFile}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        Assert.True(File.ReadAllText(GapsFile).ReplaceLineEndings("\n").TrimEnd() == actual.ReplaceLineEndings("\n").TrimEnd(),
            $"{GapsFile} isn't what the installed game's code reads: regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1 and explain the change");
    }
}
