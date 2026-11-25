using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// ImbalancedPower read from its code (decision #271): AfterDamageGiven, `dealer == base.Owner &amp;&amp; result.WasFullyBlocked`: Flash(), then
/// `base.Owner.Monster is BowlbugRock bowlbugRock` ? bowlbugRock.IsOffBalance = true : CreatureCmd.Stun(base.Owner). Any hit its owner deals that a
/// block takes whole sets it off (a teammate's too, a later hit of its attack too), never one that gets HP through; on Bowlbug Rock it's its own state,
/// which its Headbutt reads after its attack (`if (IsOffBalance) await Stun()`: its next move the stunned one).
/// </summary>
public class ImbalancedTests
{
    private static SimState Fight(int yourBlock, params int[] hits)
    {
        var state = new SimState { TurnNumber = 1 };
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Block = yourBlock;
        var enemy = new SimEnemy { Id = 0, Name = "Bowlbug", Hp = 40, Hits = hits.ToList(), IntendsToAttack = true };
        enemy.Powers = enemy.Powers.With(PowerId.Imbalanced, amount: 1);
        state.Enemies.Add(enemy);
        return state;
    }

    [Fact]
    public void AHitYourBlockTakesWholeStunsIt()
    {
        Assert.True(SimEngine.Run(Fight(yourBlock: 10, hits: 6)).StunnedInAll(enemyId: 0));
        Assert.False(SimEngine.Run(Fight(yourBlock: 5, hits: 6)).StunnedInAll(enemyId: 0)); // HP got through
        Assert.False(SimEngine.Run(Fight(yourBlock: 0, hits: 6)).StunnedInAll(enemyId: 0));
    }

    [Fact]
    public void AnyHitOfItsAttackCounts()
    {
        // Its second hit is taken whole by block you had left after the first: the code asks of every hit it deals, not the first.
        var state = Fight(yourBlock: 10, hits: new[] { 4, 4 });
        Assert.True(SimEngine.Run(state).StunnedInAll(enemyId: 0));
    }

    [Fact]
    public void ATeammatesBlockTakingItsHitWholeStunsIt()
    {
        // You take its hit through no block; your teammate's block takes theirs whole: dealer == base.Owner for their result too.
        var state = Fight(yourBlock: 0, hits: 6);
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, Block = 10 });
        Assert.True(SimEngine.Run(state).StunnedInAll(enemyId: 0));
    }

    [Fact]
    public void OnBowlbugRockItsHeadbuttStunsItWhenOffBalance()
    {
        // Its monster's own state, set by the power, read by its move after the attack: the move's stun on IsOffBalance.
        var offBalance = new CountCondition(SimCounted.MonsterState, SimCompare.NotEqual, Than: 0) { Field = "Monster.BowlbugRock.IsOffBalance" };
        var state = Fight(yourBlock: 10, hits: 6);
        var bowlbug = state.Enemies[0];
        bowlbug.Monster = "BowlbugRock";
        bowlbug.Move = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1), new WhenAction(offBalance, new SimAction[] { new MoveStunAction() }) };
        bowlbug.Fields = bowlbug.Fields.With("Monster.BowlbugRock.IsOffBalance", 0);
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.StunnedInAll(enemyId: 0));
        Assert.All(outcome.Branches, branch => Assert.Equal(1, branch.Enemy(0)!.Fields["Monster.BowlbugRock.IsOffBalance"]));
        // Its hit through your block: not off balance, its move doesn't stun it.
        state.You.Block = 2;
        Assert.False(SimEngine.Run(state).StunnedInAll(enemyId: 0));
    }

    [Fact]
    public void OnBowlbugRockThePowerItselfDoesntStunIt()
    {
        // `base.Owner.Monster is BowlbugRock`: the power sets its state and leaves the stun to its move; a move not read stuns nothing.
        var state = Fight(yourBlock: 10, hits: 6);
        state.Enemies[0].Monster = "BowlbugRock";
        Assert.False(SimEngine.Run(state).StunnedInAll(enemyId: 0));
    }

    /// <summary>
    /// What the forecast audit holds to the game's hits: its code's test (any hit its owner deals that a block takes whole, every player's), as its
    /// Flash() in every outcome or in none, where the hand-written rule's check was your first hit alone.
    /// </summary>
    [Fact]
    public void ItsCodesTestIsWhatTheForecastAuditHoldsToTheGame()
    {
        var imbalanced = PowerId.Imbalanced.Name;
        // Your hit gets HP through, your teammate's block takes theirs whole: set off, though your first hit wasn't fully blocked.
        var teammate = Fight(yourBlock: 0, hits: 6);
        teammate.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, Block = 10 });
        Assert.True(SimEngine.Run(teammate).FlashedInAll(imbalanced, owner: 0));
        // Every hit gets HP through: set off in no outcome.
        Assert.False(SimEngine.Run(Fight(yourBlock: 5, hits: 6)).FlashedInAny(imbalanced, owner: 0));
        // On Bowlbug Rock too, where it sets its monster's state and stuns nothing itself.
        var bowlbug = Fight(yourBlock: 10, hits: 6);
        bowlbug.Enemies[0].Monster = "BowlbugRock";
        Assert.True(SimEngine.Run(bowlbug).FlashedInAll(imbalanced, owner: 0));
    }

    [Fact]
    public void ABlockPlayThatTakesItsHitWholeHasItsTabAfterThePlay()
    {
        var state = Fight(yourBlock: 0, hits: 6);
        var model = SimModels.Get(PowerId.Imbalanced.Name)!;
        var play = new SimPlay { Name = "Defend", BlockAmount = 8, BlockGains = 1,
            Card = new SimCardPlay(new SimCard("Defend", SimCardType.Skill) { Energy = 1, GainsBlock = true }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        var rule = model.OutcomeTab(state, SimEngine.Run(state), owner: 0);
        Assert.NotNull(rule);
        Assert.False(rule!.Harmful);
        Assert.False(rule.InPlay); // set off in its turn, after the play: the play check isn't held to it
        Assert.True(rule.SetOff(SimEngine.Run(state, play)));
    }
}
