using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Consuming Shadow (its owner's last orb evoked, its amount of times, as their turn ends) in a card's play: 200 generated fights, you (and sometimes
/// a teammate) with Consuming Shadow at 1 to 3 or none, Lightning, Frost and Dark orbs or none; a card that channels an orb, attacks an enemy or
/// gains block, each simulated against ending the turn now and read into its tabs by the rules the game shows them by (CardTabs: every tab's key,
/// crossing, number, chance and aim marker, in order, and what the play check holds the play to), with each player's HP, the block as the enemies'
/// turn starts, each player's orbs left and each enemy's HP at the end of the turn, over the outcomes. Recorded with the hand-written model
/// (Cases/consuming-shadow-tab-parity.txt, SPIREQOL_RECORD_CONSUMING_SHADOW_TABS=path writes it): read from its code, it shows the same, but where
/// its code places it otherwise (ReviewedTabs: each a fix, with the game's line).
/// </summary>
public class ConsumingShadowTabParityTests
{
    private const int Scenarios = 200;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "consuming-shadow-tab-parity.txt");

    /// <summary>Scenarios whose tabs or numbers a class read from its code places otherwise, each a fix: the scenario, and the game's line that moves them.</summary>
    private static readonly Dictionary<int, string> ReviewedTabs = new();

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_CONSUMING_SHADOW_TABS") is { Length: > 0 } recordTo)
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
    public void TheScenariosShowItsEvokes()
    {
        // The recording is only a proof where Consuming Shadow acts: some fight ends otherwise without it.
        var otherwise = Enumerable.Range(0, Scenarios).Count(scenario =>
        {
            var (state, play) = Scenario(scenario);
            var (without, _) = Scenario(scenario);
            foreach (var player in without.PlayerIds) without.MutablePlayer(player).Powers = without.Player(player)!.Powers.Without(PowerId.ConsumingShadow);
            string Ends(SimState start) => string.Join(",", SimEngine.Run(start, play).Branches.Select(branch => $"{branch.You.Hp}/{branch.BlockAtEnemyTurn}/{branch.You.Orbs.Count}/{string.Join(".", branch.Enemies.Select(enemy => enemy.Hp))}"));
            return Ends(state) != Ends(without);
        });
        Assert.True(otherwise > 60, $"only {otherwise} scenarios end otherwise without Consuming Shadow");
    }

    private static SimOrb Orb(Random random) => random.Next(3) switch
    {
        0 => new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 },
        1 => new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 },
        _ => new SimOrb { Kind = OrbKind.Dark, Passive = 6, Evoke = random.Next(6, 20) },
    };

    /// <summary>One generated fight and its play.</summary>
    internal static (SimState State, SimPlay Play) Scenario(int index)
    {
        var random = new Random(9500 + index);
        var state = new SimState { TurnNumber = 2, RoundNumber = 2 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
            state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(8, 40), Hits = new List<int> { random.Next(3, 12) }, IntendsToAttack = random.Next(4) != 0 });
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = 3;
        if (random.Next(3) == 0) state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2, Energy = 2 });
        foreach (var player in state.PlayerIds)
        {
            var who = state.MutablePlayer(player);
            who.OrbCapacity = 3;
            who.Orbs = Enumerable.Range(0, random.Next(0, 4)).Select(_ => Orb(random)).ToList();
            if (random.Next(3) != 0) who.Powers = who.Powers.With(PowerId.ConsumingShadow, random.Next(1, 4));
        }
        var target = random.Next(enemies);
        var play = new SimPlay { Name = "Played", TargetId = target, Card = new SimCardPlay(new SimCard("Played", SimCardType.Skill) { Energy = 1 }, Who.You, target, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        switch (random.Next(3))
        {
            case 0:
                play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Orb(random) });
                break;
            case 1:
                play.IsAttack = true;
                play.HitTarget = SimPlay.Target.Single;
                play.HitDamage[target] = random.Next(4, 15);
                play.Hits = 1;
                play.Card = play.Card with { Card = new SimCard("Played", SimCardType.Attack) { Energy = 1 } };
                break;
            default:
                play.BlockGains = 1;
                play.BlockAmount = random.Next(3, 9);
                break;
        }
        return (state, play);
    }

    /// <summary>One generated fight and play: its tabs, each player's HP and orbs left, the block as the enemies' turn starts and each enemy's HP, as text.</summary>
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
        string Range(Func<SimState, int> value) => outcome.Branches.Count == 0 ? "-" : $"{outcome.Branches.Min(value)}-{outcome.Branches.Max(value)}";
        text.Append($"| branches {outcome.Branches.Count} | players ");
        foreach (var player in state.PlayerIds)
            text.Append($"{player} hp {Range(branch => branch.Player(player)?.Hp ?? 0)} orbs {Range(branch => branch.Player(player)?.Orbs.Count ?? 0)}; ");
        text.Append($"at the enemies' turn {Range(branch => branch.BlockAtEnemyTurn)} | enemies ");
        foreach (var enemy in state.Enemies) text.Append($"{enemy.Id} hp {Range(branch => branch.Enemy(enemy.Id)?.Hp ?? 0)}; ");
        return text.ToString();
    }
}
