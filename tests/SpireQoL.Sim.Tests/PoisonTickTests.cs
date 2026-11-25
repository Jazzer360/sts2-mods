using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// An enemy's poison ticks from PoisonPower's own code, as the enemies' turn starts (decompile v0.113.1): AfterSideTurnStart, when its owner
/// takes part, runs its Trigger(): TriggerCount times (its stacks at most, 1 + every living opponent's Accelerant), CreatureCmd.Damage of its
/// amount to its owner (Unblockable | Unpowered, no dealer), then PowerCmd.Decrement while its owner lives. Each tick is a damage of its own.
/// </summary>
public class PoisonTickTests
{
    private static SimState Poisoned(int hp, int stacks)
    {
        var enemy = Enemy(id: 1, hp);
        enemy.Hits.Add(10);
        enemy.AddPoison(stacks);
        return new SimState { Enemies = { enemy } };
    }

    private static SimState EnemyTurnOver(SimState state) => Assert.Single(SimEngine.Run(state).Branches);

    [Fact]
    public void EachTickIsADamageOfItsOwn()
    {
        // Your Accelerant 1: two ticks, 5 then 4. Slippery lets 1 HP of each through (a charge each): 2 HP, where one hit of 9 took 1.
        var state = Poisoned(hp: 20, stacks: 5);
        state.You.Powers = state.You.Powers.With(PowerId.Accelerant, amount: 1);
        state.Enemies[0].Slippery = 2;
        var after = EnemyTurnOver(state);
        Assert.Equal(18, after.Enemy(id: 1)!.Hp);
        Assert.Equal(3, after.Enemy(id: 1)!.PoisonStacks); // a stack less each tick
    }

    [Fact]
    public void TicksAtMostItsStacksAndStopAtDeath()
    {
        // Accelerant 4 on you, 2 stacks: two ticks (2, 1); 2 HP dies on the second, which takes no stack.
        var state = Poisoned(hp: 50, stacks: 2);
        state.You.Powers = state.You.Powers.With(PowerId.Accelerant, amount: 4);
        Assert.Equal(47, EnemyTurnOver(state).Enemy(id: 1)!.Hp);
        var dying = Poisoned(hp: 3, stacks: 2);
        dying.You.Powers = dying.You.Powers.With(PowerId.Accelerant, amount: 4);
        var outcome = SimEngine.Run(dying);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax); // it died before it could act
    }

    [Fact]
    public void ADeadPlayersAccelerantAddsNoTick()
    {
        // TriggerCount sums the living opponents' Accelerant only.
        var state = Poisoned(hp: 50, stacks: 5);
        state.Teammates.Add(new SimPlayer { Hp = 0, Died = true, Powers = PowerSet.Empty.With(PowerId.Accelerant, amount: 3) });
        Assert.Equal(45, EnemyTurnOver(state).Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void HardenedShellResetsBeforeTheTick()
    {
        // HardenedShellPower.BeforeSideTurnStart resets what it took this turn, before any AfterSideTurnStart: the tick meets a whole shell.
        var state = Poisoned(hp: 30, stacks: 4);
        ShellTabParityTests.Shell(state.Enemies[0], amount: 3, taken: 3);
        Assert.Equal(27, EnemyTurnOver(state).Enemy(id: 1)!.Hp);
    }
}
