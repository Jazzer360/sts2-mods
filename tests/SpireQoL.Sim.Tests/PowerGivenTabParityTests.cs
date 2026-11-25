using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A power's amount as it's given (Hook.ModifyPowerAmountGiven: Snecko Skull's Poison added, Unsettling Lamp's debuffs doubled) in a card's play:
/// 200 generated fights, you (and sometimes a teammate) with Snecko Skull, Unsettling Lamp, both or neither, in either order; enemies, some with
/// Artifact or poisoned; a card that poisons one enemy, every enemy or a random one (some more than once), or puts Weak or Vulnerable on its target,
/// yours or a teammate's, each simulated against ending the turn now and read into its tabs by the rules the game shows them by (CardTabs: every
/// tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the play to), with each enemy's HP and poison and
/// its Weak and Vulnerable at the end of the turn, over the outcomes. Recorded with the hand-written models (Cases/power-given-tab-parity.txt,
/// SPIREQOL_RECORD_POWER_GIVEN_TABS=path writes it): read from their code, they show the same, but where their code places it otherwise
/// (ReviewedTabs: each a fix, with the game's line).
/// </summary>
public class PowerGivenTabParityTests
{
    private const int Scenarios = 200;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "power-given-tab-parity.txt");

    /// <summary>Scenarios whose tabs or numbers a class read from its code places otherwise, each a fix: the scenario, and the game's line that moves them.</summary>
    private static readonly Dictionary<int, string> ReviewedTabs = new();

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_POWER_GIVEN_TABS") is { Length: > 0 } recordTo)
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
    public void TheScenariosShowTheAmountsChanged()
    {
        // The recording is only a proof where the relics act: Unsettling Lamp's tab shows, and some enemy ends with more poison than without Snecko Skull.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        Assert.Contains(lines, line => line.Contains($"proc:{RelicId.UnsettlingLamp.Name} ", StringComparison.Ordinal));
        var more = Enumerable.Range(0, Scenarios).Count(scenario =>
        {
            var (state, play) = Scenario(scenario);
            var (without, _) = Scenario(scenario);
            foreach (var player in without.PlayerIds) without.MutablePlayer(player).Relics = without.Player(player)!.Relics.Without(RelicId.SneckoSkull);
            int Poison(SimState start) => SimEngine.Run(start, play).Branches.Sum(branch => branch.Enemies.Sum(enemy => enemy.Powers[PowerId.Poison]));
            return Poison(state) != Poison(without);
        });
        Assert.True(more > 30, $"only {more} scenarios end otherwise without Snecko Skull");
    }

    /// <summary>One generated fight and its play.</summary>
    internal static (SimState State, SimPlay Play) Scenario(int index)
    {
        var random = new Random(9100 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(10, 40), Hits = new List<int> { random.Next(1, 8) }, IntendsToAttack = true };
            if (random.Next(5) == 0) enemy.Powers = enemy.Powers.With(PowerId.Artifact, 1);
            if (random.Next(4) == 0) enemy.Powers = enemy.Powers.With(PowerId.Poison, random.Next(1, 6));
            state.Enemies.Add(enemy);
        }
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = 3;
        if (random.Next(3) == 0) state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2, Energy = 2 });
        foreach (var player in state.PlayerIds)
        {
            var who = state.MutablePlayer(player);
            // Each relic or none, in either order (the listeners' order); Unsettling Lamp armed (unused this combat) or not, Snecko Skull at its Poison value.
            var relics = new List<RelicId>();
            if (random.Next(3) != 0) relics.Add(RelicId.SneckoSkull);
            if (random.Next(2) == 0) relics.Add(RelicId.UnsettlingLamp);
            if (random.Next(2) == 0) relics.Reverse();
            foreach (var relic in relics)
                who.Relics = relic == RelicId.SneckoSkull ? who.Relics.With(relic, 1) : who.Relics.With(relic, 0, random.Next(3) != 0 ? (int)RelicUse.Armed : 0);
        }
        var owner = state.Teammates.Count > 0 && random.Next(4) == 0 ? Who.Teammate(0) : Who.You;
        var target = random.Next(enemies);
        var play = new SimPlay { Name = "Played", TargetId = target, Card = new SimCardPlay(new SimCard("Played", SimCardType.Skill) { Energy = 1 }, owner, target, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        switch (random.Next(4))
        {
            case 0:
                play.Poison = random.Next(2, 8);
                play.PoisonAll = random.Next(2) == 0;
                break;
            case 1:
                play.RandomPoison = random.Next(1, 4);
                play.RandomPoisonTimes = random.Next(1, 4);
                break;
            default:
                play.Body.Add(new ApplyPowerAction(random.Next(2) == 0 ? PowerId.Weak : PowerId.Vulnerable, Amount: random.Next(1, 3)) { Debuff = true });
                if (random.Next(2) == 0) play.Poison = random.Next(2, 6);
                break;
        }
        return (state, play);
    }

    /// <summary>One generated fight and play: its tabs, and each enemy's HP, poison, Weak and Vulnerable at the end of the turn, as text.</summary>
    internal static string Tabs(int index)
    {
        var (state, play) = Scenario(index);
        var outcome = SimEngine.Run(state, play);
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
        foreach (var player in state.PlayerIds)
            foreach (var relic in state.Player(player)!.Relics.Items) sources.Add(new TabSource(relic.Name.Name, relic.Name.Name, player, OutcomePass: 1));
        var tabs = new CardTabs { Indirect = index % 2 == 0, Chances = true, ProcNumbers = true };
        tabs.Add(sources, state, baseline, new[] { outcome });
        var text = new StringBuilder();
        foreach (var tab in tabs.Tabs.Values)
            text.Append($"{tab.Key} {(tab.Crossed ? "crossed" : "plain")}{(tab.Chance != null ? " chance " + tab.Chance : "")}{(tab.Number is { } number ? $" {tab.Counted} {number}" : "")}"
                + $" amount {tab.Amount} {tab.AuditMin}-{tab.AuditMax}{(tab.Aimed ? " aimed" : "")}; ");
        text.Append("| checked ");
        foreach (var (source, (every, any)) in tabs.FiresBy.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            text.Append($"{source} {string.Concat(every.Select(fires => fires ? '1' : '0'))}/{string.Concat(any.Select(fires => fires ? '1' : '0'))}; ");
        text.Append("| enemies ");
        string Range(Func<SimState, int> value) => outcome.Branches.Count == 0 ? "-" : $"{outcome.Branches.Min(value)}-{outcome.Branches.Max(value)}";
        foreach (var enemy in state.Enemies)
            text.Append($"{enemy.Id} hp {Range(branch => branch.Enemy(enemy.Id)?.Hp ?? 0)} poison {Range(branch => branch.Enemy(enemy.Id)?.Powers[PowerId.Poison] ?? 0)}"
                + $" weak {Range(branch => branch.Enemy(enemy.Id)?.Powers[PowerId.Weak] ?? 0)} vulnerable {Range(branch => branch.Enemy(enemy.Id)?.Powers[PowerId.Vulnerable] ?? 0)}; ");
        return text.ToString();
    }
}
