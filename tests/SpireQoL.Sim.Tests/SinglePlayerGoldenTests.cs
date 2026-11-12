using System.Security.Cryptography;
using System.Text;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Single-player results must never change by accident (decision #24: single-player first). A fixed
/// set of generated single-player fights and plays (test data from a fixed seed, nothing from the game)
/// is simulated and each outcome's fingerprint compared with Cases/single-player-golden.txt, recorded
/// from a released engine. A change to single-player results fails here; when it's intended (a new
/// rule, a fix), regenerate the file with SPIREQOL_UPDATE_GOLDEN=1 and say why in the commit.
/// </summary>
public class SinglePlayerGoldenTests
{
    private const int Scenarios = 600;
    private static readonly string GoldenPath = Path.Combine(AppContext.BaseDirectory, "Cases", "single-player-golden.txt");
    private static string SourceGoldenPath => Path.Combine(FindRepoTests(), "Cases", "single-player-golden.txt");

    [Fact]
    public void SinglePlayerOutcomesAreUnchanged()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(i => $"{i} {Fingerprint(Scenario(i))}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllLines(SourceGoldenPath, actual);
            return;
        }
        Assert.True(File.Exists(GoldenPath), $"missing {GoldenPath}: generate it with SPIREQOL_UPDATE_GOLDEN=1");
        var expected = File.ReadAllLines(GoldenPath);
        var changed = actual.Zip(expected).Where(p => p.First != p.Second).Select(p => p.First.Split(' ')[0]).ToList();
        Assert.True(expected.Length == actual.Count && changed.Count == 0,
            $"single-player outcomes changed in {changed.Count} of {actual.Count} scenarios (first: {string.Join(", ", changed.Take(10))})");
    }

    private static string FindRepoTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SpireQoL.Sim.Tests.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("test project folder not found");
    }

    /// <summary>One generated single-player fight and play (no teammates).</summary>
    private static (SimState State, SimPlay? Play) Scenario(int index)
    {
        var r = new Random(1000 + index);
        var s = new SimState();
        var enemies = r.Next(1, 5);
        for (var i = 0; i < enemies; i++)
        {
            var e = new SimEnemy
            {
                Id = i, Name = $"E{i}", Hp = r.Next(1, 60), Block = r.Next(0, 3) == 0 ? r.Next(1, 15) : 0,
                Hits = Enumerable.Range(0, r.Next(0, 4)).Select(_ => r.Next(1, 14)).ToList(),
            };
            if (r.Next(6) == 0) e.Thorns = r.Next(1, 4);
            if (r.Next(6) == 0) e.Barricade = true;
            if (r.Next(8) == 0) e.CurlUp = r.Next(3, 10);
            if (r.Next(8) == 0) e.Imbalanced = true;
            if (r.Next(10) == 0) e.HitCap = 1;
            if (r.Next(5) == 0) e.AddPoison(r.Next(1, 8));
            s.Enemies.Add(e);
        }
        var p = s.P;
        p.Block = r.Next(0, 3) == 0 ? r.Next(1, 20) : 0;
        if (r.Next(5) == 0) p.Plating = r.Next(1, 6);
        if (r.Next(6) == 0) p.Juggernaut = r.Next(2, 6);
        if (r.Next(6) == 0) p.Thorns = r.Next(1, 4);
        if (r.Next(6) == 0) p.FlameBarrier = r.Next(2, 8);
        if (r.Next(8) == 0) p.Buffer = 1;
        if (r.Next(8) == 0) p.TungstenRod = 1;
        if (r.Next(8) == 0) p.FeelNoPain = r.Next(2, 5);
        if (r.Next(10) == 0) p.Reflect = true;
        if (r.Next(2) == 0)
        {
            p.OrbCapacity = r.Next(1, 6);
            var kinds = new[] { OrbKind.Lightning, OrbKind.Frost, OrbKind.Dark, OrbKind.Glass };
            for (var o = 0; o < r.Next(0, p.OrbCapacity + 1); o++)
            {
                var k = kinds[r.Next(kinds.Length)];
                p.Orbs.Add(k switch
                {
                    OrbKind.Lightning => new SimOrb { Kind = k, Passive = 3, Evoke = 8 },
                    OrbKind.Frost => new SimOrb { Kind = k, Passive = 2, Evoke = 5 },
                    OrbKind.Dark => new SimOrb { Kind = k, Passive = 6, Evoke = r.Next(6, 20) },
                    _ => new SimOrb { Kind = k, Passive = 4, Evoke = 8, GlassBase = 4 },
                });
            }
        }
        if (r.Next(4) == 0) return (s, null); // end the turn now

        var play = new SimPlay { Name = "P", IsAttack = r.Next(2) == 0 };
        if (play.IsAttack)
        {
            play.HitTarget = (SimPlay.Target)r.Next(1, 4);
            play.TargetId = r.Next(enemies);
            play.Hits = r.Next(1, 4);
            for (var i = 0; i < enemies; i++) play.HitDamage[i] = r.Next(1, 15);
        }
        if (r.Next(3) == 0) { play.BlockGains = 1; play.BlockAmount = r.Next(3, 12); }
        if (r.Next(5) == 0) { play.Poison = r.Next(1, 8); play.PoisonAll = r.Next(2) == 0; if (!play.PoisonAll) play.TargetId = r.Next(enemies); }
        if (r.Next(6) == 0) { play.RandomPoison = r.Next(1, 5); play.RandomPoisonTimes = r.Next(1, 4); }
        if (r.Next(5) == 0) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = r.Next(1, 3) });
        if (r.Next(5) == 0) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } });
        if (r.Next(8) == 0) play.AddPlating = r.Next(1, 5);
        if (r.Next(8) == 0) play.AddFlameBarrier = r.Next(2, 6);
        if (r.Next(6) == 0) play.Procs.Add(new ProcHit { Spread = SimPlay.Target.Random, Damage = Enumerable.Range(0, enemies).ToDictionary(i => i, _ => r.Next(1, 8)), Times = r.Next(1, 3) });
        if (r.Next(10) == 0) play.FocusDelta = r.Next(-2, 3);
        return (s, play);
    }

    /// <summary>What a single-player outcome says: every branch's numbers that predictions read, sorted.</summary>
    private static string Fingerprint((SimState State, SimPlay? Play) sc)
    {
        var o = SimEngine.Run(sc.State, sc.Play);
        var sb = new StringBuilder();
        sb.Append($"T{o.Truncated} K[{string.Join(",", o.KilledByPlayInAll.OrderBy(x => x))}|{string.Join(",", o.KilledByPlayInAny.OrderBy(x => x))}] ");
        sb.Append($"B{o.PlayBlockGainsMax} {o.PlayKillsInEveryBranch} {o.PlayBreaksBlockInEveryBranch} R[{string.Join(",", o.BlockReactionInAny.OrderBy(x => x))}] ");
        if (o.SafeWorst is { } w) sb.Append($"S{w.Taken}/{w.OstyTaken} ");
        var branches = o.Branches.Select(b =>
        {
            var e = string.Join(";", b.Enemies.OrderBy(x => x.Id).Select(x =>
                $"{x.Id}:{x.Hp},{x.Block},{x.Alive},{x.DiedBeforeActing},{x.Stunned},{x.Poison},{x.FirstHitFullyBlocked}"));
            return $"{b.Taken},{b.OstyTaken},{b.DamageThrough},{b.BlockAtEnemyTurn},{b.CancelledHits},{b.P.Block},{b.BlockDemand},{b.Weight:0.######}|{e}";
        }).OrderBy(x => x, StringComparer.Ordinal);
        sb.Append(string.Join("#", branches));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }
}
