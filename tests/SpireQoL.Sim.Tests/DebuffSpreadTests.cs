using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A spread of the play's target's debuffs onto the other enemies (Misery.OnPlay, decision #293): before its attack the code takes each power on
/// the target whose amount is a debuff (TypeForCurrentAmount == Debuff, PowerModel.GetTypeForAmount: its Type, or a counter that can go below 0
/// below 0, read from each class's code: ReadModels/power-rules.json), with its amount; after it, onto every hittable enemy but the target, enemy
/// by enemy, each as PowerCmd applies it (ModifyAmount of one already there, Apply of a clone: Artifact takes a debuff either way).
/// </summary>
public class DebuffSpreadTests
{
    private static SimEnemy Enemy(int id, int hp = 50) => new() { Id = id, Name = $"E{id}", Hp = hp };

    /// <summary>The state right after the play: the enemies' turn's end ticks their Vulnerable and Weak down (WeakPower.AfterSideTurnEnd).</summary>
    private static SimState AfterThePlay(SimState state, SimPlay play)
    {
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        return Assert.Single(branches);
    }

    private static SimPlay Misery(int damage = 7) => new()
    {
        Name = "Misery", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 0,
        Body = { new KeepTargetDebuffsAction(), ScriptBuilder.Attack(baseDamage: damage, mode: SimPlay.Target.Single), new SpreadKeptDebuffsAction() },
    };

    [Fact]
    public void TheTargetsDebuffsGoOntoEveryOtherEnemy()
    {
        // Vulnerable 2 and Weak 1 on the target, Strength 3 (a buff) not taken; Weak 1 already on the other stacks to 2; the target keeps its own.
        var state = new SimState { Enemies = { Enemy(id: 0), Enemy(id: 1), Enemy(id: 2) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Vulnerable, amount: 2).With(PowerId.Weak, amount: 1).With(PowerId.Strength, amount: 3);
        state.Enemies[1].Powers = PowerSet.Empty.With(PowerId.Weak, amount: 1);
        var branch = AfterThePlay(state, Misery());
        Assert.Null(branch.Unmodelled);
        Assert.Equal(2, branch.Enemy(id: 1)!.Powers[PowerId.Vulnerable]);
        Assert.Equal(2, branch.Enemy(id: 1)!.Powers[PowerId.Weak]);
        Assert.Equal(0, branch.Enemy(id: 1)!.Powers[PowerId.Strength]);
        Assert.Equal(2, branch.Enemy(id: 2)!.Powers[PowerId.Vulnerable]);
        Assert.Equal(1, branch.Enemy(id: 2)!.Powers[PowerId.Weak]);
        Assert.Equal(2, branch.Enemy(id: 0)!.Powers[PowerId.Vulnerable]);
        Assert.Equal(1, branch.Enemy(id: 0)!.Powers[PowerId.Weak]);
    }

    [Fact]
    public void StrengthBelowZeroIsADebuffAndArtifactTakesTheFirst()
    {
        // StrengthPower can go below 0 and is a counter: -2 is a debuff (GetTypeForAmount). The other enemy's Artifact takes the first debuff
        // spread (Vulnerable, the target's first), and the Strength lands.
        var state = new SimState { Enemies = { Enemy(id: 0), Enemy(id: 1) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Vulnerable, amount: 1).With(PowerId.Strength, amount: -2);
        state.Enemies[1].Powers = PowerSet.Empty.With(PowerId.Artifact, amount: 1);
        var branch = Assert.Single(SimEngine.Run(state, Misery()).Branches);
        Assert.Equal(0, branch.Enemy(id: 1)!.Powers[PowerId.Vulnerable]);
        Assert.Equal(-2, branch.Enemy(id: 1)!.Powers[PowerId.Strength]);
        Assert.False(branch.Enemy(id: 1)!.Powers.Has(PowerId.Artifact));
    }

    [Fact]
    public void TheDebuffsAreTakenBeforeTheAttackAndSpreadAfterIt()
    {
        // The target dies to the attack: its debuffs, taken before it, still spread; an enemy the attack killed isn't hittable and gets none.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 5), Enemy(id: 1) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Vulnerable, amount: 3);
        var branch = AfterThePlay(state, Misery(damage: 10));
        Assert.False(branch.Enemy(id: 0)!.Alive);
        Assert.Equal(3, branch.Enemy(id: 1)!.Powers[PowerId.Vulnerable]);
    }

    [Fact]
    public void NoDebuffsOnTheTargetSpreadsNothing()
    {
        var state = new SimState { Enemies = { Enemy(id: 0), Enemy(id: 1) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Strength, amount: 2);
        var branch = Assert.Single(SimEngine.Run(state, Misery()).Branches);
        Assert.Equal(0, branch.Enemy(id: 1)!.Powers.Count);
    }
}
