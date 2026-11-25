using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The wake tabs of Asleep (Lagavulin Matriarch) and Slumber (Slumbering Beetle), as a card's play shows them: 300 generated fights, sleeping
/// enemies with their stacks, Plating and block, beside an attacker, you and sometimes a teammate, an Attack aimed at each enemy, at all of them or at
/// a random one, with one to four hits, each simulated against ending the turn now and read into its tabs by the rules the game shows them by
/// (CardTabs: every tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the play to). Recorded with the
/// hand-written models (Cases/wake-tab-parity.txt, SPIREQOL_RECORD_WAKE_TABS=path writes it), the record a model read from the game's code is held to.
/// </summary>
public class WakeTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "wake-tab-parity.txt");

    [Fact]
    public void TheTabsAreWhatTheRecordShows()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_WAKE_TABS") is { Length: > 0 } recordTo)
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
        // The recording is only a proof where the tabs show: each power has a tab in some scenario.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        foreach (var name in new[] { PowerId.Asleep.Name, PowerId.Slumber.Name })
            Assert.True(lines.Any(line => line.Contains($"proc:{name} ", StringComparison.Ordinal)), $"no scenario shows {name}'s tab");
    }

    /// <summary>The kinds of enemy a scenario has: a sleeping Lagavulin Matriarch, a sleeping Slumbering Beetle, an attacker.</summary>
    internal enum Sleeper { Asleep, Slumber, Attacker }

    /// <summary>One generated fight: each enemy's kind, and the play.</summary>
    internal static (SimState State, SimPlay Play, Sleeper[] Kinds) Fight(int index)
    {
        var random = new Random(9300 + index);
        var state = new SimState { TurnNumber = 2, RoundNumber = 2 };
        var enemies = random.Next(1, 3);
        var kinds = new Sleeper[enemies];
        for (var i = 0; i < enemies; i++)
        {
            kinds[i] = (Sleeper)random.Next(3);
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(15, 60), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0 };
            var stacks = random.Next(1, 4);
            var plating = random.Next(2) == 0 ? random.Next(4, 13) : 0;
            switch (kinds[i])
            {
                case Sleeper.Asleep:
                    enemy.Powers = enemy.Powers.With(PowerId.Asleep, amount: stacks);
                    if (plating > 0) enemy.Powers = enemy.Powers.With(PowerId.Plating, amount: plating);
                    break;
                case Sleeper.Slumber:
                    enemy.Powers = enemy.Powers.With(PowerId.Slumber, amount: stacks);
                    if (plating > 0) enemy.Powers = enemy.Powers.With(PowerId.Plating, amount: plating);
                    break;
                default:
                    enemy.Hits = new List<int> { random.Next(1, 12) };
                    enemy.IntendsToAttack = true;
                    break;
            }
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
        for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 16);
        return (state, play, kinds);
    }

    /// <summary>One generated fight and play: its tabs, as text.</summary>
    private static string Tabs(int index)
    {
        var (state, play, _) = Fight(index);
        var enemies = state.Enemies.Count;
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
        return text.ToString();
    }
}
