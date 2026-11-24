using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The orbs read from their classes' code (decision #214) against the engine's hand-coded orb rules they replaced: 400 generated fights with
/// Lightning, Frost, Dark and Glass orbs (Glass's decayed, Dark's grown), Focus, Infused Core, Thunder, Gold-Plated Cables and a teammate's orbs,
/// each played with a card that channels by kind, evokes the front orb (once, twice, keeping it), evokes every orb twice and changes Focus, then
/// to the enemies' turn. Each outcome's fingerprint (every branch's enemies, block, HP taken, weight, orbs, the end-of-turn breakdown and the play
/// log) was recorded by the hand-coded rules (Cases/orb-parity.txt, written at 44034ee) and must stay. What the read code does on purpose that
/// the hand-coded rules left out is outside these boards and tested apart: Plasma's evoke energy, Frost's block for the others with Hibernate.
/// </summary>
public class OrbParityTests
{
    private const int Scenarios = 400;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "orb-parity.txt");

    [Fact]
    public void TheReadOrbsDoWhatTheHandCodedRulesDid()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Fingerprint(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_ORBS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var expected = File.ReadAllLines(ParityPath);
        var changed = actual.Zip(expected).Where(pair => pair.First != pair.Second).Select(pair => pair.First.Split(' ')[0]).ToList();
        Assert.True(expected.Length == actual.Count && changed.Count == 0, $"orb outcomes changed in {changed.Count} of {actual.Count} scenarios (first: {string.Join(", ", changed.Take(10))})");
    }

    /// <summary>One generated fight's outcome, as numbers that predictions read.</summary>
    private static string Fingerprint(int index)
    {
        var random = new Random(7000 + index);
        var state = new SimState { Log = true };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
            state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(0, 3) == 0 ? random.Next(1, 10) : 0, Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 12)).ToList() });
        var you = state.You;
        you.Focus = random.Next(-3, 4);
        if (random.Next(4) == 0)
        {
            you.Relics = you.Relics.With(RelicId.InfusedCore, amount: 1);
            state.Vars[$"{Who.You}:InfusedCore:ExtraDamage"] = random.Next(1, 3);
        }
        if (random.Next(4) == 0) you.Thunder = random.Next(2, 7);
        if (random.Next(5) == 0) FrontBonus(you);
        you.OrbCapacity = random.Next(1, 5);
        var kinds = new[] { OrbKind.Lightning, OrbKind.Frost, OrbKind.Dark, OrbKind.Glass };
        for (var orb = 0; orb < random.Next(0, you.OrbCapacity + 1); orb++)
        {
            var made = SimEngine.FreshOrb(state, kinds[random.Next(kinds.Length)], Who.You);
            if (made.Kind == OrbKind.Dark) made.Evoke += random.Next(0, 12);
            you.Orbs.Add(made);
        }
        if (random.Next(5) == 0)
        {
            var mate = new SimPlayer { Hp = 30, MaxHp = 30, OrbCapacity = 2 };
            state.Teammates.Add(mate);
            mate.Orbs.Add(SimEngine.FreshOrb(state, OrbKind.Frost, Who.Teammate(0)));
            mate.Orbs.Add(SimEngine.FreshOrb(state, OrbKind.Lightning, Who.Teammate(0)));
        }
        var body = new List<SimAction>();
        for (var step = 0; step < random.Next(0, 4); step++)
            body.Add(random.Next(6) switch
            {
                0 => new OrbAction(OrbAction.Op.Channel) { Orb = kinds[random.Next(kinds.Length)] },
                1 => new OrbAction(OrbAction.Op.EvokeFront) { Times = random.Next(1, 3) },
                2 => new OrbAction(OrbAction.Op.EvokeFront) { Times = 2, Keeps = true },
                3 => new OrbAction(OrbAction.Op.EvokeAllTwice),
                4 => new FocusAction(random.Next(-2, 3)),
                _ => new OrbAction(OrbAction.Op.Slots) { Slots = random.Next(-1, 2) },
            });
        var outcome = body.Count == 0 ? SimEngine.Run(state) : SimEngine.Run(state, ScriptBuilder.Play(ScriptBuilder.Card("Orbs", SimCardType.Skill), target: Who.None, plays: 1, body.ToArray()));
        var text = new StringBuilder($"T{outcome.Truncated} ");
        var branches = outcome.Branches.Select(branch =>
        {
            var enemyText = string.Join(";", branch.Enemies.OrderBy(enemy => enemy.Id).Select(enemy => $"{enemy.Id}:{enemy.Hp},{enemy.Block},{enemy.Alive}"));
            var orbs = string.Join(";", branch.You.Orbs.Select(orb => $"{orb.Kind}{orb.Passive}/{orb.Evoke}"));
            var mates = string.Join(";", branch.Teammates.Select(mate => $"{mate.Block}"));
            // The breakdown where an enemy is left: with none hittable an orb's damage isn't dealt (LightningOrb.ApplyLightningDamage returns at
            // list.Count == 0, GlassOrb's Damage of no one), which the read code doesn't list and the hand-coded rules did (reviewed).
            var shown = !branch.Enemies.Any(enemy => enemy.Alive) ? "-" : string.Join(";", branch.EndOfTurnDamage.Select(entry => $"{entry.Item1}={entry.Item2}").Concat(branch.EndOfTurnBlock.Select(entry => $"{entry.Item1}+{entry.Item2}")));
            var fired = string.Join(";", branch.Fired.Items.Where(entry => entry.Name.Contains("Orb", StringComparison.Ordinal) || entry.Name.Contains("Thunder", StringComparison.Ordinal))
                .Select(entry => (Name: entry.Name == "OrbEvokeBlock" ? "FrostOrb" : entry.Name, entry.Amount)) // the evoke block's log key, named for Frost when recorded
                .OrderBy(entry => entry.Name, StringComparer.Ordinal).Select(entry => $"{entry.Name}={entry.Amount}"));
            return $"{branch.Taken},{branch.You.Block},{branch.BlockAtEnemyTurn},{branch.Weight.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}|{enemyText}|{orbs}|{mates}|{shown}|{fired}";
        }).OrderBy(line => line, StringComparer.Ordinal);
        text.Append(string.Join("#", branches));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    /// <summary>Gold-Plated Cables: the relic (its code read: one more trigger on the front orb), or the hand-coded rules' count of it.</summary>
    private static void FrontBonus(SimPlayer you)
    {
        you.Relics = you.Relics.With(RelicId.GoldPlatedCables, amount: 1);
        typeof(SimPlayer).GetField("FrontOrbBonusTriggers", BindingFlags.Instance | BindingFlags.Public)?.SetValue(you, 1);
    }
}
