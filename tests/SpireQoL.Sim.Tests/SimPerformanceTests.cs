using System.Diagnostics;
using SpireQoL.Sim;
using Xunit;
using Xunit.Abstractions;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The simulator runs on the game's frame (every card's simulations in parallel, the frame waits for
/// them), so one simulation must stay cheap whatever the fight. Checked by counting its work, which is the
/// same on every machine (decision #31), not by timing it:
///  - the bound: a step makes at most about SimEngine.StepCopyBudget state copies and steps at most
///    MaxBranches branches, for any mix of enemies, players, orbs and effects (BoundsHoldInEveryFight);
///  - the scenarios: today's copies, steps and bytes allocated for the heavy fights, with headroom, so a
///    change that makes the simulator do more work fails on any machine. When a change is meant to cost
///    more, raise the limit in the same commit and say why.
/// One timing check is left, loose enough that only a disaster (something slow that neither copies nor
/// allocates) trips it on a slow machine.
/// </summary>
public class SimPerformanceTests
{
    private readonly ITestOutputHelper _out;

    public SimPerformanceTests(ITestOutputHelper output) => _out = output;

    private static SimOrb Lightning() => new() { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 };
    private static SimOrb Frost() => new() { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 };
    private static SimPlay Shatter() => new() { OrbOps = { new OrbOp { Op = OrbOp.Kind.EvokeAllTwice } } };

    /// <summary>The most work any one of these simulations did, and the bytes all of them allocated.</summary>
    private sealed record Cost(int Copies, int Stepped, int Steps, int MaxStepCopies, long Bytes, double Ms);

    private static Cost Measure(SimState s, IReadOnlyList<SimPlay?> plays)
    {
        // Warm up (JIT, first-call allocations) the way it would be after the first frame in game.
        foreach (var p in plays.Take(3)) SimEngine.Run(s, p);
        int copies = 0, stepped = 0, steps = 0, maxStep = 0;
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        foreach (var p in plays)
        {
            var o = SimEngine.Run(s, p);
            copies = Math.Max(copies, o.Copies);
            stepped = Math.Max(stepped, o.Stepped);
            steps = Math.Max(steps, o.Steps);
            maxStep = Math.Max(maxStep, o.MaxStepCopies);
        }
        sw.Stop();
        return new Cost(copies, stepped, steps, maxStep, GC.GetAllocatedBytesForCurrentThread() - bytes, sw.Elapsed.TotalMilliseconds / plays.Count);
    }

    // ------------------------------------------------------------------ the scenarios

