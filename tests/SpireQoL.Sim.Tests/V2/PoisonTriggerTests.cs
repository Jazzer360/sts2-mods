using Xunit;
using static SpireQoL.Sim.Tests.V2.V2Script;

namespace SpireQoL.Sim.Tests.V2;

/// <summary>
/// Outbreak setting off every enemy's poison during the play (Outbreak.OnPlay: PoisonPower.Trigger on each), and Accelerant's
/// extra ticks: PoisonPower.TriggerCount is 1 + every living player's Accelerant, at most the stacks (decompile v0.113.1).
/// </summary>
public class PoisonTriggerTests
{
    private static SimEnemy Poisoned(int id, int hp, int stacks, int triggers = 1)
    {
        var e = Enemy(id, hp);
        e.PoisonTriggers = triggers;
        e.AddPoison(stacks);
        return e;
    }

    private static SimScript Outbreak(int poison) => Play(Card("Outbreak", SimCardType.Skill), -1, 1,
        new ApplyPowerAction("PoisonPower", poison) { Debuff = true, To = ApplyPowerAction.Where.AllEnemies }, new TriggerPoisonAction());

    [Fact]
    public void OutbreakTicksEveryEnemysPoisonOnce()
    {
        // The report's Decimillipede: poison 3, 7 and 3, then Outbreak's 9 on each: hits of 12, 16 and 12, a stack less each.
        var s = new SimState { Enemies = { Poisoned(0, 33, 3), Poisoned(1, 44, 7), Poisoned(2, 47, 3) } };
        var after = After(s, Outbreak(9)).Single();
        Assert.Equal(new[] { 21, 28, 35 }, after.Enemies.Select(e => e.Hp));
        Assert.Equal(new[] { 11, 15, 11 }, after.Enemies.Select(e => e.PoisonStacks));
        Assert.Equal(new[] { 11, 15, 11 }, after.Enemies.Select(e => e.Poison)); // the next tick
    }

    [Fact]
    public void AccelerantTicksItMoreTimesEvenOnAnUnpoisonedEnemy()
    {
        // Accelerant 2 (from any players): 3 ticks per trigger. A fresh 9 poison hits 9, 8, 7 and leaves 6.
        var s = new SimState { Enemies = { Poisoned(0, 50, 0, triggers: 3) } };
        var after = After(s, Outbreak(9)).Single();
        Assert.Equal(26, after.Enemy(0)!.Hp);
        Assert.Equal(6, after.Enemy(0)!.PoisonStacks);
        Assert.Equal(6 + 5 + 4, after.Enemy(0)!.Poison);
    }

    [Fact]
    public void TicksStopAtTheStacksAndAtDeath()
    {
        var s = new SimState { Enemies = { Poisoned(0, 50, 0, triggers: 5), Poisoned(1, 3, 0, triggers: 5) } };
        var after = After(s, Outbreak(2)).Single();
        Assert.Equal(47, after.Enemy(0)!.Hp); // 2 + 1: two stacks, two ticks
        Assert.Equal(0, after.Enemy(0)!.PoisonStacks);
        Assert.False(after.Enemy(1)!.Alive); // 3 HP: 2, then 1
    }

    [Fact]
    public void GainingAccelerantAddsTicksForEveryEnemy()
    {
        // Your Accelerant (or a teammate's) raises every enemy's trigger count: poison 5 ticks 5 + 4 with 1 Accelerant.
        var s = new SimState { Enemies = { Poisoned(0, 50, 5), Poisoned(1, 50, 0) } };
        var after = After(s, Play(Card("Accelerant", SimCardType.Power), -1, 1, new ApplyPowerAction("AccelerantPower", 1) { To = ApplyPowerAction.Where.You })).Single();
        Assert.Equal(9, after.Enemy(0)!.Poison);
        Assert.Equal(2, after.Enemy(1)!.PoisonTriggers);
        Assert.Equal(1, after.P.Powers["AccelerantPower"]);
    }
}
