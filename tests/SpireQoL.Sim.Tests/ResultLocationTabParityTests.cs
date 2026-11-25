using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Where a played card goes as a card's play shows it (Rebound and Nostalgia: the top of your draw pile; Corruption's Skills: the exhaust pile;
/// Rebound's stack used): 200 generated fights, you (and sometimes a teammate) with Rebound, Nostalgia or both at one or two stacks, some cards played
/// already this turn; a card played (an Attack, a Skill, a Power, one that exhausts itself, twice played, yours or a teammate's), each simulated
/// against ending the turn now and read into its tabs by the rules the game shows them by (CardTabs: every tab's key, crossing, number, chance
/// and aim marker, in order, and what the play check holds the play to), with each player's Rebound and Nostalgia left at the end of the turn,
/// over the outcomes. Recorded with the hand-written models (Cases/result-location-tab-parity.txt, SPIREQOL_RECORD_RESULT_LOCATION_TABS=path
/// writes it): a class read from its code shows the same, but where its code places it otherwise (ReviewedTabs: each a fix, with the game's line).
/// </summary>
public class ResultLocationTabParityTests
{
    private const int Scenarios = 200;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "result-location-tab-parity.txt");

    /// <summary>Scenarios whose tabs a class read from its code places otherwise, each a fix: the scenario, and the game's line that moves them.</summary>
    private static readonly Dictionary<int, string> ReviewedTabs = new();

    private static readonly PowerId[] Powers = { PowerId.Rebound, PowerId.Nostalgia };

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_RESULT_LOCATION_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
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
    public void TheScenariosShowBothPowersAtWork()
    {
        // The recording is only a proof where the powers act: Nostalgia's tab shows, and some play uses a stack of Rebound.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        Assert.Contains(lines, line => line.Contains($"proc:{PowerId.Nostalgia.Name} ", StringComparison.Ordinal));
        var used = Enumerable.Range(0, Scenarios).Count(scenario =>
        {
            var (state, play) = Scenario(scenario);
            return SimEngine.Run(state, play).Branches.Any(branch => state.PlayerIds.Any(player => SimEngine.PowerOn(branch, player, PowerId.Rebound) < SimEngine.PowerOn(state, player, PowerId.Rebound)));
        });
        Assert.True(used > 20, $"only {used} scenarios use a stack of Rebound");
    }

    /// <summary>One generated fight and its play.</summary>
    internal static (SimState State, SimPlay Play) Scenario(int index)
    {
        var random = new Random(8100 + index);
        var state = new SimState { TurnNumber = 1 };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 40, Hits = new List<int> { 5 }, IntendsToAttack = true });
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = 3;
        if (random.Next(3) == 0) state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2, Energy = 2 });
        foreach (var player in state.PlayerIds)
        {
            var who = state.MutablePlayer(player);
            foreach (var power in Powers)
                if (random.Next(2) == 0) who.Powers = who.Powers.With(power, random.Next(1, 3));
            // Attacks and Skills already started this turn (Nostalgia's count of them).
            var started = random.Next(3);
            for (var i = 0; i < started; i++) who.Turn = SimKeys.CountPlay(who.Turn, PlayStage.Started, random.Next(2) == 0 ? SimCardType.Attack : SimCardType.Skill, new List<SimCardTag>());
        }
        var type = random.Next(4) switch { 0 => SimCardType.Attack, 1 => SimCardType.Skill, 2 => SimCardType.Power, _ => SimCardType.Status };
        var owner = state.Teammates.Count > 0 && random.Next(4) == 0 ? Who.Teammate(0) : Who.You;
        var card = new SimCard("Played", type) { Energy = 1, ExhaustsSelf = random.Next(5) == 0 };
        var plays = random.Next(4) == 0 ? 2 : 1;
        var play = new SimPlay { Name = "Played", Plays = plays, Card = new SimCardPlay(card, owner, Who.None, PlayIndex: 0, PlayCount: plays, AutoPlay: false) };
        return (state, play);
    }

    /// <summary>One generated fight and play: its tabs, and each player's Rebound and Nostalgia at the end of the turn, as text.</summary>
    internal static string Tabs(int index)
    {
        var (state, play) = Scenario(index);
        var outcome = SimEngine.Run(state, play);
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
        foreach (var player in state.PlayerIds)
            foreach (var power in state.Player(player)!.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, player, OutcomePass: 2));
        var tabs = new CardTabs { Indirect = index % 2 == 0, Chances = true, ProcNumbers = true };
        tabs.Add(sources, state, baseline, new[] { outcome });
        var text = new StringBuilder();
        foreach (var tab in tabs.Tabs.Values)
            text.Append($"{tab.Key} {(tab.Crossed ? "crossed" : "plain")}{(tab.Chance != null ? " chance " + tab.Chance : "")}{(tab.Number is { } number ? $" {tab.Counted} {number}" : "")}"
                + $" amount {tab.Amount} {tab.AuditMin}-{tab.AuditMax}{(tab.Aimed ? " aimed" : "")}; ");
        text.Append("| checked ");
        foreach (var (source, (every, any)) in tabs.FiresBy.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            text.Append($"{source} {string.Concat(every.Select(fires => fires ? '1' : '0'))}/{string.Concat(any.Select(fires => fires ? '1' : '0'))}; ");
        text.Append("| left ");
        foreach (var player in state.PlayerIds)
            foreach (var power in Powers)
                text.Append($"{player} {power.Name} {string.Join(",", outcome.Branches.Select(branch => SimEngine.PowerOn(branch, player, power)).Distinct().Order())}; ");
        return text.ToString();
    }
}
