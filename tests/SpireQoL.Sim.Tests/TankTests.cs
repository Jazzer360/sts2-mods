using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// TankPower, read from its code (decision #329): its AfterApplied gives every other living player of its owner's side Guarded (foreach over
/// CombatState.GetTeammatesOf(base.Owner): IsAlive, IsPlayer, not its owner; PowerCmd.Apply of its amount, its owner the applier), and its
/// ModifyDamageMultiplicative makes powered attacks on its owner deal its DamageIncrease (1.5).
/// </summary>
public class TankTests
{
    private static SimState TwoPlayers()
    {
        var enemy = Enemy(id: 0, hp: 40);
        enemy.Hits = new List<int> { 10 };
        enemy.Intent = new List<IntentHit> { new(Base: 10) };
        enemy.IntendsToAttack = true;
        var state = new SimState { Enemies = { enemy } };
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60 });
        return state;
    }

    private static SimScript PlayTank() =>
        Play(Card("Tank", SimCardType.Power), target: Who.None, plays: 1, new ApplyPowerAction(PowerId.Tank, Amount: 1) { To = ApplyPowerAction.Where.You });

    [Fact]
    public void PlayingItGivesYourTeammatesGuarded()
    {
        Assert.IsType<ReadModel>(SimModels.Get(PowerId.Tank.Name));
        var after = Assert.Single(After(TwoPlayers(), PlayTank()));
        Assert.True(after.You.Powers.Has(PowerId.Tank));
        Assert.Equal(1, after.Teammates[0].Powers[PowerId.Guarded]);
        Assert.False(after.You.Powers.Has(PowerId.Guarded));
    }

    [Fact]
    public void PoweredAttacksOnYouDealHalfAgainAfterIt()
    {
        // The enemy's 10 on you: 15 with Tank.
        var state = TwoPlayers();
        state.You.Powers = PowerSet.Empty.With(PowerId.Tank, amount: 1);
        var you = Assert.Single(SimEngine.Run(state).Branches).You;
        Assert.Equal(50 - 15, you.Hp);
    }

    [Fact]
    public void TheHitsAfterPlayingItAreTheTanks()
    {
        // Played this turn, kept on you (a read model): the enemy's 10 lands as 15, where leaving it out gave 10.
        var you = Assert.Single(SimEngine.Run(TwoPlayers(), PlayTank()).Branches).You;
        Assert.Equal(50 - 15, you.Hp);
    }

    [Fact]
    public void ADeadTeammateGetsNoGuarded()
    {
        var state = TwoPlayers();
        state.Teammates[0].Hp = 0;
        state.Teammates[0].Died = true;
        var after = Assert.Single(After(state, PlayTank()));
        Assert.False(after.Teammates[0].Powers.Has(PowerId.Guarded));
    }
}
