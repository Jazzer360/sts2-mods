using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The tabs of the models that summon (Devour Life on a Soul you play, Sic 'Em on its enemy when your Osty damages it), as a card's play shows
/// them: 300 generated fights, Osty at 0 to 9 HP, your Devour Life, enemies with your Sic 'Em, a Soul or an Attack of one to three hits by you or by
/// Osty, aimed at one enemy or all of them, each simulated against ending the turn now and read into its tabs by the rules the game shows them by
/// (CardTabs: every tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the play to). Recorded with the
/// hand-written models (Cases/summon-tab-parity.txt, SPIREQOL_RECORD_SUMMON_TABS=path writes it): read from their code they must show the same
/// tabs. The summons themselves now happen (Osty's HP after the play), which the hand-written models only logged: not part of the recording.
/// </summary>
public class SummonTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "summon-tab-parity.txt");

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_SUMMON_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var expected = File.ReadAllLines(ParityPath).Select(Reviewed).ToArray();
        var changed = actual.Zip(expected).Where(pair => pair.First != pair.Second).Select(pair => $"{pair.Second}\n  now: {pair.First}").ToList();
        Assert.True(expected.Length == actual.Count && changed.Count == 0, $"the tabs changed in {changed.Count} of {actual.Count} scenarios:\n{string.Join("\n", changed.Take(6))}");
    }

    [Fact]
    public void TheScenariosShowEachTab()
    {
        // The recording is only a proof where the tabs show: every source has a tab in some scenario.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        foreach (var name in new[] { PowerId.DevourLife.Name, PowerId.SicEm.Name })
            Assert.True(lines.Any(line => line.Contains($"proc:{name} ", StringComparison.Ordinal)), $"no scenario shows {name}'s tab");
    }

    /// <summary>
    /// The recording as reviewed: what the play check holds a play to (FiresBy) for a read model is its code's flashes (decision #199), and neither
    /// DevourLifePower.AfterCardPlayed nor SicEmPower.AfterDamageGiven calls Flash(): none expected, where the hand-written models expected one
    /// wherever they acted. The in-game check expects only a power whose code flashes (CodeIndex.FlashesOutsideAmountHooks). Their tabs are unchanged.
    /// </summary>
    private static string Reviewed(string line) => System.Text.RegularExpressions.Regex.Replace(line, @"(?<=\| checked [^|]*)(DevourLifePower|SicEmPower) ([01]+)/([01]+)",
        match => match.Groups[1].Value + " " + new string('0', match.Groups[2].Length) + "/" + new string('0', match.Groups[3].Length));

    /// <summary>One generated fight and play: its tabs, as text.</summary>
    private static string Tabs(int index)
    {
        var random = new Random(9900 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0, Hits = new List<int> { random.Next(1, 12) } };
            // Yours (its applier you, as the adapter reads it: only your instance).
            if (random.Next(2) == 0) enemy.Powers = enemy.Powers.With(PowerId.SicEm, amount: random.Next(1, 5), data: DataCreature.Of(Who.You));
            state.Enemies.Add(enemy);
        }
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.OstyHp = random.Next(0, 10);
        state.You.OstyMaxHp = Math.Max(state.You.OstyHp, 1);
        if (random.Next(2) == 0) state.You.Powers = state.You.Powers.With(PowerId.DevourLife, amount: random.Next(1, 4));
        var soul = random.Next(3) == 0;
        var byOsty = !soul && random.Next(2) == 0;
        var card = soul ? new SimCard(CardId.Soul.Name, SimCardType.Skill) { Energy = 0 } : new SimCard("Played", SimCardType.Attack) { Energy = 1 };
        var play = new SimPlay { Name = card.Name, IsAttack = !soul, ByOsty = byOsty, Card = new SimCardPlay(card, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        if (!soul)
        {
            play.HitTarget = random.Next(2) == 0 ? SimPlay.Target.Single : SimPlay.Target.All;
            play.Hits = random.Next(1, 4);
            for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 12);
        }
        // One simulation per aim: each enemy for a card aimed at one, else one.
        var aims = play.HitTarget == SimPlay.Target.Single && !soul ? Enumerable.Range(0, enemies).ToList() : new List<int> { Who.None };
        var outcomes = aims.Select(aim =>
        {
            play.TargetId = aim == Who.None ? 0 : aim;
            return SimEngine.Run(state, play);
        }).ToArray();
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
        foreach (var enemy in state.Enemies)
            foreach (var power in enemy.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, enemy.Id, OutcomePass: 0));
        foreach (var power in state.You.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, null, OutcomePass: 1));
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
