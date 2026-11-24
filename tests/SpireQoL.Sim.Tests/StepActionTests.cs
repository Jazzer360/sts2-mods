using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What a play's steps translate into (decision #193: each command step its own action, its condition the code's test asked as the
/// step runs): a shot's result (Hobbling Shot), the target's HP (Judgement), your own power (Cho Ki), the target's amount of another power
/// (Dominate), and an evoke that keeps its orb (Dualcast's first, OrbCmd.EvokeNext with dequeue false).
/// </summary>
public class StepActionTests
{
    [Fact]
    public void HobblingShotsDebuffsLandOnlyWhenItsShotHadAnArrow()
    {
        // HobblingShot.OnPlay: if (await ArrowCmd.ShootArrowAtTarget(...)) its Weak; the shot's result is whether it had an arrow.
        var hadArrow = new ShotHadArrowCondition();
        var shot = Play(Card("HobblingShot", tags: SimCardTag.Shoot), target: 0, plays: 2, new ShootAction(), Attack(baseDamage: 5) with { NeedsArrow = true }, new ImpaleAction(),
            new WhenAction(hadArrow, new SimAction[] { new ApplyPowerAction(PowerId.Weak, Amount: 1) { Debuff = true } }));
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        Assert.Equal(0, Assert.Single(After(state, shot)).Enemy(id: 0)!.Powers[PowerId.Weak]);
        // One arrow: the first play shoots it and weakens; the second finds none (it's in the target) and doesn't.
        state.You.Arrows = 1;
        var one = Assert.Single(After(state, shot));
        Assert.Equal(1, one.Enemy(id: 0)!.Powers[PowerId.Weak]);
        Assert.Equal(45, one.Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void JudgementAsksTheTargetsHpAsItsStepRuns()
    {
        // Judgement.OnPlay: if (target.IsAlive && target.CurrentHp <= Threshold) SetCurrentHp(target, 0): the test is the step's condition.
        var low = new AllCondition(new SimCondition[]
        {
            new CountCondition(SimCounted.TargetHp, SimCompare.Greater, Than: 0),
            new CountCondition(SimCounted.TargetHp, SimCompare.LessOrEqual, Than: 30),
        });
        SimScript Judgement(int target) => Play(Card("Judgement", SimCardType.Skill), target, plays: 1,
            new WhenAction(low, new SimAction[] { new ExecuteAction(int.MaxValue) }));
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 30), Enemy(id: 1, hp: 31) } };
        Assert.False(Assert.Single(After(state, Judgement(target: 0))).Enemy(id: 0)!.Alive);
        Assert.True(Assert.Single(After(state, Judgement(target: 1))).Enemy(id: 1)!.Alive);
        // A hit first brings the second one down to it: the test is asked after it.
        var hitFirst = Play(Card("Judgement", SimCardType.Skill), target: 1, plays: 1, Attack(baseDamage: 1),
            new WhenAction(low, new SimAction[] { new ExecuteAction(int.MaxValue) }));
        Assert.False(Assert.Single(After(state, hitFirst)).Enemy(id: 1)!.Alive);
    }

    [Fact]
    public void ChoKiDoublesYourStretchOnlyWhenYouHaveSome()
    {
        // ChoKi.OnPlay: stretchAmount = your Stretch; if (stretchAmount > 0) Apply<StretchPower>(you, stretchAmount).
        var hasStretch = new CountCondition(SimCounted.OwnerPower, SimCompare.Greater, Than: 0) { Power = PowerId.Stretch };
        var choKi = Play(Card("ChoKi", SimCardType.Skill), target: Who.None, plays: 1,
            new WhenAction(hasStretch, new SimAction[] { new ApplyPowerAction(PowerId.Stretch, Amount: 0) { To = ApplyPowerAction.Where.You, CopiesOwn = true } }));
        Assert.Equal(0, Assert.Single(After(new SimState(), choKi)).You.Powers[PowerId.Stretch]);
        var state = new SimState();
        state.You.Powers = state.You.Powers.With(PowerId.Stretch, amount: 3);
        Assert.Equal(6, Assert.Single(After(state, choKi)).You.Powers[PowerId.Stretch]);
    }

    [Fact]
    public void DominateGivesStrengthEqualToTheVulnerableItLeaves()
    {
        // Dominate.OnPlay: Apply<VulnerablePower>(target, 1), then Apply<StrengthPower>(you, target's Vulnerable as it is then).
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Vulnerable, amount: 2);
        var dominate = Play(Card("Dominate", SimCardType.Skill), target: 0, plays: 1,
            new ApplyPowerAction(PowerId.Vulnerable, Amount: 1) { Debuff = true },
            new ApplyPowerAction(PowerId.Strength, Amount: 0) { To = ApplyPowerAction.Where.You, TargetsAmountOf = PowerId.Vulnerable });
        Assert.Equal(3, Assert.Single(After(state, dominate)).You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void AnEvokeThatKeepsItsOrbEvokesItAgainNext()
    {
        // Dualcast.OnPlay: EvokeNext(dequeue: false), then EvokeNext(): the front orb evoked twice, then gone.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 }, new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } };
        var dualcast = Play(Card("Dualcast", SimCardType.Skill), target: Who.None, plays: 1,
            new OrbAction(OrbAction.Op.EvokeFront) { Keeps = true }, new OrbAction(OrbAction.Op.EvokeFront));
        var after = Assert.Single(After(state, dualcast));
        Assert.Equal(10, after.You.Block);
        Assert.Single(after.You.Orbs);
    }
}
