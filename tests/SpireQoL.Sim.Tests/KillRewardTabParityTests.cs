using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The tabs of Compost (energy for every living player when an enemy dies), as a card's play shows them: 200 generated fights, Compost on you or a
/// teammate (sometimes both), one to three enemies (some low on HP, some with block), an Attack aimed at each enemy, at all of them or at a random
/// one, or a Skill that gives you block, each simulated against ending the turn now and read into its tabs by the rules the game shows them by
/// (CardTabs: every tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the play to), with each player's
/// energy at the end of the play. Recorded with the hand-written model (Cases/kill-reward-tab-parity.txt, SPIREQOL_RECORD_KILL_REWARD_TABS=path
/// writes it): read from its code, it shows the same tabs, but where its code places them otherwise (ReviewedTabs: each a fix, with the game's line).
/// </summary>
public class KillRewardTabParityTests
{
    private const int Scenarios = 200;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "kill-reward-tab-parity.txt");

    /// <summary>Scenarios whose tabs a class read from its code places otherwise, each a fix: the scenario, and the game's line that moves them.</summary>
    private static readonly Dictionary<int, string> ReviewedTabs = new();

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_KILL_REWARD_TABS") is { Length: > 0 } recordTo)
        {
            ParityRecording.Write(recordTo, actual, ParityPath);
            return;
        }
        var expected = File.ReadAllLines(ParityPath).ToList();
        Assert.Equal(expected.Count, actual.Count);
        var changed = actual.Zip(expected, (now, was) => (Now: now, Was: was)).Select((pair, scenario) => (pair.Now, pair.Was, Scenario: scenario))
            .Where(entry => entry.Now != entry.Was && !ReviewedTabs.ContainsKey(entry.Scenario)).Select(entry => $"{entry.Was}\n  now: {entry.Now}").ToList();
        Assert.True(changed.Count == 0, $"the tabs changed in {changed.Count} of {actual.Count} scenarios:\n{string.Join("\n", changed.Take(6))}");
        var stale = ReviewedTabs.Keys.Where(scenario => actual[scenario] == expected[scenario]).ToList();
        Assert.True(stale.Count == 0, $"reviewed, but the tabs are as recorded: {string.Join(", ", stale)}");
    }

    [Fact]
    public void TheScenariosShowTheTab()
    {
        // The recording is only a proof where the tab shows: Compost's tab is in some scenario, on you and on a teammate, aimed and not.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        Assert.Contains(lines, line => line.Contains($"proc:{PowerId.Compost.Name} ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains($"proc:{PowerId.Compost.Name}@", StringComparison.Ordinal) || line.Contains("seen CompostPower@-", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("aimed", StringComparison.Ordinal));
    }

    /// <summary>One generated fight and its play.</summary>
    internal static (SimState State, SimPlay Play, int Enemies) Scenario(int index)
    {
        var random = new Random(7300 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 30), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0,
                Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 12)).ToList() };
            enemy.IntendsToAttack = enemy.Hits.Count > 0;
            state.Enemies.Add(enemy);
        }
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = random.Next(0, 4);
        state.You.HandCount = 3;
        var teammate = random.Next(2) == 0;
        if (teammate) state.Teammates.Add(new SimPlayer { Hp = random.Next(3) == 0 ? 0 : 40, MaxHp = 60, HandCount = 2, Energy = random.Next(0, 3) });
        var amount = random.Next(1, 3);
        var on = teammate ? random.Next(3) : 0;
        if (on != 1) state.You.Powers = state.You.Powers.With(PowerId.Compost, amount);
        if (on != 0 && teammate) state.Teammates[0].Powers = state.Teammates[0].Powers.With(PowerId.Compost, amount);
        if (teammate && state.Teammates[0].Hp == 0) state.Teammates[0].Died = true;
        SimPlay play;
        if (random.Next(5) == 0)
        {
            play = new SimPlay { Name = "Played", Card = new SimCardPlay(new SimCard("Played", SimCardType.Skill) { Energy = 1, GainsBlock = true }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            play.BlockAmount = random.Next(1, 15);
            play.BlockGains = 1;
        }
        else
        {
            play = new SimPlay { Name = "Played", IsAttack = true, Card = new SimCardPlay(new SimCard("Played", SimCardType.Attack) { Energy = 1 }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            play.HitTarget = random.Next(3) switch { 0 => SimPlay.Target.Single, 1 => SimPlay.Target.All, _ => SimPlay.Target.Random };
            play.Hits = random.Next(1, 4);
            for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 20);
        }
        return (state, play, enemies);
    }

    /// <summary>One generated fight and play: its tabs, and each player's energy at the end of each aim's play, as text.</summary>
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
        foreach (var player in state.PlayerIds)
            if (state.Player(player)!.Powers.Has(PowerId.Compost)) sources.Add(new TabSource(PowerId.Compost.Name, PowerId.Compost.Name, player, OutcomePass: 0));
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
                text.Append($"{source.Model}@{source.Owner} {(model.HarmfulFor(source.Owner) ? "crossed" : "plain")}; ");
        // Each player's energy after the play, least and most over the outcomes (Compost's energy lands as the kill's hit is over).
        text.Append("| energy ");
        foreach (var player in state.PlayerIds)
            text.Append($"{player} {string.Join(",", outcomes.Select(outcome => outcome.Branches.Count == 0 ? "-" : $"{outcome.Branches.Min(branch => branch.Player(player)!.Energy)}-{outcome.Branches.Max(branch => branch.Player(player)!.Energy)}"))}; ");
        return text.ToString();
    }
}
