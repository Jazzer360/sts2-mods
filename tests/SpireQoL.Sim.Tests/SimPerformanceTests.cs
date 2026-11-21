using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The simulator runs on the game's frame (every card's simulations in parallel, the frame waits for
/// them), so one simulation must stay cheap whatever the fight. Checked by counting its work, which is the
/// same on every machine (decision #31), not by timing it:
///  - the bound: every fight, for any mix of enemies, players, orbs and effects, finishes well inside the memory cap (decision
///    #107: SimWork.MaxHeldStates), every step merging identical branches (EveryFightFinishesInsideTheCap);
///  - the scenarios: today's copies, steps and bytes allocated for the heavy fights, with headroom, so a
///    change that makes the simulator do more work fails on any machine. When a change is meant to cost
///    more, raise the limit in the same commit and say why.
/// One timing limit is left, loose enough that only a disaster (something slow that neither copies nor
/// allocates) trips it on a slow machine. The scenarios are kept small enough that the whole class runs in a
/// few seconds: the busiest boards (four Defects with ten orbs each) are measured by hand, not here.
/// </summary>
public class SimPerformanceTests
{
    private readonly ITestOutputHelper _out;

    public SimPerformanceTests(ITestOutputHelper output) => _out = output;

    private static SimOrb Lightning() => new() { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 };
    private static SimOrb Frost() => new() { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 };
    private static SimPlay Shatter() => new() { OrbOps = { new OrbOp { Op = OrbOp.Kind.EvokeAllTwice } } };

    /// <summary>The most work any one of these simulations did, and the bytes all of them allocated.</summary>
    private sealed record Cost(int Copies, int Stepped, int Steps, int MaxStepCopies, int PeakHeld, long Bytes, double Ms);

    private static Cost Measure(SimState s, IReadOnlyList<SimPlay?> plays)
    {
        // Warm up (JIT, first-call allocations) the way it would be after the first frame in game.
        SimEngine.Run(s, plays[0]);
        int copies = 0, stepped = 0, steps = 0, maxStep = 0, held = 0;
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        foreach (var p in plays)
        {
            var o = SimEngine.Run(s, p);
            copies = Math.Max(copies, o.Copies);
            stepped = Math.Max(stepped, o.Stepped);
            steps = Math.Max(steps, o.Steps);
            maxStep = Math.Max(maxStep, o.MaxStepCopies);
            held = Math.Max(held, o.PeakHeld);
            Assert.False(o.Truncated);
        }
        sw.Stop();
        return new Cost(copies, stepped, steps, maxStep, held, GC.GetAllocatedBytesForCurrentThread() - bytes, sw.Elapsed.TotalMilliseconds / plays.Count);
    }

    // ------------------------------------------------------------------ the scenarios

    /// <summary>Three enemies, six Lightning orbs, Juggernaut: ten cards, once per target.</summary>
    private static (SimState, List<SimPlay?>) BranchHeavyHand()
    {
        var s = new SimState();
        for (var i = 0; i < 3; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 20 + i * 7, Block = 5, Hits = new() { 6, 6 } });
        s.You.Orbs = Enumerable.Range(0, 6).Select(_ => Lightning()).ToList();
        s.You.OrbCapacity = 6;
        s.You.Juggernaut = 4;
        s.You.Plating = 3;
        s.You.OrichalcumBlock = 6;
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