    /// <summary>Three enemies, six Lightning orbs, Juggernaut: ten cards, once per target.</summary>
    private static (SimState, List<SimPlay?>) BranchHeavyHand()
    {
        var s = new SimState();
        for (var i = 0; i < 3; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 20 + i * 7, Block = 5, Hits = new() { 6, 6 } });
        s.P.Orbs = Enumerable.Range(0, 6).Select(_ => Lightning()).ToList();
        s.P.OrbCapacity = 6;
        s.P.Juggernaut = 4;
        s.P.Plating = 3;
        s.P.OrichalcumBlock = 6;
        var plays = new List<SimPlay?> { null };
        for (var c = 0; c < 10; c++)
            for (var t = 0; t < 3; t++)
                plays.Add(new SimPlay
                {
                    HitTarget = SimPlay.Target.Single, TargetId = t, Hits = 1, HitDamage = { [0] = 9, [1] = 9, [2] = 9 },
                    BlockAmount = 5, BlockGains = c % 2,
                    OrbOps = { new OrbOp { Op = OrbOp.Kind.Channel, Orb = Lightning() } },
                });
        return (s, plays);
    }

    /// <summary>You and three teammates with 10 Lightning orbs each against four enemies (4^40 roll sequences).</summary>
    private static (SimState, List<SimPlay?>) FourDefects()
    {
        var s = new SimState();
        for (var i = 0; i < 4; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 60 + i * 11, Block = 4, Hits = new() { 9, 9 } });
        s.P.OrbCapacity = 10;
        s.P.Orbs = Enumerable.Range(0, 10).Select(_ => Lightning()).ToList();
        for (var t = 0; t < 3; t++)
            s.Teammates.Add(new SimPlayer { OrbCapacity = 10, HandCount = -1, Orbs = Enumerable.Range(0, 10).Select(_ => Lightning()).ToList() });
        s.TeammatesBeforeMe = 1;
        var plays = new List<SimPlay?> { null };
        plays.AddRange(Enumerable.Range(0, 40).Select(t => new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = t % 4, Hits = 1, HitDamage = { [0] = 9, [1] = 9, [2] = 9, [3] = 9 } }));
        plays.Add(Shatter());
        return (s, plays);
    }

    /// <summary>Four players, each with Beacon of Hope, Juggernaut and Frost: every block gain shares block, each share hits a random enemy.</summary>
    private static (SimState, List<SimPlay?>) BeaconAndJuggernaut()
    {
        var s = new SimState();
        for (var i = 0; i < 4; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 300, Hits = new() { 6 } });
        SimPlayer Setup(SimPlayer p)
        {
            p.BeaconOfHope = true;
            p.Juggernaut = 3;
            p.Plating = 4;
            p.OrichalcumBlock = 6;
            p.OrbCapacity = 3;
            p.Orbs = Enumerable.Range(0, 3).Select(_ => Frost()).ToList();
            return p;
        }
        Setup(s.P);
        for (var t = 0; t < 3; t++) s.Teammates.Add(Setup(new SimPlayer { Hp = 80, HandCount = -1 }));
        var plays = new List<SimPlay?> { null, Shatter() };
        plays.AddRange(Enumerable.Range(0, 8).Select(_ => new SimPlay { BlockAmount = 5, BlockGains = 1 }));
        return (s, plays);
    }

    /// <summary>Two enemies, two Lightning orbs and a Frost orb: a few random targets.</summary>
    private static (SimState, List<SimPlay?>) TypicalFight()
    {
        var s = new SimState();
        for (var i = 0; i < 2; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 30, Hits = new() { 8 } });
        s.P.Orbs = new() { Lightning(), Lightning(), Frost() };
        s.P.OrbCapacity = 3;
        var plays = new List<SimPlay?> { null };
        plays.AddRange(Enumerable.Range(0, 16).Select(t => new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = t % 2, Hits = 1, HitDamage = { [0] = 6, [1] = 6 } }));
        return (s, plays);
    }

    /// <summary>
    /// Limits per scenario: the most state copies, branches stepped and steps of one simulation, and the
    /// bytes allocated for all of them. Measured on the current simulator, plus about 25% (bytes 50%: they
    /// move a little between .NET versions).
    /// </summary>
    public static IEnumerable<object[]> Scenarios() => new[]
    {
        // Measured (2026-11-12): copies, stepped, steps, bytes.
        new object[] { "branch-heavy hand", 1160, 830, 65, 32_000_000L },              // 926, 659, 52, 21.3 MB
        new object[] { "four Defects", 1490, 3100, 183, 141_000_000L },                // 1192, 2483, 146, 94.1 MB
        new object[] { "Beacon of Hope and Juggernaut", 2600, 95, 95, 70_000_000L },   // 2056 (the step budget), 76, 76, 46.9 MB
        new object[] { "typical fight", 10, 48, 27, 390_000L },                        // 6, 38, 21, 0.26 MB
    };

    private static (SimState, List<SimPlay?>) Build(string name) => name switch
    {
        "branch-heavy hand" => BranchHeavyHand(),
        "four Defects" => FourDefects(),
        "Beacon of Hope and Juggernaut" => BeaconAndJuggernaut(),
        "typical fight" => TypicalFight(),
        _ => throw new ArgumentException(name),
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void EachScenarioStaysInsideItsWork(string name, int maxCopies, int maxStepped, int maxSteps, long maxBytes)
    {
        var (s, plays) = Build(name);
        var c = Measure(s, plays);
        _out.WriteLine($"{name}: copies {c.Copies}, stepped {c.Stepped}, steps {c.Steps}, most copies in a step {c.MaxStepCopies}, {c.Bytes} bytes for {plays.Count} simulations, {c.Ms:0.00} ms each");
        Assert.True(c.Copies <= maxCopies, $"{name}: {c.Copies} state copies (limit {maxCopies})");
        Assert.True(c.Stepped <= maxStepped, $"{name}: {c.Stepped} branches stepped (limit {maxStepped})");
        Assert.True(c.Steps <= maxSteps, $"{name}: {c.Steps} steps (limit {maxSteps})");
        Assert.True(c.Bytes <= maxBytes, $"{name}: {c.Bytes} bytes allocated (limit {maxBytes})");
    }

    // ------------------------------------------------------------------ the bound

    /// <summary>
    /// The complexity bound, for any fight: no step makes more than its copy budget (plus the random
    /// choice in progress when it ran out: at most one per enemy at each level of a chain), and no step
    /// steps more than MaxBranches branches. Fights from one enemy to five, one player to four, no orbs to
    /// full Lightning, with Beacon of Hope and Juggernaut or without; random hits, Shatter, channelling.
    /// </summary>
    [Fact]
    public void BoundsHoldInEveryFight()
    {
        var worstStep = 0;
        var worstStepped = 0;
        var runs = 0;
        foreach (var enemies in new[] { 1, 2, 3, 5 })
        foreach (var players in new[] { 1, 2, 4 })
        foreach (var orbs in new[] { 0, 5, 10 })
        foreach (var beacon in new[] { false, true })
        {
            var s = new SimState();
            for (var i = 0; i < enemies; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 200, Hits = new() { 7, 7 } });
            SimPlayer Setup(SimPlayer p)
            {
                p.OrbCapacity = Math.Max(orbs, 1);
                p.Orbs = Enumerable.Range(0, orbs).Select(i => i % 3 == 2 ? Frost() : Lightning()).ToList();
                p.Plating = 4;
                if (beacon)
                {
                    p.BeaconOfHope = true;
                    p.Juggernaut = 3;
                }
                return p;
            }
            Setup(s.P);
            for (var t = 1; t < players; t++) s.Teammates.Add(Setup(new SimPlayer { Hp = 80, HandCount = -1 }));
            var plays = new SimPlay?[]
            {
                null,
                Shatter(),
                new() { HitTarget = SimPlay.Target.Random, Hits = 5, HitDamage = Enumerable.Range(0, enemies).ToDictionary(i => i, _ => 4) },
                new() { BlockAmount = 6, BlockGains = 2, OrbOps = { new OrbOp { Op = OrbOp.Kind.Channel, Orb = Lightning() } } },
            };
            foreach (var p in plays)
            {
                var o = SimEngine.Run(s, p);
                runs++;
                worstStep = Math.Max(worstStep, o.MaxStepCopies);
                worstStepped = Math.Max(worstStepped, o.Steps == 0 ? 0 : (int)Math.Ceiling((double)o.Stepped / o.Steps));
                var slack = 8 * enemies;
                Assert.True(o.MaxStepCopies <= SimEngine.StepCopyBudget + slack,
                    $"{enemies} enemies, {players} players, {orbs} orbs, beacon {beacon}: a step made {o.MaxStepCopies} copies (budget {SimEngine.StepCopyBudget} + {slack})");
                Assert.True(o.Stepped <= o.Steps * SimEngine.MaxBranches,
                    $"{enemies} enemies, {players} players, {orbs} orbs, beacon {beacon}: {o.Stepped} branches stepped in {o.Steps} steps");
            }
        }
        _out.WriteLine($"{runs} fights: most copies in one step {worstStep}, most branches per step on average {worstStepped}");
    }

    // ------------------------------------------------------------------ the one timing check

    /// <summary>
    /// Only a disaster: the heaviest scenarios at 50 ms per simulation (a dev machine does them in a few
    /// ms; GitHub's runners in about three times that). The counts above are what catch a slowdown.
    /// </summary>
    [Fact]
    public void NoSimulationTakesAnythingLikeAFrame()
    {
        foreach (var name in new[] { "branch-heavy hand", "four Defects", "Beacon of Hope and Juggernaut" })
        {
            var (s, plays) = Build(name);
            var c = Measure(s, plays);
            _out.WriteLine($"{name}: {c.Ms:0.00} ms per simulation");
            Assert.True(c.Ms < 50, $"{name}: {c.Ms:0.00} ms per simulation (limit 50 ms)");
        }
    }
}
