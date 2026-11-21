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
        var state = new SimState();
        for (var i = 0; i < 2; i++) state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 100 });
        state.You.OrbCapacity = 2;
        state.You.Orbs = new() { Lightning(), Lightning() };
        var outcome = SimEngine.Run(state, Shatter());

        Assert.False(outcome.Truncated);
        var byDamage = outcome.Branches.GroupBy(branch => 100 - branch.Enemy(0)!.HpAtTurnEnd).ToDictionary(g => g.Key, g => g.Sum(branch => branch.Weight) / outcome.Branches.Sum(branch => branch.Weight));
        Assert.Equal(new[] { 0, 8, 16, 24, 32 }, byDamage.Keys.OrderBy(k => k).ToArray());
        Assert.Equal(6.0 / 16, byDamage[16], 6);
        Assert.Equal(1.0 / 16, byDamage[32], 6);
        Assert.All(outcome.Branches, branch => Assert.Equal(32, 200 - branch.Enemy(0)!.HpAtTurnEnd - branch.Enemy(1)!.HpAtTurnEnd));
        Assert.All(outcome.Branches, branch => Assert.Empty(branch.You.Orbs));
    }

    [Fact]
    public void ShatterEvokesFrostTwiceForBlock()
    {
        // Frost then Lightning, one enemy: 5 block twice, then 8 damage twice; no orbs left for passives.
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40, Hits = new() { 12 } } } };
        state.You.OrbCapacity = 2;
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 }, Lightning() };
        var outcome = SimEngine.Run(state, Shatter());
        Assert.Equal(10, outcome.Worst.BlockAtEnemyTurn);
        Assert.Equal(24, outcome.Worst.Enemy(0)!.HpAtTurnEnd);
        Assert.Equal(2, outcome.TakenMax);
    }

    [Fact]
    public void ShatterWithFullOrbsAndManyEnemiesStaysBounded()
    {
        // Was 4^20 copies in one step (ten Lightning orbs, four enemies): the game's frame never finished. Merged, it's one branch
        // per split of the 20 evokes among four enemies (1,771), and every outcome is found.
        var state = new SimState();
        for (var i = 0; i < 4; i++) state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 500, Hits = new() { 6 } });
        state.You.OrbCapacity = 10;
        state.You.Orbs = Enumerable.Range(0, 10).Select(_ => Lightning()).ToList();
        var outcome = SimEngine.Run(state, Shatter());
        Assert.False(outcome.Truncated);
        Assert.Equal(1771, outcome.Branches.Count);
        Assert.Equal(1.0, outcome.Branches.Sum(branch => branch.Weight), 9);
        Assert.True(outcome.Copies < 50_000, $"{outcome.Copies} copies");
    }

    [Fact]
    public void BeaconOfHopeWithJuggernautOnFourPlayersStopsAtTheWorkCap()
    {
        // Four players with Beacon of Hope and Juggernaut: each block gain shares block with everyone, and every share hits a random
        // enemy, a chain inside one step that runs on every branch. It holds little but runs on: the work cap stops it (a small one
        // here, to keep the test quick), and nothing is promised.
        var state = new SimState();
        for (var i = 0; i < 4; i++) state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 30, Hits = new() { 20 } });
        SimPlayer Setup(SimPlayer player)
        {
            player.BeaconOfHope = true;
            player.Juggernaut = 3;
            player.Plating = 4;
            return player;
        }
        Setup(state.You);
        for (var t = 0; t < 3; t++) state.Teammates.Add(Setup(new SimPlayer { Hp = 80, HandCount = -1 }));
        var outcome = SimEngine.Run(state, null, new SimWork { CopyCap = 200_000 });
        Assert.True(outcome.Truncated);
        Assert.Equal("the work cap", outcome.CapHit);
        Assert.Equal(1.0, outcome.Branches.Sum(branch => branch.Weight), 9);
        Assert.All(Enumerable.Range(0, 4), id => Assert.False(outcome.DeadInAll(id)));
        Assert.Equal(1, outcome.YouDieChance().High);
    }
}
