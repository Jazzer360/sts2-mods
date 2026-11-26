using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The Gambit's tab on an enemy (its kill-itself outcome rule: a play whose powered attack gets HP through its block), as a card's play shows it:
/// 300 generated fights, enemies with The Gambit (some with block, some with Slippery, which holds the HP lost to 1), you and sometimes a teammate,
/// an Attack aimed at each enemy, at all of them or at a random one, with one to four hits, each simulated against ending the turn now and read
/// into its tabs by the rules the game shows them by (CardTabs: every tab's key, crossing, number, chance and aim marker, in order, and what the
/// play check holds the play to). Recorded with the hand-written model (Cases/gambit-tab-parity.txt, SPIREQOL_RECORD_GAMBIT_TABS=path writes it):
/// read from its code it must show the same tabs. The game puts The Gambit only on its card's player (the owner board), where it has no tab; an
/// enemy's is the rule kept as it was.
/// </summary>
public class GambitTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "gambit-tab-parity.txt");

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_GAMBIT_TABS") is { Length: > 0 } recordTo)
        {
            ParityRecording.Write(recordTo, actual, ParityPath);
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
        foreach (var name in new[] { PowerId.TheGambit.Name })
            Assert.True(lines.Any(line => line.Contains($"proc:{name} ", StringComparison.Ordinal)), $"no scenario shows {name}'s tab");
        Assert.Contains(lines, line => line.Contains("aimed", StringComparison.Ordinal));
    }

    /// <summary>One generated fight and play: its tabs, as text.</summary>
    private static string Tabs(int index)
    {
        var random = new Random(9400 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0,
                Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 12)).ToList() };
            if (random.Next(3) != 0) enemy.Powers = enemy.Powers.With(PowerId.TheGambit, amount: 1);
            if (random.Next(4) == 0) enemy.Powers = enemy.Powers.With(PowerId.Slippery, amount: random.Next(1, 3));
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
        // What the simulations end with for each enemy: dead in every branch, and its Gambit still on it in every branch.
        text.Append("| enemies ");
        foreach (var enemy in state.Enemies)
            text.Append($"{enemy.Id} dead {string.Concat(outcomes.Select(outcome => outcome.DeadInAll(enemy.Id) ? '1' : '0'))} gambit {string.Concat(outcomes.Select(outcome => outcome.Branches.All(branch => branch.Enemy(enemy.Id)!.Powers.Has(PowerId.TheGambit)) ? '1' : '0'))}; ");
        return text.ToString();
    }
}
