using System.Security.Cryptography;
using System.Text;
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
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Fingerprint(Scenario(scenario))}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllLines(SourceGoldenPath, actual);
            return;
        }
        Assert.True(File.Exists(GoldenPath), $"missing {GoldenPath}: generate it with SPIREQOL_UPDATE_GOLDEN=1");
        var expected = File.ReadAllLines(GoldenPath);
        var changed = actual.Zip(expected).Where(pair => pair.First != pair.Second).Select(pair => pair.First.Split(' ')[0]).ToList();
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
    internal static (SimState State, SimPlay? Play) Scenario(int index)
    {
        var random = new Random(1000 + index);
        var state = new SimState();
        var enemies = random.Next(1, 5);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy
            {
                Id = i, Name = $"E{i}", Hp = random.Next(1, 60), Block = random.Next(0, 3) == 0 ? random.Next(1, 15) : 0,
                Hits = Enumerable.Range(0, random.Next(0, 4)).Select(_ => random.Next(1, 14)).ToList(),
            };
            if (random.Next(6) == 0) enemy.Thorns = random.Next(1, 4);
            if (random.Next(6) == 0) enemy.Barricade = true;
            if (random.Next(8) == 0) enemy.CurlUp = random.Next(3, 10);
            if (random.Next(8) == 0) enemy.Imbalanced = true;
            if (random.Next(10) == 0) enemy.Powers = enemy.Powers.With(PowerId.HardToKill, amount: 1);
            if (random.Next(5) == 0) enemy.AddPoison(random.Next(1, 8));
            state.Enemies.Add(enemy);
        }
        var player = state.You;
        player.Block = random.Next(0, 3) == 0 ? random.Next(1, 20) : 0;
        if (random.Next(5) == 0) player.Plating = random.Next(1, 6);
        if (random.Next(6) == 0) player.Juggernaut = random.Next(2, 6);
        if (random.Next(6) == 0) player.Thorns = random.Next(1, 4);
        if (random.Next(6) == 0) player.FlameBarrier = random.Next(2, 8);
        if (random.Next(8) == 0) player.Buffer = 1;
        if (random.Next(8) == 0) player.TungstenRod = 1;
        if (random.Next(8) == 0) player.FeelNoPain = random.Next(2, 5);
        if (random.Next(10) == 0) player.Powers = player.Powers.With(PowerId.Reflect, amount: 1);
        if (random.Next(2) == 0)
        {
            player.OrbCapacity = random.Next(1, 6);
            var kinds = new[] { OrbKind.Lightning, OrbKind.Frost, OrbKind.Dark, OrbKind.Glass };
            for (var orb = 0; orb < random.Next(0, player.OrbCapacity + 1); orb++)
            {
                var orbKind = kinds[random.Next(kinds.Length)];
                player.Orbs.Add(orbKind switch
                {
                    OrbKind.Lightning => new SimOrb { Kind = orbKind, Passive = 3, Evoke = 8 },
                    OrbKind.Frost => new SimOrb { Kind = orbKind, Passive = 2, Evoke = 5 },
                    OrbKind.Dark => new SimOrb { Kind = orbKind, Passive = 6, Evoke = random.Next(6, 20) },
                    _ => new SimOrb { Kind = orbKind, Passive = 4, Evoke = 8, Fields = NamedCounts.Empty.With("_passiveVal", amount: 4) },
                });
            }
        }
        if (random.Next(4) == 0) return (state, null); // end the turn now

        var play = new SimPlay { Name = "P", IsAttack = random.Next(2) == 0 };
        if (play.IsAttack)
        {
            play.HitTarget = (SimPlay.Target)random.Next(1, 4);
            play.TargetId = random.Next(enemies);
            play.Hits = random.Next(1, 4);
            for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 15);
        }
        if (random.Next(3) == 0) { play.BlockGains = 1; play.BlockAmount = random.Next(3, 12); }
        if (random.Next(5) == 0) { play.Poison = random.Next(1, 8); play.PoisonAll = random.Next(2) == 0; if (!play.PoisonAll) play.TargetId = random.Next(enemies); }
        if (random.Next(6) == 0) { play.RandomPoison = random.Next(1, 5); play.RandomPoisonTimes = random.Next(1, 4); }
        if (random.Next(5) == 0) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = random.Next(1, 3) });
        if (random.Next(5) == 0) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } });
        if (random.Next(8) == 0) play.AddPlating = random.Next(1, 5);
        if (random.Next(8) == 0) play.AddFlameBarrier = random.Next(2, 6);
        if (random.Next(6) == 0) play.Procs.Add(new ProcHit { Spread = SimPlay.Target.Random, Damage = Enumerable.Range(0, enemies).ToDictionary(enemyId => enemyId, _ => random.Next(1, 8)), Times = random.Next(1, 3) });
        if (random.Next(10) == 0) play.FocusDelta = random.Next(-2, 3);
        return (state, play);
    }

    /// <summary>What a single-player outcome says: every branch's numbers that predictions read, sorted.</summary>
    private static string Fingerprint((SimState State, SimPlay? Play) statePlay)
    {
        var outcome = SimEngine.Run(statePlay.State, statePlay.Play);
        var text = new StringBuilder();
        text.Append($"T{outcome.Truncated} K[{string.Join(",", outcome.KilledByPlayInAll.OrderBy(x => x))}|{string.Join(",", outcome.KilledByPlayInAny.OrderBy(x => x))}] ");
        var reacted = outcome.Branches.SelectMany(branch => branch.Enemies.Where(enemy => enemy.ReactedWithBlock).Select(enemy => enemy.Id)).Distinct().Order();
        text.Append($"B{outcome.PlayBlockGainsMax} {outcome.PlayKillsInAll} {outcome.PlayBreaksBlockInAll} R[{string.Join(",", reacted)}] ");
        var branches = outcome.Branches.Select(branch =>
        {
            var enemies = string.Join(";", branch.Enemies.OrderBy(enemy => enemy.Id).Select(enemy =>
                $"{enemy.Id}:{enemy.Hp},{enemy.Block},{enemy.Alive},{enemy.DiedBeforeActing},{enemy.Stunned},{enemy.Poison},{enemy.FirstHitFullyBlocked}"));
            return $"{branch.Taken},{branch.OstyTaken},{branch.DamageThrough},{branch.BlockAtEnemyTurn},{branch.CancelledHits},{branch.You.Block},{branch.BlockDemand},{branch.Weight.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}|{enemies}";
        }).OrderBy(line => line, StringComparer.Ordinal);
        text.Append(string.Join("#", branches));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }
}
