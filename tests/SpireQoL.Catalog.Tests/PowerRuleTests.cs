using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Diagnostics;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Every power class's PowerCmd rules, read from its code (PowerRule, decision #234): whether it can go below 0, and its scaling for the players.
/// The simulator tests' copy (tests/SpireQoL.Sim.Tests/ReadModels/power-rules.json) is the installed game's (SPIREQOL_UPDATE_SNAPSHOT=1
/// regenerates it), and both branches' code reads every rule.
/// </summary>
public class PowerRuleTests
{
    private static string RulesFile => Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Sim.Tests", "ReadModels", "power-rules.json");

    [Fact]
    public void TheSimulatorTestsRulesAreTheInstalledGamesCode()
    {
        var actual = HookProgramJson.WriteRules(ReadModels.PowerRules(CoverageLedger.ModelTypes(), RuntimeCode.Read));
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(RulesFile, actual.ReplaceLineEndings("\n") + "\n");
            return;
        }
        Assert.True(File.Exists(RulesFile), $"missing {RulesFile}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        Assert.True(File.ReadAllText(RulesFile).ReplaceLineEndings("\n").TrimEnd() == actual.ReplaceLineEndings("\n").TrimEnd(),
            $"{RulesFile} isn't what the installed game's code reads: regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1 and explain the change");
    }

    [Theory]
    [MemberData(nameof(HookProgramTests.Branches), MemberType = typeof(HookProgramTests))]
    public void EveryPowersRulesReadOnEachBranch(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile here (docs/DEVELOPMENT.md "Decompiled game source")
        var code = BranchCode.Of(path);
        var rules = ReadModels.PowerRules(code.Assembly.GetTypes(), code.Read);
        Assert.Empty(rules.Where(rule => rule.Unread != null).Select(rule => $"{rule.Name}: {rule.Unread}"));
        // Every class PowerCmd.Apply scales has its scaling read, and none is kept in copies (the engine scales a new one where none is there).
        var scaled = rules.Where(rule => rule.ScaledForPlayers != null).Select(rule => rule.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(nameof(SlipperyPower), scaled);
        Assert.Contains(nameof(PlowPower), scaled);
        Assert.Contains(nameof(StrengthPower), rules.Where(rule => rule.AllowNegative).Select(rule => rule.Name));
        foreach (var name in scaled)
        {
            var type = code.Assembly.GetTypes().Single(candidate => candidate.Name == name && candidate.Namespace == "MegaCrit.Sts2.Core.Models.Powers");
            Assert.Equal(PowerCopies.One, HookReader.Read(type, code.Read).Spec.Copies);
        }
    }

    [Fact]
    public void ScalingReadsAsTheGameScales()
    {
        // SlipperyPower: amount × players; SkittishPower: amount × (1 + (players − 1) × 0.5); PowerModel's own (Plow): amount × players × the
        // fight's scaling (MultiplayerScalingModel.GetMultiplayerScaling).
        var state = new SimState { Teammates = { new SimPlayer { Hp = 50 } }, EncounterScaling = 1.2m };
        Assert.Equal(6, SimModels.ScaledForPlayers(state, PowerId.Slippery, amount: 3));
        Assert.Equal(4, SimModels.ScaledForPlayers(state, PowerId.Skittish, amount: 3));
        Assert.Equal(24, SimModels.ScaledForPlayers(state, PowerId.Plow, amount: 10));
        Assert.Equal(5, SimModels.ScaledForPlayers(state, PowerId.Strength, amount: 5));
    }
}
