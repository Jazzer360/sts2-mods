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

        // In game, every card's simulations run in parallel and the frame waits for them, so what matters
        // is the cost of one simulation. Generous for slow CI machines.
        var perSim = sw.Elapsed.TotalMilliseconds / (plays.Count + 1);
        _out.WriteLine($"{perSim:0.00} ms per simulation");
        // On a dev machine: 4 ms per simulation, so a hand of them stays well inside a frame; one
        // simulation never overruns a frame's budget. Shared CI runners are budget CPUs and noisy (about
        // 10 ms per simulation measured there): 30 ms (about one frame at 30 fps) still catches a real
        // slowdown with plenty of headroom.
        var limit = Environment.GetEnvironmentVariable("CI") == "true" ? 30.0 : 4.0;
        Assert.True(perSim < limit, $"{perSim:0.00} ms per simulation (limit {limit} ms)");
    }

    [Fact]
    public void FourDefectsWithFortyLightningOrbsStayInsideTheLimit()
    {
        // Multiplayer worst case: you and three teammates with 10 Lightning orbs each against four
        // enemies (4^40 roll sequences). Merging identical branches and the 128-branch cap (then the
        // safe fallback: random hits land on nobody) keep one simulation inside the same limit.
        var s = new SimState();
        for (var i = 0; i < 4; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 60 + i * 11, Block = 4, Hits = new() { 9, 9 } });
        SimOrb L() => new() { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 };
        s.P.OrbCapacity = 10;
        s.P.Orbs = Enumerable.Range(0, 10).Select(_ => L()).ToList();
        for (var t = 0; t < 3; t++)
            s.Teammates.Add(new SimPlayer { OrbCapacity = 10, HandCount = -1, Orbs = Enumerable.Range(0, 10).Select(_ => L()).ToList() });
        s.TeammatesBeforeMe = 1;
        var plays = Enumerable.Range(0, 40).Select(t => new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = t % 4, Hits = 1, HitDamage = { [0] = 9, [1] = 9, [2] = 9, [3] = 9 } }).ToList();

        SimEngine.Run(s);
        foreach (var p in plays.Take(3)) SimEngine.Run(s, p);
        var sw = Stopwatch.StartNew();
        var truncated = SimEngine.Run(s).Truncated;
        foreach (var p in plays) SimEngine.Run(s, p);
        sw.Stop();
        var perSim = sw.Elapsed.TotalMilliseconds / (plays.Count + 1);
        _out.WriteLine($"40 Lightning orbs: {perSim:0.00} ms per simulation, truncated {truncated}");
        var limit = Environment.GetEnvironmentVariable("CI") == "true" ? 30.0 : 4.0;
        Assert.True(perSim < limit, $"{perSim:0.00} ms per simulation (limit {limit} ms)");
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
