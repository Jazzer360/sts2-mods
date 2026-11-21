using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>Expose taking its target's block and Artifact, and Echoing Slash repeating per kill (decompile v0.113.1).</summary>
public class StripAndEchoTests
{
    [Fact]
    public void ExposeTakesTheTargetsBlockAndArtifactBeforeVulnerable()
    {
        // Expose.OnPlay: CreatureCmd.LoseBlock(all of it), PowerCmd.Remove<ArtifactPower>, then Vulnerable.
        var state = new SimState { Enemies = { Enemy(0, 30, block: 7), Enemy(1, 30, block: 5) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Artifact, 1);
        var after = After(state, Play(Card("Expose", SimCardType.Skill), 0, 1,
            new StripBlockAction(RemoveArtifact: true), new ApplyPowerAction(PowerId.Vulnerable, 2) { Debuff = true })).Single();
        Assert.Equal(0, after.Enemy(0)!.Block);
        Assert.Equal(5, after.Enemy(1)!.Block); // only its target
        Assert.False(after.Enemy(0)!.Powers.Has(PowerId.Artifact));
        Assert.Equal(2, after.Enemy(0)!.Powers[PowerId.Vulnerable]);
    }

    [Fact]
    public void ExposeBreakingBurrowedBlockStunsIt()
    {
        // LoseBlock runs AfterBlockBroken when the block was above 0: Burrowed stuns its owner.
        var state = new SimState { Enemies = { Enemy(0, 30, block: 10) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Burrowed, 1);
        state.Enemies[0].Hits = new List<int> { 12 };
        var after = After(state, Play(Card("Expose", SimCardType.Skill), 0, 1, new StripBlockAction(RemoveArtifact: true))).Single();
        Assert.True(after.Enemy(0)!.Stunned);
    }

    [Fact]
    public void EchoingSlashRepeatsOncePerKillUntilARoundKillsNone()
    {
        // The report's fight: 9 per hit on Gardeners at 16, 28, 32 and 5 HP. Round 1 kills the 5; round 2 kills the 7 left of
        // the 16; round 3 kills none: 1 and 5 left.
        var state = new SimState { Enemies = { Enemy(0, 16), Enemy(1, 28), Enemy(2, 32), Enemy(3, 5) } };
        var after = After(state, Play(Card("EchoingSlash"), -1, 1, Attack(9, mode: SimPlay.Target.All) with { RepeatPerKill = true })).Single();
        Assert.False(after.Enemy(0)!.Alive);
        Assert.Equal(1, after.Enemy(1)!.Hp);
        Assert.Equal(5, after.Enemy(2)!.Hp);
        Assert.False(after.Enemy(3)!.Alive);
    }

    [Fact]
    public void TwoKillsInOneRoundGiveTwoMoreRounds()
    {
        var state = new SimState { Enemies = { Enemy(0, 5), Enemy(1, 5), Enemy(2, 30) } };
        var after = After(state, Play(Card("EchoingSlash"), -1, 1, Attack(9, mode: SimPlay.Target.All) with { RepeatPerKill = true })).Single();
        Assert.Equal(3, after.Enemy(2)!.Hp); // 3 rounds: 30 - 27
        // Without the flag, one round only.
        var plain = After(state, Play(Card("Whirl"), -1, 1, Attack(9, mode: SimPlay.Target.All))).Single();
        Assert.Equal(21, plain.Enemy(2)!.Hp);
    }
}
