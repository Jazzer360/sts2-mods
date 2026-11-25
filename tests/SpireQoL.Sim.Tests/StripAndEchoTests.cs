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
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 30, block: 7), Enemy(id: 1, hp: 30, block: 5) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Artifact, amount: 1);
        var after = After(state, Play(Card("Expose", SimCardType.Skill), target: 0, plays: 1,
            new StripBlockAction(RemoveArtifact: true), new ApplyPowerAction(PowerId.Vulnerable, Amount: 2) { Debuff = true })).Single();
        Assert.Equal(0, after.Enemy(id: 0)!.Block);
        Assert.Equal(5, after.Enemy(id: 1)!.Block); // only its target
        Assert.False(after.Enemy(id: 0)!.Powers.Has(PowerId.Artifact));
        Assert.Equal(2, after.Enemy(id: 0)!.Powers[PowerId.Vulnerable]);
    }

    [Fact]
    public void ExposeBreakingBurrowedBlockStunsIt()
    {
        // LoseBlock runs AfterBlockBroken when the block was above 0: Burrowed stuns its owner.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 30, block: 10) } };
        // Burrowed on its one monster, a Tunneler (BurrowedPower.AfterBlockBroken acts on a Tunneler only).
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Burrowed, amount: 1);
        state.Enemies[0].Monster = "Tunneler";
        state.Enemies[0].Hits = new List<int> { 12 };
        var after = After(state, Play(Card("Expose", SimCardType.Skill), target: 0, plays: 1, new StripBlockAction(RemoveArtifact: true))).Single();
        Assert.True(after.Enemy(id: 0)!.Stunned);
    }

    [Fact]
    public void EchoingSlashRepeatsOncePerKillUntilARoundKillsNone()
    {
        // The report's fight: 9 per hit on Gardeners at 16, 28, 32 and 5 HP. Round 1 kills the 5; round 2 kills the 7 left of
        // the 16; round 3 kills none: 1 and 5 left.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 16), Enemy(id: 1, hp: 28), Enemy(id: 2, hp: 32), Enemy(id: 3, hp: 5) } };
        var after = After(state, Play(Card("EchoingSlash"), target: Who.None, plays: 1, Attack(baseDamage: 9, mode: SimPlay.Target.All) with { RepeatPerKill = true })).Single();
        Assert.False(after.Enemy(id: 0)!.Alive);
        Assert.Equal(1, after.Enemy(id: 1)!.Hp);
        Assert.Equal(5, after.Enemy(id: 2)!.Hp);
        Assert.False(after.Enemy(id: 3)!.Alive);
    }

    [Fact]
    public void TwoKillsInOneRoundGiveTwoMoreRounds()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 5), Enemy(id: 1, hp: 5), Enemy(id: 2, hp: 30) } };
        var after = After(state, Play(Card("EchoingSlash"), target: Who.None, plays: 1, Attack(baseDamage: 9, mode: SimPlay.Target.All) with { RepeatPerKill = true })).Single();
        Assert.Equal(3, after.Enemy(id: 2)!.Hp); // 3 rounds: 30 - 27
        // Without the flag, one round only.
        var plain = After(state, Play(Card("Whirl"), target: Who.None, plays: 1, Attack(baseDamage: 9, mode: SimPlay.Target.All))).Single();
        Assert.Equal(21, plain.Enemy(id: 2)!.Hp);
    }
}
