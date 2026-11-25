using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What Hardened Shell (at most its amount of HP lost each side's turn) does to a card's play: 300 generated fights, enemies with Hardened Shell
/// (some with part of this turn's budget taken), some poisoned (the tick meets the budget whole again as the enemies' turn starts), some with a
/// power whose tab reads the HP a hit takes (Slumber, Asleep, Curl Up), with block or low on HP, attacking or not; you (sometimes with block) and
/// sometimes a teammate; an Attack aimed at each enemy, at all of them or at a random one, or a Skill that gives you block, each simulated against
/// ending the turn now and read into its tabs by the rules the game shows them by (CardTabs: every tab's key, crossing, number, chance and aim
/// marker, in order, and what the play check holds the play to), with each enemy's HP and each player's at the end of the turn, over the outcomes.
/// Recorded with the hand-written model (Cases/shell-tab-parity.txt, SPIREQOL_RECORD_SHELL_TABS=path writes it): read from its code, it shows the
/// same tabs and the same HP, but where its code places them otherwise (ReviewedTabs: each a fix, with the game's line).
/// </summary>
public class ShellTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "shell-tab-parity.txt");

    /// <summary>The powers beside the shell whose tabs read the HP a hit takes.</summary>
    private static readonly PowerId[] Beside = { PowerId.Slumber, PowerId.Asleep, PowerId.CurlUp };

    /// <summary>Scenarios whose tabs or HP a class read from its code places otherwise, each a fix: the scenario, and the game's line that moves them.</summary>
    private static readonly Dictionary<int, string> ReviewedTabs = new();

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_SHELL_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var expected = File.ReadAllLines(ParityPath).Select(MonsterStateTabParityTests.WithoutWakeTabs).ToList();
        actual = actual.Select(MonsterStateTabParityTests.WithoutWakeTabs).ToList();
        Assert.Equal(expected.Count, actual.Count);
        var changed = actual.Zip(expected, (now, was) => (Now: now, Was: was)).Select((pair, scenario) => (pair.Now, pair.Was, Scenario: scenario))
            .Where(entry => entry.Now != entry.Was && !ReviewedTabs.ContainsKey(entry.Scenario)).Select(entry => $"{entry.Was}\n  now: {entry.Now}").ToList();
        Assert.True(changed.Count == 0, $"the tabs changed in {changed.Count} of {actual.Count} scenarios:\n{string.Join("\n", changed.Take(6))}");
        var stale = ReviewedTabs.Keys.Where(scenario => actual[scenario] == expected[scenario]).ToList();
        Assert.True(stale.Count == 0, $"reviewed, but the tabs are as recorded: {string.Join(", ", stale)}");
    }

    [Fact]
    public void TheScenariosShowTheShellAtWork()
    {
        // The recording is only a proof where the shell stops something: some enemy ends the turn with more HP than it would without its shell, and
        // the tabs beside it show.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        foreach (var power in Beside)
            Assert.True(lines.Any(line => line.Contains($"proc:{power.Name} ", StringComparison.Ordinal)), $"no scenario shows {power.Name}'s tab");
        Assert.Contains(lines, line => line.Contains("aimed", StringComparison.Ordinal));
        var capped = Enumerable.Range(0, Scenarios).Count(scenario =>
        {
            var (state, play, _) = Scenario(scenario);
            var (unshelled, _, _) = Scenario(scenario);
            foreach (var enemy in unshelled.Enemies) enemy.Powers = enemy.Powers.Without(PowerId.HardenedShell);
            int Least(SimState start) => SimEngine.Run(start, play).Branches.Sum(branch => branch.Enemies.Sum(enemy => enemy.Hp));
            return Least(state) != Least(unshelled);
        });
        Assert.True(capped > 50, $"only {capped} scenarios end otherwise without the shell");
    }

    /// <summary>One generated fight and its play.</summary>
    internal static (SimState State, SimPlay Play, int Enemies) Scenario(int index)
    {
        var random = new Random(5100 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0,
                Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 12)).ToList() };
            enemy.IntendsToAttack = enemy.Hits.Count > 0;
            if (random.Next(4) != 0)
            {
                // HardenedShellPower: its amount, and what it took this turn (its Data's damageReceivedThisTurn).
                var amount = random.Next(1, 16);
                var taken = random.Next(3) == 0 ? random.Next(0, amount + 1) : 0;
                Shell(enemy, amount, taken);
            }
            if (random.Next(3) == 0) enemy.Powers = enemy.Powers.With(PowerId.Poison, random.Next(1, 12));
            if (random.Next(3) == 0 && Beside[random.Next(Beside.Length)] is var beside)
                enemy.Powers = enemy.Powers.With(beside, beside == PowerId.CurlUp ? random.Next(3, 9) : random.Next(1, 4));
            WakeTabParityTests.WithSleeperRules(enemy); // its move rules, as the adapter reads them for Asleep and Slumber (decision #307)
            state.Enemies.Add(enemy);
        }
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = 3;
        if (random.Next(2) == 0) state.You.Block = random.Next(1, 15);
        if (random.Next(3) == 0) state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2, Energy = 2, Block = random.Next(2) == 0 ? random.Next(1, 15) : 0 });
        SimPlay play;
        if (random.Next(5) == 0)
        {
            // A Skill that gives you block.
            play = new SimPlay { Name = "Played", Card = new SimCardPlay(new SimCard("Played", SimCardType.Skill) { Energy = 1, GainsBlock = true }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            play.BlockAmount = random.Next(1, 15);
            play.BlockGains = 1;
        }
        else
        {
            play = new SimPlay { Name = "Played", IsAttack = true, Card = new SimCardPlay(new SimCard("Played", SimCardType.Attack) { Energy = 1 }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            play.HitTarget = random.Next(3) switch { 0 => SimPlay.Target.Single, 1 => SimPlay.Target.All, _ => SimPlay.Target.Random };
            play.Hits = random.Next(1, 5);
            for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 20);
        }
        return (state, play, enemies);
    }

    /// <summary>
    /// Hardened Shell on an enemy at <paramref name="amount"/>, having taken <paramref name="taken"/> this turn: its own data's damageReceivedThisTurn,
    /// as the adapter reads it from the game's object (the recording was made with the hand-written model's Data, what's left: amount - taken).
    /// </summary>
    internal static void Shell(SimEnemy enemy, int amount, int taken)
    {
        enemy.Powers = enemy.Powers.With(PowerId.HardenedShell, amount);
        enemy.Fields = enemy.Fields.With(PowerId.HardenedShell.Name + ".Data.damageReceivedThisTurn", taken);
    }

    /// <summary>One generated fight and play: its tabs, and every creature's HP at the end of each aim's turn, as text.</summary>
    internal static string Tabs(int index)
    {
        var (state, play, enemies) = Scenario(index);
        var aims = play.HitTarget == SimPlay.Target.Single ? Enumerable.Range(0, enemies).ToList() : new List<int> { Who.None };
        var outcomes = aims.Select(aim =>
        {
            play.TargetId = aim == Who.None ? 0 : aim;
            return SimEngine.Run(state, play);
        }).ToArray();
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
        foreach (var enemy in state.Enemies)
            foreach (var power in enemy.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, enemy.Id, OutcomePass: 0));
        var tabs = new CardTabs { Indirect = index % 2 == 0, Chances = true, ProcNumbers = true };
        tabs.Add(sources, state, baseline, outcomes);
        var text = new StringBuilder();
        foreach (var tab in tabs.Tabs.Values)
            text.Append($"{tab.Key} {(tab.Crossed ? "crossed" : "plain")}{(tab.Chance != null ? " chance " + tab.Chance : "")}{(tab.Number is { } number ? $" {tab.Counted} {number}" : "")}"
                + $" amount {tab.Amount} {tab.AuditMin}-{tab.AuditMax}{(tab.Aimed ? " aimed" : "")}; ");
        text.Append("| checked ");
        foreach (var (source, (every, any)) in tabs.FiresBy.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            text.Append($"{source} {string.Concat(every.Select(fires => fires ? '1' : '0'))}/{string.Concat(any.Select(fires => fires ? '1' : '0'))}; ");
        // Every creature's HP at the end of the turn, least and most over each aim's outcomes, then over ending the turn now.
        text.Append("| hp ");
        string Range(SimOutcome outcome, Func<SimState, int> hp) => outcome.Branches.Count == 0 ? "-" : $"{outcome.Branches.Min(hp)}-{outcome.Branches.Max(hp)}";
        foreach (var enemy in state.Enemies)
            text.Append($"{enemy.Id} {string.Join(",", outcomes.Append(baseline).Select(outcome => Range(outcome, branch => branch.Enemy(enemy.Id)?.Hp ?? 0)))}; ");
        foreach (var player in state.PlayerIds)
            text.Append($"{player} {string.Join(",", outcomes.Append(baseline).Select(outcome => Range(outcome, branch => branch.Player(player)!.Hp)))}; ");
        return text.ToString();
    }
}
