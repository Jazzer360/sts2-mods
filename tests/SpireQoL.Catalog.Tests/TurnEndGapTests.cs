using SpireQoL.Diagnostics;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The enemies' side's turn end's listeners (decision #303): every power and relic class on both game branches whose code acts as a side's turn ends
/// (a turn's-end hook it overrides) is followed there (a hand-written model, or read whole and compiled) or listed as a gap with why
/// (ReadModels.TurnEndGaps: the state after that turn's end is unknown where it's on the board), so a class a game update adds can't act there
/// unseen. The simulator tests' copy (tests/SpireQoL.Sim.Tests/ReadModels/turn-end-gaps.json) is the installed game's (SPIREQOL_UPDATE_SNAPSHOT=1
/// regenerates it).
/// </summary>
public class TurnEndGapTests
{
    private static string GapsFile => Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Sim.Tests", "ReadModels", "turn-end-gaps.json");

    private static readonly string[] TurnEndHooks = { "BeforeSideTurnEndVeryEarly", "BeforeSideTurnEndEarly", "BeforeSideTurnEnd", "AfterSideTurnEnd", "AfterSideTurnEndLate" };

    [Theory]
    [MemberData(nameof(HookProgramTests.Branches), MemberType = typeof(HookProgramTests))]
    public void EveryClassActingAtATurnsEndIsFollowedOrMarked(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile here (docs/DEVELOPMENT.md "Decompiled game source")
        var code = BranchCode.Of(path);
        var types = code.Assembly.GetTypes();
        var read = code.Specs().Where(spec => spec.Unread == null).Select(spec => spec.Name).ToHashSet(StringComparer.Ordinal);
        var unread = code.Specs().Where(spec => spec.Unread != null).Select(spec => spec.Name).ToHashSet(StringComparer.Ordinal);
        var gaps = ReadModels.TurnEndGaps(types, code.Read).ToDictionary(gap => gap.Name, StringComparer.Ordinal);
        var unseen = new List<string>();
        foreach (var type in HookReader.Powers(types).Concat(HookReader.Relics(types)))
        {
            // Its own override of a turn's-end hook, from its code (not from its name or a list).
            if (!HookReader.Hooks(type).Any(hook => TurnEndHooks.Contains(hook.Name))) continue;
            if (SimModels.IsHandWritten(type.Name) || read.Contains(type.Name) || unread.Contains(type.Name) || gaps.ContainsKey(type.Name)) continue;
            unseen.Add(type.Name);
        }
        Assert.True(unseen.Count == 0, $"{branch}: acts as a side's turn ends, neither followed nor marked: {string.Join(", ", unseen)}");
        // A listed class isn't followed: no read model, no hand-written one.
        Assert.DoesNotContain(gaps.Keys, name => read.Contains(name) || SimModels.IsHandWritten(name));
        // Every gap that acts says why it isn't followed.
        Assert.All(gaps.Values.Where(gap => gap.ActsForAnEnemy || gap.ActsForAPlayer), gap => Assert.Contains("isn't simulated there", gap.Why, StringComparison.Ordinal));
    }

    /// <summary>
    /// Doom (DoomPower.BeforeSideTurnEnd: side != CombatSide.Player, its owner taking part and doomed) kills as the enemies' side's turn ends, on an
    /// enemy it's on; No Draw's AfterSideTurnEnd waits on its owner taking part (participants.Contains(base.Owner)), which no player does then.
    /// </summary>
    [Fact]
    public void WhoActsThereIsReadFromTheHooks()
    {
        var gaps = ReadModels.TurnEndGaps(CoverageLedger.ModelTypes(), RuntimeCode.Read).ToDictionary(gap => gap.Name, StringComparer.Ordinal);
        Assert.True(gaps[PowerId.Doom.Name].ActsForAnEnemy);
        Assert.Contains("BeforeSideTurnEnd", gaps[PowerId.Doom.Name].Why, StringComparison.Ordinal);
        Assert.False(gaps[PowerId.NoDraw.Name].ActsForAPlayer);
        Assert.True(gaps[PowerId.BattlewornDummyTimeLimit.Name].ActsForAnEnemy);
    }

    [Fact]
    public void TheSimulatorTestsGapsAreTheInstalledGamesCode()
    {
        var actual = HookProgramJson.WriteTurnEndGaps(ReadModels.TurnEndGaps(CoverageLedger.ModelTypes(), RuntimeCode.Read));
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
