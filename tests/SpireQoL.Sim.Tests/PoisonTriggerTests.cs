using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Outbreak setting off every enemy's poison during the play (Outbreak.OnPlay: PoisonPower.Trigger on each), and Accelerant's
/// extra ticks: PoisonPower.TriggerCount is 1 + every living player's Accelerant, at most the stacks (decompile v0.113.1).
/// </summary>
public class PoisonTriggerTests
{
    private static SimEnemy Poisoned(int id, int hp, int stacks, int triggers = 1)
    {
        var enemy = Enemy(id, hp);
        enemy.PoisonTriggers = triggers;
        enemy.AddPoison(stacks);
        return enemy;
    }

    private static SimScript Outbreak(int poison) => Play(Card("Outbreak", SimCardType.Skill), target: -1, plays: 1,
        new ApplyPowerAction(PowerId.Poison, poison) { Debuff = true, To = ApplyPowerAction.Where.AllEnemies }, new TriggerPoisonAction());

    [Fact]
    public void OutbreakTicksEveryEnemysPoisonOnce()
    {
        // The report's Decimillipede: poison 3, 7 and 3, then Outbreak's 9 on each: hits of 12, 16 and 12, a stack less each.
        var state = new SimState { Enemies = { Poisoned(id: 0, hp: 33, stacks: 3), Poisoned(id: 1, hp: 44, stacks: 7), Poisoned(id: 2, hp: 47, stacks: 3) } };
        var after = After(state, Outbreak(poison: 9)).Single();
        Assert.Equal(new[] { 21, 28, 35 }, after.Enemies.Select(enemy => enemy.Hp));
        Assert.Equal(new[] { 11, 15, 11 }, after.Enemies.Select(enemy => enemy.PoisonStacks));
        Assert.Equal(new[] { 11, 15, 11 }, after.Enemies.Select(enemy => enemy.Poison)); // the next tick
    }

    [Fact]
    public void AccelerantTicksItMoreTimesEvenOnAnUnpoisonedEnemy()
    {
        // Accelerant 2 (from any players): 3 ticks per trigger. A fresh 9 poison hits 9, 8, 7 and leaves 6.
        var state = new SimState { Enemies = { Poisoned(id: 0, hp: 50, stacks: 0, triggers: 3) } };
        var after = After(state, Outbreak(poison: 9)).Single();
        Assert.Equal(26, after.Enemy(id: 0)!.Hp);
        Assert.Equal(6, after.Enemy(id: 0)!.PoisonStacks);
        Assert.Equal(6 + 5 + 4, after.Enemy(id: 0)!.Poison);
    }

    [Fact]
    public void TicksStopAtTheStacksAndAtDeath()
    {
        var state = new SimState { Enemies = { Poisoned(id: 0, hp: 50, stacks: 0, triggers: 5), Poisoned(id: 1, hp: 3, stacks: 0, triggers: 5) } };
        var after = After(state, Outbreak(poison: 2)).Single();
        Assert.Equal(47, after.Enemy(id: 0)!.Hp); // 2 + 1: two stacks, two ticks
        Assert.Equal(0, after.Enemy(id: 0)!.PoisonStacks);
        Assert.False(after.Enemy(id: 1)!.Alive); // 3 HP: 2, then 1
    }

    [Fact]
    public void GainingAccelerantAddsTicksForEveryEnemy()
    {
        // Your Accelerant (or a teammate's) raises every enemy's trigger count: poison 5 ticks 5 + 4 with 1 Accelerant.
        var state = new SimState { Enemies = { Poisoned(id: 0, hp: 50, stacks: 5), Poisoned(id: 1, hp: 50, stacks: 0) } };
        var after = After(state, Play(Card("Accelerant", SimCardType.Power), target: -1, plays: 1, new ApplyPowerAction(PowerId.Accelerant, Amount: 1) { To = ApplyPowerAction.Where.You })).Single();
        Assert.Equal(9, after.Enemy(id: 0)!.Poison);
        Assert.Equal(2, after.Enemy(id: 1)!.PoisonTriggers);
        Assert.Equal(1, after.You.Powers[PowerId.Accelerant]);
    }
}
