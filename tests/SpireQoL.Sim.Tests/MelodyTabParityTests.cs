using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The tabs of Song Path and Violent Chord (read from their code: the first Cho or Wah its player started this turn), as a card's play shows them:
/// 300 generated fights, you with either or both, a card with no melody, a Wah, a Cho or both, played once or twice, with a melody already played
/// this turn or none, each simulated against ending the turn now and read into its tabs by the rules the game shows them by (CardTabs: every tab's
/// key, crossing, number, chance and aim marker, in order, and what the play check holds the play to). Recorded with the hand-written models
/// (Cases/melody-tab-parity.txt, SPIREQOL_RECORD_MELODY_TABS=path writes it): a class read from its code must show the same tabs. No play here
/// starts another inside it (where the read models follow the game and the hand-written ones didn't: ProcModelTests).
/// </summary>
public class MelodyTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "melody-tab-parity.txt");

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_MELODY_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var expected = File.ReadAllLines(ParityPath);
        var changed = actual.Zip(expected).Where(pair => Compared(pair.First) != Compared(pair.Second)).Select(pair => $"{pair.Second}\n  now: {pair.First}").ToList();
        Assert.True(expected.Length == actual.Count && changed.Count == 0, $"the tabs changed in {changed.Count} of {actual.Count} scenarios:\n{string.Join("\n", changed.Take(6))}");
    }

    /// <summary>
    /// What the play check holds a class to, where reading it from its code changed it on purpose (the tabs never change): the class, and why. A
    /// read model is held to its code's flashes (decision #199), a hand-written one to its acting log.
    /// </summary>
    private static readonly Dictionary<string, string> ReviewedChecks = new(StringComparer.Ordinal)
    {
        [PowerId.ViolentChord.Name] = "its code flashes before its damage to every hittable enemy, so on a first Wah that has killed every enemy it still flashes (the game's Flash(), then CreatureCmd.Damage on none), where the hand-written model logged nothing with no enemy to hit; its tab is unchanged",
    };

    /// <summary>A scenario's line as compared: the play check's entry for a reviewed class left out.</summary>
    private static string Compared(string line)
    {
        foreach (var name in ReviewedChecks.Keys)
            line = System.Text.RegularExpressions.Regex.Replace(line, $@"(\| checked [^|]*?){name} [01]+/[01]+; ", "$1");
        return line;
    }

    [Fact]
    public void TheScenariosShowEachTab()
    {
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        foreach (var name in new[] { PowerId.SongPath.Name, PowerId.ViolentChord.Name })
            Assert.True(lines.Any(line => line.Contains($"proc:{name} ", StringComparison.Ordinal)), $"no scenario shows {name}'s tab");
    }

    /// <summary>One generated fight and play: its tabs, as text.</summary>
    private static string Tabs(int index)
    {
        var random = new Random(9700 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
            state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0, Hits = new List<int> { random.Next(1, 12) } });
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = random.Next(0, 6);
        if (random.Next(3) != 0) state.You.Powers = state.You.Powers.With(PowerId.SongPath, amount: random.Next(1, 6));
        if (random.Next(3) != 0) state.You.Powers = state.You.Powers.With(PowerId.ViolentChord, amount: random.Next(1, 6));
        // A melody already played this turn, or none.
        foreach (var earlier in new[] { SimCardTag.Wah, SimCardTag.Cho })
            if (random.Next(3) == 0)
                state.You.Turn = SimKeys.CountPlay(SimKeys.CountPlay(state.You.Turn, PlayStage.Started, SimCardType.Skill, new[] { earlier }), PlayStage.Finished, SimCardType.Skill, new[] { earlier });
        var type = random.Next(3) == 0 ? SimCardType.Attack : SimCardType.Skill;
        var tags = random.Next(4) switch
        {
            0 => new HashSet<SimCardTag>(), 1 => new HashSet<SimCardTag> { SimCardTag.Wah }, 2 => new HashSet<SimCardTag> { SimCardTag.Cho }, _ => new HashSet<SimCardTag> { SimCardTag.Wah, SimCardTag.Cho },
        };
        var card = new SimCard("Played", type) { Energy = random.Next(0, 3), Tags = tags };
        var play = new SimPlay { Name = "Played", IsAttack = type == SimCardType.Attack, Plays = random.Next(4) == 0 ? 2 : 1,
            Card = new SimCardPlay(card, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        if (play.IsAttack)
        {
            play.HitTarget = SimPlay.Target.All;
            play.Hits = 1;
            for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 10);
        }
        play.TargetId = play.IsAttack ? 0 : Who.None;
        var outcomes = new[] { SimEngine.Run(state, play) };
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
        foreach (var power in state.You.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, null, OutcomePass: 2));
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
        foreach (var source in sources.Select(source => source.Model).Distinct())
            if (SimModels.Get(source) is { } model)
                text.Append($"{source} {model.Seen} {(model.HarmfulFor(Who.You) ? "crossed" : "plain")}; ");
        // What the simulation ends with: the enemies' HP, your block.
        text.Append($"| hp {string.Join(",", outcomes[0].Branches.Select(branch => string.Join("/", branch.Enemies.Select(enemy => enemy.Hp))).Distinct())} block {string.Join(",", outcomes[0].Branches.Select(branch => branch.You.Block).Distinct())}");
        return text.ToString();
    }
}
