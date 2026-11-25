using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The tabs of the enemy powers whose tab says a play stuns their owner or sets them off by a kill (Shriek, Flutter, Ravenous), as a card's play
/// shows them: 300 generated fights, enemies with those powers (some with block, some low on HP), you and sometimes a teammate, an Attack aimed at
/// each enemy, at all of them or at a random one, with one to four hits, each simulated against ending the turn now and read into its tabs by the
/// rules the game shows them by (CardTabs: every tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the
/// play to). Recorded with the hand-written models (Cases/stun-tab-parity.txt, SPIREQOL_RECORD_STUN_TABS=path writes it): a class read from its
/// code must show the same tabs.
/// </summary>
public class StunTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "stun-tab-parity.txt");

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_STUN_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var expected = File.ReadAllLines(ParityPath);
        var changed = actual.Zip(expected).Where(pair => pair.First != pair.Second).Select(pair => $"{pair.Second}\n  now: {pair.First}").ToList();
        Assert.True(expected.Length == actual.Count && changed.Count == 0, $"the tabs changed in {changed.Count} of {actual.Count} scenarios:\n{string.Join("\n", changed.Take(6))}");
    }

    [Fact]
    public void TheScenariosShowEachTab()
    {
        // The recording is only a proof where the tabs show: every source has a tab in some scenario.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        foreach (var name in new[] { PowerId.Shriek.Name, PowerId.Flutter.Name, PowerId.Ravenous.Name })
            Assert.True(lines.Any(line => line.Contains($"proc:{name} ", StringComparison.Ordinal)), $"no scenario shows {name}'s tab");
        Assert.Contains(lines, line => line.Contains("aimed", StringComparison.Ordinal));
    }

    /// <summary>One generated fight and play: its tabs, as text.</summary>
    private static string Tabs(int index)
    {
        var random = new Random(9100 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0,
                Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 12)).ToList() };
            if (random.Next(3) == 0) enemy.Powers = enemy.Powers.With(PowerId.Shriek, amount: random.Next(1, 30));
            if (random.Next(3) == 0)
            {
                // Its DamageDecrease (50): the read model's value, and the hand-written model's Data, as the adapter read it.
                enemy.Powers = enemy.Powers.With(PowerId.Flutter, amount: random.Next(1, 4), data: 50);
                state.Vars[$"{i}:{PowerId.Flutter.Name}:DamageDecrease"] = 50;
            }
            if (random.Next(3) == 0) enemy.Powers = enemy.Powers.With(PowerId.Ravenous, amount: random.Next(1, 5));
            state.Enemies.Add(enemy);
        }
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = 3;
        if (random.Next(3) == 0) state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2, Energy = 2 });
        var play = new SimPlay { Name = "Played", IsAttack = true, Card = new SimCardPlay(new SimCard("Played", SimCardType.Attack) { Energy = 1 }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        play.HitTarget = random.Next(3) switch { 0 => SimPlay.Target.Single, 1 => SimPlay.Target.All, _ => SimPlay.Target.Random };
        play.Hits = random.Next(1, 5);
        for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 20);
        // One simulation per aim: each enemy for a card aimed at one, else one.
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
        text.Append("| seen ");
        foreach (var source in sources.Select(source => (source.Model, Owner: source.Owner ?? Who.You)).Distinct())
            if (SimModels.Get(source.Model) is { } model)
                text.Append($"{source.Model}@{source.Owner} {model.Seen} {(model.HarmfulFor(source.Owner) ? "crossed" : "plain")}; ");
        // What the simulations end with for each enemy: stunned, alive, its Strength (Ravenous' lands on a stunned enemy: left out, it makes no move).
        text.Append("| enemies ");
        foreach (var enemy in state.Enemies)
            text.Append($"{enemy.Id} stunned {string.Concat(outcomes.Select(outcome => outcome.StunnedInAll(enemy.Id) ? '1' : '0'))}; ");
        return text.ToString();
    }
}
