using System.Diagnostics;
using SpireQoL.Sim;
using Xunit;
using Xunit.Abstractions;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The simulator runs on the game's frame (synchronously, when the state changes): a full hand must
/// simulate well inside one frame even in branch-heavy fights.
/// </summary>
public class SimPerformanceTests
{
    private readonly ITestOutputHelper _out;

    public SimPerformanceTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void AFullHandInABranchHeavyFightSimulatesInsideAFrame()
    {
        // Three enemies, six Lightning orbs (random targets), Juggernaut on every block gain:
        // the worst realistic case for branching.
        var s = new SimState();
        for (var i = 0; i < 3; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 20 + i * 7, Block = 5, Hits = new() { 6, 6 } });
        s.P.Orbs = Enumerable.Range(0, 6).Select(_ => new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 }).ToList();
        s.P.OrbCapacity = 6;
        s.P.Juggernaut = 4;
        s.P.Plating = 3;
        s.P.OrichalcumBlock = 6;

        // Ten cards, each simulated once per target (single-target attacks) like Proc Indicators does.
        var plays = new List<SimPlay>();
        for (var c = 0; c < 10; c++)
        {
            for (var t = 0; t < 3; t++)
            {
                plays.Add(new SimPlay
                {
                    HitTarget = SimPlay.Target.Single, TargetId = t, Hits = 1, HitDamage = { [0] = 9, [1] = 9, [2] = 9 },
                    BlockAmount = 5, BlockGains = c % 2,
                    OrbOps = { new OrbOp { Op = OrbOp.Kind.Channel, Orb = new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } } },
                });
            }
        }

        // Warm up (JIT) the way it would be after the first frame in game.
        SimEngine.Run(s);
        foreach (var p in plays.Take(3)) SimEngine.Run(s, p);

        var sw = Stopwatch.StartNew();
        var baseline = SimEngine.Run(s);
        var maxBranches = baseline.Branches.Count;
        foreach (var p in plays)
        {
            var o = SimEngine.Run(s, p);
            maxBranches = Math.Max(maxBranches, o.Branches.Count);
        }
        sw.Stop();
        _out.WriteLine($"{plays.Count + 1} simulations in {sw.Elapsed.TotalMilliseconds:0.0} ms, most branches {maxBranches}");

        // In game, card simulations get 4 ms per frame (the rest continue next frame), so what matters
        // is the cost of one simulation: it must fit the budget with room to spare. Generous for slow CI machines.
        var perSim = sw.Elapsed.TotalMilliseconds / (plays.Count + 1);
        Assert.True(perSim < 4, $"{perSim:0.00} ms per simulation");
    }

    [Fact]
    public void ATypicalFightSimulatesAFullHandInWellUnderAFrame()
    {
        // Two enemies, two Lightning orbs, a Frost orb: a few random targets.
        var s = new SimState();
        for (var i = 0; i < 2; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 30, Hits = new() { 8 } });
        s.P.Orbs = new() { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 }, new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 }, new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } };
        s.P.OrbCapacity = 3;
        var plays = Enumerable.Range(0, 16).Select(t => new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = t % 2, Hits = 1, HitDamage = { [0] = 6, [1] = 6 } }).ToList();
        SimEngine.Run(s);
        var sw = Stopwatch.StartNew();
        SimEngine.Run(s);
        foreach (var p in plays) SimEngine.Run(s, p);
        sw.Stop();
        _out.WriteLine($"typical: {plays.Count + 1} simulations in {sw.Elapsed.TotalMilliseconds:0.00} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 16, $"took {sw.Elapsed.TotalMilliseconds:0.0} ms");
    }
}