    /// <summary>
    /// You and three teammates with 6 Lightning orbs each against four enemies (4^24 roll sequences; merged, a few thousand
    /// branches). With 10 orbs each it's about 460,000 copies and 2 s a simulation (decision #107), too slow to run on every push.
    /// </summary>
    private static (SimState, List<SimPlay?>) FourDefects()
    {
        var s = new SimState();
        for (var i = 0; i < 4; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 60 + i * 11, Block = 4, Hits = new() { 9, 9 } });
        s.You.OrbCapacity = 6;
        s.You.Orbs = Enumerable.Range(0, 6).Select(_ => Lightning()).ToList();
        for (var t = 0; t < 3; t++)
            s.Teammates.Add(new SimPlayer { OrbCapacity = 6, HandCount = -1, Orbs = Enumerable.Range(0, 6).Select(_ => Lightning()).ToList() });
        s.TeammatesBeforeMe = 1;
        var plays = new List<SimPlay?> { null };
        plays.AddRange(Enumerable.Range(0, 4).Select(t => new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = t, Hits = 1, HitDamage = { [0] = 9, [1] = 9, [2] = 9, [3] = 9 } }));
        return (s, plays);
    }

    /// <summary>
    /// Two players, each with Beacon of Hope, Juggernaut and Frost, against two enemies (four: about 1 s a simulation): every block gain shares block, each share hits a random enemy.
    /// With three or four such players the chain inside each step runs on every branch and meets the work cap (decision #107's
    /// follow-up; SimStepBudgetTests checks the cap stops it).
    /// </summary>
    private static (SimState, List<SimPlay?>) BeaconAndJuggernaut()
    {
        var s = new SimState();
        for (var i = 0; i < 2; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 300, Hits = new() { 6 } });
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
        Setup(s.You);
        s.Teammates.Add(Setup(new SimPlayer { Hp = 80, HandCount = -1 }));
        var plays = new List<SimPlay?> { null };
        plays.AddRange(Enumerable.Range(0, 4).Select(_ => new SimPlay { BlockAmount = 5, BlockGains = 1 }));
        return (s, plays);
    }

    /// <summary>Two enemies, two Lightning orbs and a Frost orb: a few random targets.</summary>
    private static (SimState, List<SimPlay?>) TypicalFight()
    {
        var s = new SimState();
        for (var i = 0; i < 2; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = 30, Hits = new() { 8 } });
        s.You.Orbs = new() { Lightning(), Lightning(), Frost() };
        s.You.OrbCapacity = 3;
        var plays = new List<SimPlay?> { null };
        plays.AddRange(Enumerable.Range(0, 16).Select(t => new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = t % 2, Hits = 1, HitDamage = { [0] = 6, [1] = 6 } }));
        return (s, plays);
    }

    /// <summary>
    /// Limits per scenario: the most state copies, branches stepped and steps of one simulation, and the
    /// bytes allocated for all of them, and the time per simulation. Measured on the current simulator, plus about 25% (bytes
    /// 50%: they move a little between .NET versions; time about five times: only a disaster).
    /// </summary>
    public static IEnumerable<object[]> Scenarios() => new[]
    {
        // Measured (2026-11-19, decision #107: one merged search, nothing cut off): copies, stepped, steps, bytes, ms.
        new object[] { "branch-heavy hand", 3_600, 4_200, 32, 290_000_000L, 60 },                // 2,874, 3,363, 25, 192.7 MB, 10 ms (was cut off at 128 branches: 926 copies)
        new object[] { "four Defects", 88_000, 45_000, 55, 1_140_000_000L, 1_500 },            // 70,192, 35,901, 44, 758.5 MB, 294 ms (six orbs each; ten: 460,000 copies, 2 s)
        new object[] { "Beacon of Hope and Juggernaut", 1_510, 350, 29, 23_000_000L, 60 },       // 1,208, 281, 23, 15.4 MB, 7 ms (two players, two enemies)
        new object[] { "typical fight", 10, 48, 27, 610_000L, 10 },                             // 6, 30, 17, 0.41 MB, 0.05 ms
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
    public void EachScenarioStaysInsideItsWork(string name, int maxCopies, int maxStepped, int maxSteps, long maxBytes, int maxMs)
    {
        var (s, plays) = Build(name);
        var c = Measure(s, plays);
        _out.WriteLine($"{name}: copies {c.Copies}, stepped {c.Stepped}, steps {c.Steps}, most copies in a step {c.MaxStepCopies}, most held {c.PeakHeld}, {c.Bytes} bytes for {plays.Count} simulations, {c.Ms:0.00} ms each");
        Assert.True(c.Copies <= maxCopies, $"{name}: {c.Copies} state copies (limit {maxCopies})");
        Assert.True(c.Stepped <= maxStepped, $"{name}: {c.Stepped} branches stepped (limit {maxStepped})");
        Assert.True(c.Steps <= maxSteps, $"{name}: {c.Steps} steps (limit {maxSteps})");
        Assert.True(c.Bytes <= maxBytes, $"{name}: {c.Bytes} bytes allocated (limit {maxBytes})");
        // Only a disaster: about five times what a dev machine takes (GitHub's runners take about three times as long).
        Assert.True(c.Ms < maxMs, $"{name}: {c.Ms:0.00} ms per simulation (limit {maxMs} ms)");
    }

    // ------------------------------------------------------------------ the bound

    /// <summary>
    /// The bound, for any fight (decision #107): every one finishes, holding well under the memory cap's share for one search
    /// (SimWork.MinCapped), so the cap is for the rare board past all of these, not for any of them. Fights from one enemy to
    /// five, one player to four, no orbs to six, Beacon of Hope and Juggernaut on one player or none; random hits, Shatter,
    /// channelling.
    /// </summary>
    [Fact]
    public void EveryFightFinishesInsideTheCap()
    {
        var worstHeld = 0;
        var worstCopies = 0;
        var runs = 0;
        foreach (var enemies in new[] { 1, 2, 3, 5 })
        foreach (var players in new[] { 1, 2, 4 })
        foreach (var orbs in new[] { 0, 3, 6 })
        foreach (var beacon in new[] { false, true })
        {
            // Beacon of Hope with Juggernaut on two or more players runs a chain inside every step on every branch (decision #107's
            // follow-up): seconds a simulation, up to the work cap. Checked on its own (SimStepBudgetTests), not here.
            if (beacon && players > 1) continue;
            // Six orbs on four players or on five enemies, or any orbs on four players against five enemies: tens of thousands of
            // distinct states (five enemies, four players with three orbs each and Shatter: 124,740 held, near the memory cap),
            // seconds each. Measured by hand, too slow for every push.
            if ((players == 4 && orbs == 6) || (enemies == 5 && orbs == 6) || (enemies == 5 && players == 4 && orbs > 0)) continue;
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
            Setup(s.You);
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
                var what = $"{enemies} enemies, {players} players, {orbs} orbs, beacon {beacon}, play {p?.Name ?? (p == null ? "none" : "?")}";
                Assert.False(o.Truncated, what);
                Assert.True(o.PeakHeld <= MaxHeldInAnyFight, $"{what}: held {o.PeakHeld} states (limit {MaxHeldInAnyFight})");
                if (o.PeakHeld > worstHeld) _out.WriteLine($"{what}: held {o.PeakHeld}, {o.Copies} copies");
                worstHeld = Math.Max(worstHeld, o.PeakHeld);
                worstCopies = Math.Max(worstCopies, o.Copies);
            }
        }
        _out.WriteLine($"{runs} fights: most states held {worstHeld}, most copies {worstCopies}");
    }

    /// <summary>The most states any fight above holds at once: measured, plus about 25%; far under SimWork.MinCapped.</summary>
    private const int MaxHeldInAnyFight = 22_000; // 17,640 (2026-11-19)
}
