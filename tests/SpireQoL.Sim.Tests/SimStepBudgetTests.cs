using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Shatter (every orb evoked twice, front first) as one step per evoke, and the copy budget per step
/// (SimEngine.StepCopyBudget): past it the run is out of branches, so nothing is guaranteed and damage to
/// you comes from the safe run.
/// </summary>
public class SimStepBudgetTests
{
    private static SimOrb Lightning() => new() { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 };
    private static SimPlay Shatter() => new() { OrbOps = { new OrbOp { Op = OrbOp.Kind.EvokeAllTwice } } };

    [Fact]
    public void ShatterEvokesEveryLightningTwiceAtRandom()
    {
        // Two Lightning orbs, two enemies: four evokes of 8, each on either enemy. E0 takes 0, 8, 16, 24 or
        // 32, with the binomial odds 1, 4, 6, 4, 1 in 16 (merged branches keep their shares).
        var s = new SimState();
        for (var i = 0; i < 2; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 100 });
        s.P.OrbCapacity = 2;
        s.P.Orbs = new() { Lightning(), Lightning() };
        var o = SimEngine.Run(s, Shatter());

        Assert.False(o.Truncated);
        var byDamage = o.Branches.GroupBy(b => 100 - b.Enemy(0)!.HpAtTurnEnd).ToDictionary(g => g.Key, g => g.Sum(b => b.Weight) / o.Branches.Sum(b => b.Weight));
        Assert.Equal(new[] { 0, 8, 16, 24, 32 }, byDamage.Keys.OrderBy(k => k).ToArray());
        Assert.Equal(6.0 / 16, byDamage[16], 6);
        Assert.Equal(1.0 / 16, byDamage[32], 6);
        Assert.All(o.Branches, b => Assert.Equal(32, 200 - b.Enemy(0)!.HpAtTurnEnd - b.Enemy(1)!.HpAtTurnEnd));
        Assert.All(o.Branches, b => Assert.Empty(b.P.Orbs));
    }

    [Fact]
    public void ShatterEvokesFrostTwiceForBlock()
    {
        // Frost then Lightning, one enemy: 5 block twice, then 8 damage twice; no orbs left for passives.
        var s = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40, Hits = new() { 12 } } } };
        s.P.OrbCapacity = 2;
        s.P.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 }, Lightning() };
        var o = SimEngine.Run(s, Shatter());
        Assert.Equal(10, o.Worst.BlockAtEnemyTurn);
        Assert.Equal(24, o.Worst.Enemy(0)!.HpAtTurnEnd);
        Assert.Equal(2, o.TakenMax);
    }

    [Fact]
    public void ShatterWithFullOrbsAndManyEnemiesStaysBounded()
    {
        // Was 4^20 copies in one step (ten Lightning orbs, four enemies): the game's frame never finished.
        var s = new SimState();
        for (var i = 0; i < 4; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 500, Hits = new() { 6 } });
        s.P.OrbCapacity = 10;
        s.P.Orbs = Enumerable.Range(0, 10).Select(_ => Lightning()).ToList();
        var o = SimEngine.Run(s, Shatter());
        Assert.True(o.Copies < 4 * SimEngine.StepCopyBudget, $"{o.Copies} copies");
    }

    [Fact]
    public void PastTheBudgetNothingIsGuaranteedAndDamageComesFromTheSafeRun()
    {
        // Four players with Beacon of Hope and Juggernaut: each block gain shares block with everyone, and
        // every share hits a random enemy. One step makes more copies than the budget.
        var s = new SimState();
        for (var i = 0; i < 4; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 30, Hits = new() { 20 } });
        SimPlayer Setup(SimPlayer p)
        {
            p.BeaconOfHope = true;
            p.Juggernaut = 3;
            p.Plating = 4;
            return p;
        }
        Setup(s.P);
        for (var t = 0; t < 3; t++) s.Teammates.Add(Setup(new SimPlayer { Hp = 80, HandCount = -1 }));
        var o = SimEngine.Run(s);

        Assert.True(o.Truncated);
        Assert.NotNull(o.SafeWorst);
        Assert.True(o.MaxStepCopies >= SimEngine.StepCopyBudget);
        // Juggernaut can't be counted on to kill anyone, and you take every hit: the safe run's random hits
        // land on nobody, so all four enemies attack.
        Assert.All(Enumerable.Range(0, 4), id => Assert.False(o.DeadInAll(id)));
        Assert.Equal(o.SafeWorst!.Taken, o.TakenMax);
    }
}
