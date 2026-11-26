using SpireQoL.Sim.Models;
using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// PossessStrengthPower and PossessSpeedPower (The Lost's, decision #326): AfterPowerAmountChanged counts what its owner took from each player (an
/// application by its owner of less than 0 on a player), and AfterDeath gives it back as its owner dies (PowerCmd.Apply of -stolen, no applier):
/// a kill during your play gives you your Strength back for the play's later hits.
/// </summary>
public class PossessTests
{
    private static SimState Fight(int stolenStrength, int stolenDexterity = 0)
    {
        var lost = Enemy(id: 0, hp: 4);
        lost.Name = "TheLost";
        lost.Powers = PowerSet.Empty.With(PowerId.PossessStrength, amount: 1).With(PowerId.PossessSpeed, amount: 1);
        lost.Fields = lost.Fields.With(SimKeys.PerCreature(PowerId.PossessStrength.Name, "stolenStrength", Who.You), stolenStrength)
            .With(SimKeys.PerCreature(PowerId.PossessSpeed.Name, "stolenDexterity", Who.You), stolenDexterity);
        var state = new SimState { Enemies = { lost, Enemy(id: 1, hp: 40) } };
        state.You.Powers = PowerSet.Empty.With(PowerId.Strength, amount: stolenStrength).With(PowerId.Dexterity, amount: stolenDexterity);
        return state;
    }

    private static SimAction Hit(int target) =>
        new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 6, SimProps.Move)) { TargetId = target };

    [Fact]
    public void KillingItGivesTheStolenStrengthBackForTheHitsAfter()
    {
        Assert.IsType<PossessStrengthPower>(SimModels.Get(PowerId.PossessStrength.Name));
        // 6 with -2 Strength kills The Lost (4 HP); its death gives the 2 back, so the hit on the other enemy deals 6.
        var after = Assert.Single(After(Fight(stolenStrength: -2), Play(Card("Two hits"), target: 0, plays: 1, Hit(target: 0), Hit(target: 1))));
        Assert.False(after.Enemy(id: 0)!.Alive);
        Assert.Equal(0, after.You.Powers[PowerId.Strength]);
        Assert.Equal(40 - 6, after.Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void AnAttackOnEveryEnemyHitsTheOthersBeforeItsDeathGivesTheStrengthBack()
    {
        // Decision #402 (a dev review: Hyperbeam killed The Lost and hit The Forgotten for 33, the simulator said 35): one CreatureCmd.Damage with every
        // target deals each its damage, then runs the results' hooks, then kills the killed (CreatureCmd.Kill after the results loop). The hit on the
        // other enemy is worked out with the Strength still taken (6 - 2 = 4), and the 2 come back after.
        var everyone = new AttackAction(Hits: 1, SimPlay.Target.All, new DamageSpec(Base: 6, SimProps.Move));
        var after = Assert.Single(After(Fight(stolenStrength: -2), Play(Card("Hyperbeam"), target: 0, plays: 1, everyone)));
        Assert.False(after.Enemy(id: 0)!.Alive);
        Assert.Equal(40 - 4, after.Enemy(id: 1)!.Hp);
        Assert.Equal(0, after.You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void KillingItGivesTheStolenDexterityBack()
    {
        var after = Assert.Single(After(Fight(stolenStrength: 0, stolenDexterity: -1), Play(Card("Hit"), target: 0, plays: 1, Hit(target: 0))));
        Assert.Equal(0, after.You.Powers[PowerId.Dexterity]);
    }

    [Fact]
    public void ItCountsWhatItsOwnerTakesFromAPlayer()
    {
        // applier == base.Owner, power.Owner.IsPlayer, amount < 0: added to what it took; Strength it gives itself, or another's, isn't counted.
        var state = Fight(stolenStrength: -2);
        var possess = SimModels.Get(PowerId.PossessStrength.Name)!;
        var key = SimKeys.PerCreature(PowerId.PossessStrength.Name, "stolenStrength", Who.You);
        Assert.Single(possess.AfterPowerApplied(state, owner: 0, PowerId.Strength, amount: -2, applier: 0, target: Who.You, debuff: true));
        Assert.Equal(-4, state.Enemy(id: 0)!.Fields[key]);
        Assert.Single(possess.AfterPowerApplied(state, owner: 0, PowerId.Strength, amount: 2, applier: 0, target: 0, debuff: false));
        Assert.Single(possess.AfterPowerApplied(state, owner: 0, PowerId.Strength, amount: -1, applier: 1, target: Who.You, debuff: true));
        Assert.Equal(-4, state.Enemy(id: 0)!.Fields[key]);
    }

    [Fact]
    public void AnotherEnemysDeathGivesNothingBack()
    {
        var after = Assert.Single(After(Fight(stolenStrength: -2), Play(Card("Hit"), target: 1, plays: 1, Hit(target: 1))));
        Assert.Equal(-2, after.You.Powers[PowerId.Strength]);
    }
}
