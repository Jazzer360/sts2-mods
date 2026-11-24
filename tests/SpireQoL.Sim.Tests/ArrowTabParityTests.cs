using System.Text;
using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The tabs of the powers that act on a shot (Explosive Arrows' damage tab; Covering Fire's block, which has no tab) and of the relics and powers
/// that hear an arrow come back (Twine, Yah), as a shot's play shows them: 300 generated fights, you (and sometimes a teammate) with Explosive Arrows,
/// Covering Fire, Yah and Twine, enemies of every HP (a shot that kills lets its arrow go), a shot with an arrow or without, aimed at each enemy,
/// each simulated against ending the turn now and read into its tabs (CardTabs: every tab's key, crossing, number, chance and aim marker, in order,
/// and what the play check holds the play to), with what each player's block and arrows come to. Recorded with the hand-written models
/// (Cases/arrow-tab-parity.txt, SPIREQOL_RECORD_ARROW_TABS=path writes it): each class read from its code must show the same.
/// </summary>
public class ArrowTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "arrow-tab-parity.txt");

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_ARROW_TABS") is { Length: > 0 } recordTo)
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
        ["ExplosiveArrowsPower"] = "its code flashes whenever its owner's arrow impales (ExplosiveArrowsPower.AfterArrowImpaled: Flash() before its damage), with no enemy left to hit too (the shot killed the last), where the hand-written model logged only damage it dealt: the play check now expects that flash; its tab is the same",
    };

    /// <summary>A scenario's line as compared: the play check's entry for a reviewed class left out, and every entry no aim sets off.</summary>
    private static string Compared(string line)
    {
        foreach (var name in ReviewedChecks.Keys)
            line = System.Text.RegularExpressions.Regex.Replace(line, $@"(\| checked [^|]*?){name} [01]+/[01]+; ", "$1");
        while (System.Text.RegularExpressions.Regex.Match(line, @"(\| checked [^|]*?)\w+ 0+/0+; ") is { Success: true } none)
            line = line.Remove(none.Index + none.Groups[1].Length, none.Length - none.Groups[1].Length);
        return line;
    }

    [Fact]
    public void TheScenariosShowEachTab()
    {
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        Assert.Contains(lines, line => line.Contains("proc:ExplosiveArrowsPower ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("proc:Twine ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("aimed", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("mate block 0", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("mate block ", StringComparison.Ordinal) && !line.Contains("mate block 0", StringComparison.Ordinal));
    }

    /// <summary>One generated fight and shot: its tabs, as text, and what each player's block and arrows come to.</summary>
    private static string Tabs(int index)
    {
        var random = new Random(9100 + index);
        var state = new SimState();
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 30), Block = random.Next(3) == 0 ? random.Next(1, 8) : 0,
                Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 10)).ToList() };
            if (random.Next(3) == 0) enemy.Powers = enemy.Powers.With(PowerId.Impaled, amount: random.Next(1, 3), data: random.Next(1, 4));
            state.Enemies.Add(enemy);
        }
        Player(random, state, Who.You);
        if (random.Next(2) == 0)
        {
            state.Teammates.Add(new SimPlayer());
            Player(random, state, Who.Teammate(teammateIndex: 0));
        }
        var damage = random.Next(1, 12);
        var hits = random.Next(1, 3);
        var outcomes = Enumerable.Range(0, enemies).Select(aim =>
            SimEngine.Run(state, Play(Card("Shot"), target: aim, plays: random.Next(1, 3), new ShootAction(), Attack(baseDamage: damage, hits: hits, props: SimProps.Move | SimProps.FromArrow), new ImpaleAction()))).ToArray();
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
        void Add(SimPlayer player, int? owner)
        {
            foreach (var power in player.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, owner, OutcomePass: 2));
            foreach (var relic in player.Relics.Items) sources.Add(new TabSource(relic.Name.Name, relic.Name.Name, owner, OutcomePass: 1));
        }
        Add(state.You, owner: null);
        foreach (var enemy in state.Enemies)
            foreach (var power in enemy.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, enemy.Id, OutcomePass: 0));
        for (var i = 0; i < state.Teammates.Count; i++) Add(state.Teammates[i], Who.Teammate(i));
        var tabs = new CardTabs { Indirect = index % 2 == 0, Chances = true, ProcNumbers = true };
        tabs.Add(sources, state, baseline, outcomes);
        var text = new StringBuilder();
        foreach (var tab in tabs.Tabs.Values)
            text.Append($"{tab.Key} {(tab.Crossed ? "crossed" : "plain")}{(tab.Chance != null ? " chance " + tab.Chance : "")}{(tab.Number is { } number ? $" {tab.Counted} {number}" : "")}"
                + $" amount {tab.Amount} {tab.AuditMin}-{tab.AuditMax}{(tab.Aimed ? " aimed" : "")}; ");
        text.Append("| checked ");
        foreach (var (source, (every, any)) in tabs.FiresBy.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            text.Append($"{source} {string.Concat(every.Select(fires => fires ? '1' : '0'))}/{string.Concat(any.Select(fires => fires ? '1' : '0'))}; ");
        // What the shot left each player with, after the turn: block and arrows (Covering Fire's block, the arrows that come back).
        text.Append("| after ");
        foreach (var outcome in outcomes)
            text.Append($"[{string.Join(",", outcome.Branches.Select(branch => $"you block {branch.You.Block} arrows {branch.You.Arrows}{(branch.Teammates.Count > 0 ? $" mate block {branch.Teammates[0].Block}" : "")}").Distinct().Order(StringComparer.Ordinal))}] ");
        return text.ToString();
    }

    /// <summary>A player with some of the arrow powers and relics, their values as the adapter reads them.</summary>
    private static void Player(Random random, SimState state, int id)
    {
        var player = state.MutablePlayer(id);
        player.Hp = random.Next(5, 60);
        player.MaxHp = 70;
        player.Block = random.Next(3) == 0 ? random.Next(1, 10) : 0;
        player.Arrows = random.Next(0, 3);
        if (random.Next(2) == 0) player.Powers = player.Powers.With(PowerId.ExplosiveArrows, amount: random.Next(1, 6));
        if (random.Next(3) == 0) player.Powers = player.Powers.With(PowerId.CoveringFire, amount: random.Next(1, 6));
        if (random.Next(4) == 0) player.Powers = player.Powers.With(PowerId.Yah, amount: 1);
        if (random.Next(3) == 0)
        {
            state.Vars[$"{id}:{RelicId.Twine.Name}:Block"] = 2;
            state.Vars[$"{id}:{RelicId.Twine.Name}:Block.Props"] = (int)SimProps.Unpowered;
            player.Relics = player.Relics.With(RelicId.Twine, amount: 0, data: (int)RelicUse.Armed);
        }
    }
}
