using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Shatter (every orb evoked twice, front first) as one step per evoke, and the heavy chains that once needed a copy budget per
/// step: merging every step keeps them to their distinct states (decision #107).
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
        s.You.OrbCapacity = 2;
        s.You.Orbs = new() { Lightning(), Lightning() };
        var o = SimEngine.Run(s, Shatter());

        Assert.False(o.Truncated);
        var byDamage = o.Branches.GroupBy(b => 100 - b.Enemy(0)!.HpAtTurnEnd).ToDictionary(g => g.Key, g => g.Sum(b => b.Weight) / o.Branches.Sum(b => b.Weight));
        Assert.Equal(new[] { 0, 8, 16, 24, 32 }, byDamage.Keys.OrderBy(k => k).ToArray());
        Assert.Equal(6.0 / 16, byDamage[16], 6);
        Assert.Equal(1.0 / 16, byDamage[32], 6);
        Assert.All(o.Branches, b => Assert.Equal(32, 200 - b.Enemy(0)!.HpAtTurnEnd - b.Enemy(1)!.HpAtTurnEnd));
        Assert.All(o.Branches, b => Assert.Empty(b.You.Orbs));
    }

    [Fact]
    public void ShatterEvokesFrostTwiceForBlock()
    {
        // Frost then Lightning, one enemy: 5 block twice, then 8 damage twice; no orbs left for passives.
        var s = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40, Hits = new() { 12 } } } };
        s.You.OrbCapacity = 2;
        s.You.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 }, Lightning() };
        var o = SimEngine.Run(s, Shatter());
        Assert.Equal(10, o.Worst.BlockAtEnemyTurn);
        Assert.Equal(24, o.Worst.Enemy(0)!.HpAtTurnEnd);
        Assert.Equal(2, o.TakenMax);
    }

    [Fact]
    public void ShatterWithFullOrbsAndManyEnemiesStaysBounded()
    {
        // Was 4^20 copies in one step (ten Lightning orbs, four enemies): the game's frame never finished. Merged, it's one branch
        // per split of the 20 evokes among four enemies (1,771), and every outcome is found.
        var s = new SimState();
        for (var i = 0; i < 4; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 500, Hits = new() { 6 } });
        s.You.OrbCapacity = 10;
        s.You.Orbs = Enumerable.Range(0, 10).Select(_ => Lightning()).ToList();
        var o = SimEngine.Run(s, Shatter());
        Assert.False(o.Truncated);
        Assert.Equal(1771, o.Branches.Count);
        Assert.Equal(1.0, o.Branches.Sum(b => b.Weight), 9);
        Assert.True(o.Copies < 50_000, $"{o.Copies} copies");
    }

    [Fact]
    public void BeaconOfHopeWithJuggernautOnFourPlayersStopsAtTheWorkCap()
    {
        // Four players with Beacon of Hope and Juggernaut: each block gain shares block with everyone, and every share hits a random
        // enemy, a chain inside one step that runs on every branch. It holds little but runs on: the work cap stops it (a small one
        // here, to keep the test quick), and nothing is promised.
        var s = new SimState();
        for (var i = 0; i < 4; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 30, Hits = new() { 20 } });
        SimPlayer Setup(SimPlayer p)
        {
            p.BeaconOfHope = true;
            p.Juggernaut = 3;
            p.Plating = 4;
            return p;
        }
        Setup(s.You);
        for (var t = 0; t < 3; t++) s.Teammates.Add(Setup(new SimPlayer { Hp = 80, HandCount = -1 }));
        var o = SimEngine.Run(s, null, new SimWork { CopyCap = 200_000 });
        Assert.True(o.Truncated);
        Assert.Equal("the work cap", o.CapHit);
        Assert.Equal(1.0, o.Branches.Sum(b => b.Weight), 9);
        Assert.All(Enumerable.Range(0, 4), id => Assert.False(o.DeadInAll(id)));
        Assert.Equal(1, o.YouDieChance().High);
    }
}
