using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Explosive read from its code (ExplosivePower.AfterDeath: CreatureCmd.Damage of every living player creature, Unpowered, no dealer) against the
/// engine's hand-coded rule it replaced (SimEnemy.Explosive, its explosion after each death and end-of-turn step): 300 generated fights with Explosive
/// enemies killed by your card, by a Lightning orb at your turn's end or by their poison at the enemies' turn start, you and a teammate with block,
/// Plating and HP. Each outcome's fingerprint (every branch's HP taken, block, HP, enemies, teammates and Incoming Damage's breakdown) was recorded by
/// the hand-coded rule (Cases/explosive-parity.txt) and must stay.
/// </summary>
public class ExplosiveParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "explosive-parity.txt");

    [Fact]
    public void TheReadExplosiveDoesWhatTheHandCodedRuleDid()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Fingerprint(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_EXPLOSIVE") is { Length: > 0 } recordTo)
        {
            ParityRecording.Write(recordTo, actual, ParityPath);
            return;
        }
        var expected = File.ReadAllLines(ParityPath);
        var changed = actual.Zip(expected).Where(pair => pair.First != pair.Second).Select(pair => pair.First.Split(' ')[0]).ToList();
        Assert.True(expected.Length == actual.Count && changed.Count == 0, $"Explosive outcomes changed in {changed.Count} of {actual.Count} scenarios (first: {string.Join(", ", changed.Take(10))})");
    }

    /// <summary>One generated fight's outcome, as numbers that predictions read.</summary>
    private static string Fingerprint(int index)
    {
        var random = new Random(9100 + index);
        var state = new SimState { Log = true };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 16), Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 11)).ToList() };
            state.Enemies.Add(enemy);
            if (random.Next(3) > 0) Explosive(state, enemy, random.Next(1, 11));
            if (random.Next(4) == 0)
            {
                var poison = random.Next(1, 6);
                enemy.AddPoison(poison);
                enemy.PoisonStacks = poison;
            }
        }
        var you = state.You;
        you.Hp = 30;
        you.MaxHp = 30;
        you.Block = random.Next(0, 9);
        if (random.Next(4) == 0) you.Plating = random.Next(1, 7);
        if (random.Next(3) == 0)
        {
            you.OrbCapacity = 2;
            you.Orbs.Add(SimEngine.FreshOrb(state, OrbKind.Lightning, Who.You));
        }
        if (random.Next(3) == 0) state.Teammates.Add(new SimPlayer { Hp = random.Next(1, 31), MaxHp = 30, Block = random.Next(0, 6) });
        SimPlay? play = null;
        if (random.Next(5) > 0)
        {
            var all = random.Next(3) == 0;
            var target = random.Next(enemies);
            var damage = random.Next(1, 16);
            play = new SimPlay
            {
                Name = "Strike", IsAttack = true, HitTarget = all ? SimPlay.Target.All : SimPlay.Target.Single, TargetId = all ? Who.None : target, Hits = random.Next(1, 3),
                HitDamage = Enumerable.Range(0, enemies).ToDictionary(id => id, _ => damage),
            };
        }
        var outcome = play == null ? SimEngine.Run(state) : SimEngine.Run(state, play);
        var text = new StringBuilder($"T{outcome.Truncated} ");
        var branches = outcome.Branches.Select(branch =>
        {
            var enemyText = string.Join(";", branch.Enemies.OrderBy(enemy => enemy.Id).Select(enemy => $"{enemy.Id}:{enemy.Hp},{enemy.Block},{enemy.Alive}"));
            var mates = string.Join(";", branch.Teammates.Select(mate => $"{mate.Hp},{mate.Block},{mate.Incoming},{mate.Taken}"));
            var shown = string.Join(";", branch.Blockable.Select(entry => $"{entry.Item1}={entry.Item2}"));
            return $"{branch.Taken},{branch.You.Hp},{branch.You.Block},{branch.BlockAtEnemyTurn},{branch.Weight.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}|{enemyText}|{mates}|{shown}";
        }).OrderBy(line => line, StringComparer.Ordinal);
        text.Append(string.Join("#", branches));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    /// <summary>Explosive on an enemy: the power (its code read), named as the game shows it, or the hand-coded rule's amount (SimEnemy.Explosive).</summary>
    private static void Explosive(SimState state, SimEnemy enemy, int amount)
    {
        enemy.Powers = enemy.Powers.With(PowerId.Explosive, amount);
        state.Titles[$"{enemy.Id}:{PowerId.Explosive.Name}"] = "Explosive";
        typeof(SimEnemy).GetField("Explosive", BindingFlags.Instance | BindingFlags.Public)?.SetValue(enemy, amount);
    }
}
