using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A play's test of a power's copy aimed at a player and its change of that copy alone (decision #322), as the cards' code does them: Frantic Escape
/// (if the first enemy with Sandpit has a copy aimed at you, PowerCmd.ModifyAmount of it by 1, applied by that enemy) and Imitation Learning (the
/// copy on you aimed at the play's target's player is raised by its value, else a new one is applied and aimed at that player). Each is asked of
/// the state as the step runs, so a replay's later play finds what its first play did.
/// </summary>
public class AimedCopyTests
{
    private static readonly AimedCopyCondition SandpitAimedAtYou = new(PowerId.Sandpit, AimHolder.FirstEnemyWithPower, AimedPlayer.PlayOwner) { HolderPower = PowerId.Sandpit };

    private static readonly ModifyAimedPowerAction RaiseSandpit = new(PowerId.Sandpit, AimHolder.FirstEnemyWithPower, AimedPlayer.PlayOwner, Amount: 1)
        { HolderPower = PowerId.Sandpit, AppliedByHolder = true };

    /// <summary>The Insatiable (enemy 1) with a Sandpit aimed at you, a bystander before it without one.</summary>
    private static SimState Sandpit(int amount)
    {
        var insatiable = Enemy(id: 1, hp: 300);
        insatiable.Powers = insatiable.Powers.With(PowerId.Sandpit, amount);
        insatiable.Fields = insatiable.Fields.With(SimKeys.Aimed(PowerId.Sandpit.Name, Who.You), amount);
        var state = new SimState { Enemies = { Bystander(id: 0), insatiable } };
        state.You.Hp = 50;
        return state;
    }

    private static SimPlay FranticEscape(int plays) => new()
    {
        Name = "FranticEscape", HandCard = 3, HandDelta = -1, Plays = plays,
        Card = new SimCardPlay(new SimCard("FranticEscape", SimCardType.Status), Who.You, Who.None, PlayIndex: 0, PlayCount: plays, AutoPlay: false),
        Body = { new WhenAction(SandpitAimedAtYou, new SimAction[] { RaiseSandpit }) },
    };

    [Fact]
    public void FranticEscapeRaisesTheSandpitAimedAtYou()
    {
        SimEngine.ApplyPlayTo(Sandpit(amount: 1), FranticEscape(plays: 1), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(2, SimEngine.AimedAmount(branch, holder: 1, PowerId.Sandpit, Who.You));
        Assert.Equal(2, branch.Enemy(id: 1)!.Powers[PowerId.Sandpit]);
        Assert.Null(branch.Unmodelled);
        // Its last count no longer runs out as the enemies' turn starts: no death.
        Assert.False(SimEngine.Run(Sandpit(amount: 1), FranticEscape(plays: 1)).YouDieChance().Possible);
        Assert.True(SimEngine.Run(Sandpit(amount: 1)).YouDieChance().Certain);
    }

    [Fact]
    public void AReplaysLaterPlayRaisesItAgain()
    {
        SimEngine.ApplyPlayTo(Sandpit(amount: 1), FranticEscape(plays: 2), out var after, out _);
        Assert.Equal(3, SimEngine.AimedAmount(Assert.Single(after), holder: 1, PowerId.Sandpit, Who.You));
    }

    [Fact]
    public void ACopyAimedAtATeammateIsLeftAlone()
    {
        // s.Target == base.Owner.Creature: a teammate's copy isn't yours, so there's nothing to raise.
        var state = Sandpit(amount: 1);
        state.Teammates.Add(new SimPlayer { Hp = 40 });
        var insatiable = state.Enemy(id: 1)!;
        insatiable.Fields = NamedCounts.Empty.With(SimKeys.Aimed(PowerId.Sandpit.Name, Who.Teammate(teammateIndex: 0)), 1);
        SimEngine.ApplyPlayTo(state, FranticEscape(plays: 1), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(1, SimEngine.AimedAmount(branch, holder: 1, PowerId.Sandpit, Who.Teammate(teammateIndex: 0)));
        Assert.Equal(1, branch.Enemy(id: 1)!.Powers[PowerId.Sandpit]);
    }

    private static readonly AimedCopyCondition ImitatingTheTarget = new(PowerId.ImitationLearning, AimHolder.PlayOwner, AimedPlayer.PlayTarget);

    private static SimPlay ImitationLearning(int plays) => new()
    {
        Name = "ImitationLearning", HandCard = 4, HandDelta = -1, Plays = plays, TargetId = Who.Teammate(teammateIndex: 0),
        Card = new SimCardPlay(new SimCard("ImitationLearning", SimCardType.Skill), Who.You, Who.Teammate(teammateIndex: 0), PlayIndex: 0, PlayCount: plays, AutoPlay: false),
        Body =
        {
            // if (copy aimed at the target's player) { ModifyAmount(copy, 2); return; } copy = Apply(2); copy.PlayerTarget = cardPlay.Target.Player;
            new WhenAction(ImitatingTheTarget, new SimAction[] { new ModifyAimedPowerAction(PowerId.ImitationLearning, AimHolder.PlayOwner, AimedPlayer.PlayTarget, Amount: 2) }),
            new WhenAction(new NotCondition(ImitatingTheTarget), new SimAction[] { new ApplyPowerAction(PowerId.ImitationLearning, Amount: 2) { To = ApplyPowerAction.Where.You, AimedAt = AimedPlayer.PlayTarget } }),
        },
    };

    private static SimState Team()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 40) } };
        state.Teammates.Add(new SimPlayer { Hp = 40 });
        return state;
    }

    [Fact]
    public void ImitationLearningAimsItsNewCopyAtItsTarget()
    {
        SimEngine.ApplyPlayTo(Team(), ImitationLearning(plays: 1), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(2, branch.You.Powers[PowerId.ImitationLearning]);
        Assert.Equal(2, SimEngine.AimedAmount(branch, Who.You, PowerId.ImitationLearning, Who.Teammate(teammateIndex: 0)));
        Assert.Null(branch.Unmodelled);
    }

    [Fact]
    public void ImitationLearningsLaterPlayRaisesTheCopyItsFirstPlayAimed()
    {
        // The second play finds the copy the first aimed at its target: PowerCmd.ModifyAmount of it, no second copy.
        SimEngine.ApplyPlayTo(Team(), ImitationLearning(plays: 2), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(4, branch.You.Powers[PowerId.ImitationLearning]);
        Assert.Equal(4, SimEngine.AimedAmount(branch, Who.You, PowerId.ImitationLearning, Who.Teammate(teammateIndex: 0)));
        Assert.Equal(new[] { Who.Teammate(teammateIndex: 0) }, SimEngine.AimedAt(branch, Who.You, PowerId.ImitationLearning));
    }

    [Fact]
    public void AModifiedCopyAtZeroGoesWithItsOwnRemoval()
    {
        // PowerCmd.ModifyAmount to 0 removes that instance alone (ShouldRemoveDueToAmount): the entry keeps the other copy's amount.
        var state = Sandpit(amount: 1);
        state.Teammates.Add(new SimPlayer { Hp = 40 });
        var insatiable = state.Enemy(id: 1)!;
        insatiable.Powers = insatiable.Powers.With(PowerId.Sandpit, 4);
        insatiable.Fields = insatiable.Fields.With(SimKeys.Aimed(PowerId.Sandpit.Name, Who.Teammate(teammateIndex: 0)), 3);
        var branch = Assert.Single(SimEngine.ModifyAimedCopy(state, holder: 1, PowerId.Sandpit, Who.You, offset: -1, applier: null));
        Assert.Equal(3, branch.Enemy(id: 1)!.Powers[PowerId.Sandpit]);
        Assert.Equal(new[] { Who.Teammate(teammateIndex: 0) }, SimEngine.AimedAt(branch, holder: 1, PowerId.Sandpit));
        Assert.True(branch.Died); // Sandpit's own AfterRemoved for your copy
        Assert.False(branch.Teammates[0].Died);
    }
}
