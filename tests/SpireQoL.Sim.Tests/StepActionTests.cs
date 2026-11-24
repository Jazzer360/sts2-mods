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
    public void WrestDrawsOnlyOnThePlayThatFindsYourArrowsIn()
    {
        // Wrest.OnPlay: if your arrow Impales the target, pull them all out (ArrowCmd.DislodgeArrow while it does), then draw. Replayed, its
        // second play asks the test on the board the first left: no arrow of yours, no draw.
        var yoursIn = new TargetImpaledCondition(ByOwner: true);
        var wrest = Play(Card("Wrest", SimCardType.Skill), target: 0, plays: 2,
            new WhenAction(yoursIn, new SimAction[] { new DislodgeAllAction(), new CardEventAction(CardEvent.Drawn, Count: 2) { Indirect = true, JoinsHand = true } }));
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Impaled, amount: 1, data: 3);
        Assert.Equal(2, Assert.Single(After(state, wrest)).Fired[SimKeys.PlayEvent(CardEvent.Drawn)]);
    }

    [Fact]
    public void FisticuffsGainsTheBlockItsAttackDealtAsItRuns()
    {
        // Fisticuffs.OnPlay: its attack, then GainBlock(you, the attack's results' TotalDamage + OverkillDamage, Move). What lands is what
        // it dealt (block taken and HP, overkill too), with Dexterity on it: 7 into 3 block and 4 HP, plus 2 Dexterity.
        var fisticuffs = Play(Card("Fisticuffs"), target: 0, plays: 2, Attack(baseDamage: 7),
            new GainBlockAction(Times: 1, Base: 0, SimProps.Move) { BaseFrom = SimBlockBase.AttackDealt });
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.Enemies[0].Block = 3;
        state.You.Powers = state.You.Powers.With(PowerId.Dexterity, amount: 2);
        // Two plays: each gains what its own attack dealt.
        Assert.Equal(2 * (7 + 2), Assert.Single(After(state, fisticuffs)).You.Block);
    }

    [Fact]
    public void EntrenchDoublesTheBlockYouHaveAsItRuns()
    {
        // Entrench.OnPlay: GainBlock(you, your block, Unpowered | Move): a replay doubles the block the first play left.
        var entrench = Play(Card("Entrench", SimCardType.Skill), target: Who.None, plays: 2,
            new GainBlockAction(Times: 1, Base: 0, SimProps.Unpowered | SimProps.Move) { BaseFrom = SimBlockBase.OwnBlock });
        var state = new SimState();
        state.You.Block = 5;
        Assert.Equal(20, Assert.Single(After(state, entrench)).You.Block);
    }

    [Fact]
    public void MimicGainsItsTargetsBlockAsItRuns()
    {
        // Mimic.OnPlay: GainBlock(you, CalculatedBlock on its target: CalculationBase + CalculationExtra × the target's block).
        var mimic = Play(Card("Mimic", SimCardType.Skill), target: Who.Teammate(teammateIndex: 0), plays: 1,
            new GainBlockAction(Times: 1, Base: 0, SimProps.Move) { BaseFrom = SimBlockBase.TargetBlock, PerBlock = 1 });
        var state = new SimState();
        state.Teammates.Add(new SimPlayer { Hp = 50, MaxHp = 50, Block = 9 });
        Assert.Equal(9, Assert.Single(After(state, mimic)).You.Block);
    }

    [Fact]
    public void ACardMadeIntoAFullHandGoesToTheDiscardPile()
    {
        // CardPileCmd.Add to the hand: a full hand sends the card to the discard pile, where a shuffle later in the turn finds it.
        var full = new SimState();
        full.You.HandCount = 10;
        full.You.Draw = new SimDrawPile();
        full.You.MadeCards = new Dictionary<string, SimDrawCard> { ["Shiv"] = new SimDrawCard("Shiv") };
        var shiv = Play(Card("BladeDance", SimCardType.Skill), target: Who.None, plays: 1,
            new CardEventAction(CardEvent.Generated, Count: 1) { Indirect = true, JoinsHand = true, MadeCard = "Shiv" });
        var after = Assert.Single(After(full, shiv));
        Assert.Equal(10, after.You.HandCount);
        Assert.Equal(1, after.You.Draw!.DiscardCount);
        var room = new SimState();
        room.You.HandCount = 5;
        room.You.Draw = new SimDrawPile();
        room.You.MadeCards = full.You.MadeCards;
        var roomy = Assert.Single(After(room, shiv));
        Assert.Equal(6, roomy.You.HandCount);
        Assert.Equal(0, roomy.You.Draw!.DiscardCount);
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
