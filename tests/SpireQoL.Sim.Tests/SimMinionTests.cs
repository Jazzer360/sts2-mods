using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Minions (MinionPower: OwnerIsSecondaryEnemy) die with the last main enemy: CreatureCmd.Kill kills a main enemy's teammates when every
/// one left is a secondary enemy (decompile v0.113.1). A dev review report: Rain Shot killed the Ovicopter, and its three Hatchlings,
/// predicted to live and attack, went with it, ending the fight.
/// </summary>
public class SimMinionTests
{
    private static SimState Board(int leaderHp)
    {
        var state = new SimState
        {
            Enemies =
            {
                new SimEnemy { Id = 0, Name = "Hatchling", Hp = 20, Hits = { 5 } },
                new SimEnemy { Id = 1, Name = "Hatchling", Hp = 20, Hits = { 5 } },
                new SimEnemy { Id = 2, Name = "Ovicopter", Hp = leaderHp, Hits = { 8 } },
            },
        };
        foreach (var minion in state.Enemies.Take(2)) minion.Powers = PowerSet.Empty.With(PowerId.Minion, amount: 1);
        state.You.Hp = 50;
        return state;
    }

    [Fact]
    public void MinionsDieWithTheLastMainEnemy()
    {
        var outcome = SimEngine.Run(Board(leaderHp: 12), Play(Card("RainShot"), target: 2, plays: 1, Attack(baseDamage: 12)));
        Assert.True(outcome.DeadInAll(enemyId: 0));
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax); // nobody is left to attack
    }

    [Fact]
    public void MinionsLiveWhileAMainEnemyDoes()
    {
        // The leader survives: its minions attack as usual. A minion's own death kills nobody.
        var outcome = SimEngine.Run(Board(leaderHp: 30), Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 20)));
        Assert.True(outcome.DeadInAll(enemyId: 0));
        Assert.False(outcome.DeadInAny(enemyId: 1));
        Assert.Equal(13, outcome.TakenMax);
    }
}
