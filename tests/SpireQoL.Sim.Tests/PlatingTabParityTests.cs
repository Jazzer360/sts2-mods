using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Plating (its block as its owner's turn ends, a stack less as its owner's next turn starts, an enemy's by the players' count) in a card's play:
/// 200 generated fights, you (and sometimes a teammate) with Plating or none, enemies with Plating or none, Juggernaut beside it sometimes; a card
/// that gives you Plating, block or neither, or attacks an enemy, each simulated against ending the turn now and read into its tabs by the rules the
/// game shows them by (CardTabs: every tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the play to),
/// with each player's HP and block as the enemies' turn starts and each enemy's HP, block and Plating at the end of the turn, over the outcomes.
/// Recorded with the hand-written model (Cases/plating-tab-parity.txt, SPIREQOL_RECORD_PLATING_TABS=path writes it): read from its code, it shows
/// the same, but where its code places it otherwise (ReviewedTabs: each a fix, with the game's line).
/// </summary>
public class PlatingTabParityTests
{
    private const int Scenarios = 200;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "plating-tab-parity.txt");

    /// <summary>Scenarios whose tabs or numbers a class read from its code places otherwise, each a fix: the scenario, and the game's line that moves them.</summary>
    private static readonly Dictionary<int, string> ReviewedTabs = new();

    /// <summary>
    /// The recorded line with each living enemy's Plating at the turn's end one less (none below 0) where it's past the fight's first round: a fix,
    /// reviewed by rule (decision #289). PlatingPower.AfterSideTurnStart: `participants.Contains(base.Owner) &amp;&amp; (base.Owner.Player == null || ...)
    /// &amp;&amp; (base.Owner.Side != CombatSide.Enemy || combatState.RoundNumber != 1)` → PowerCmd.ModifyAmount(this, -DynamicVars["Decrement"].BaseValue),
    /// as the enemies' turn starts, its Decrement 1 here (the players' count it's set to as it's applied, the canonical 1 on these boards), where the
    /// hand-written model kept it. Nothing a turn shows reads an enemy's Plating after its turn starts (its block comes as its own turn ends).
    /// </summary>
    private static string EnemyPlatingDown(int scenario, string recorded)
    {
        var (state, _) = Scenario(scenario);
        if (state.RoundNumber == 1) return recorded;
        return System.Text.RegularExpressions.Regex.Replace(recorded, @"(\d+) hp (\d+)-(\d+) block (\d+)-(\d+) plating (\d+)-(\d+);", match =>
        {
            int Part(int group) => int.Parse(match.Groups[group].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (Part(3) == 0) return match.Value;
            return $"{match.Groups[1].Value} hp {match.Groups[2].Value}-{match.Groups[3].Value} block {match.Groups[4].Value}-{match.Groups[5].Value} plating {Math.Max(0, Part(6) - 1)}-{Math.Max(0, Part(7) - 1)};";
        });
    }

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_PLATING_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var expected = File.ReadAllLines(ParityPath).ToList();
        Assert.Equal(expected.Count, actual.Count);
        var changed = actual.Zip(expected, (now, was) => (Now: now, Was: was)).Select((pair, scenario) => (pair.Now, pair.Was, Scenario: scenario))
            .Where(entry => entry.Now != entry.Was && !ReviewedTabs.ContainsKey(entry.Scenario) && EnemyPlatingDown(entry.Scenario, entry.Was) != entry.Now).Select(entry => $"{entry.Was}\n  now: {entry.Now}").ToList();
        Assert.True(changed.Count == 0, $"the tabs changed in {changed.Count} of {actual.Count} scenarios:\n{string.Join("\n", changed.Take(6))}");
        var stale = ReviewedTabs.Keys.Where(scenario => actual[scenario] == expected[scenario]).ToList();
        Assert.True(stale.Count == 0, $"reviewed, but the tabs are as recorded: {string.Join(", ", stale)}");
    }

    [Fact]
    public void TheScenariosShowPlatingsBlock()
    {
        // The recording is only a proof where Plating acts: some player's block as the enemies' turn starts is more than without it.
        var more = Enumerable.Range(0, Scenarios).Count(scenario =>
        {
            var (state, play) = Scenario(scenario);
            var (without, _) = Scenario(scenario);
            foreach (var player in without.PlayerIds) without.MutablePlayer(player).Powers = without.Player(player)!.Powers.Without(PowerId.Plating);
            play.AddPlating = 0;
            int Block(SimState start, SimPlay played) => SimEngine.Run(start, played).Branches.Sum(branch => branch.BlockAtEnemyTurn);
            return Block(state, Scenario(scenario).Play) != Block(without, play);
        });
        Assert.True(more > 60, $"only {more} scenarios end otherwise without Plating");
    }

    /// <summary>One generated fight and its play.</summary>
    internal static (SimState State, SimPlay Play) Scenario(int index)
    {
        var random = new Random(9300 + index);
        var state = new SimState { TurnNumber = 1 + random.Next(3) };
        state.RoundNumber = state.TurnNumber;
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(10, 40), Hits = new List<int> { random.Next(3, 12) }, IntendsToAttack = random.Next(4) != 0 };
            if (random.Next(3) == 0) enemy.Powers = enemy.Powers.With(PowerId.Plating, random.Next(1, 8));
            if (random.Next(3) == 0) enemy.Block = random.Next(1, 10);
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
            if (random.Next(2) == 0) who.Powers = who.Powers.With(PowerId.Plating, random.Next(1, 9));
            if (random.Next(4) == 0) who.Powers = who.Powers.With(PowerId.Juggernaut, random.Next(1, 5));
            if (random.Next(3) == 0) who.Block = random.Next(1, 8);
        }
        var target = random.Next(enemies);
        var play = new SimPlay { Name = "Played", TargetId = target, Card = new SimCardPlay(new SimCard("Played", SimCardType.Skill) { Energy = 1 }, Who.You, target, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        switch (random.Next(4))
        {
            case 0:
                play.AddPlating = random.Next(2, 7);
                break;
            case 1:
                play.BlockGains = 1;
                play.BlockAmount = random.Next(3, 9);
                break;
            case 2:
                play.IsAttack = true;
                play.HitTarget = SimPlay.Target.Single;
                play.HitDamage[target] = random.Next(4, 15);
                play.Hits = 1;
                play.Card = play.Card with { Card = new SimCard("Played", SimCardType.Attack) { Energy = 1 } };
                break;
        }
        return (state, play);
    }

    /// <summary>One generated fight and play: its tabs, each player's HP and block, and each enemy's HP, block and Plating at the end of the turn, as text.</summary>
    internal static string Tabs(int index)
    {
        var (state, play) = Scenario(index);
        var outcome = SimEngine.Run(state, play);
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
        foreach (var player in state.PlayerIds)
            foreach (var power in state.Player(player)!.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, player, OutcomePass: 2));
        foreach (var enemy in state.Enemies)
            foreach (var power in enemy.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, enemy.Id, OutcomePass: 0));
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
        text.Append("| players ");
        foreach (var player in state.PlayerIds)
            text.Append($"{player} hp {Range(branch => branch.Player(player)?.Hp ?? 0)} block {Range(branch => branch.Player(player)?.Block ?? 0)}; ");
        text.Append($"at the enemies' turn {Range(branch => branch.BlockAtEnemyTurn)} | enemies ");
        foreach (var enemy in state.Enemies)
            text.Append($"{enemy.Id} hp {Range(branch => branch.Enemy(enemy.Id)?.Hp ?? 0)} block {Range(branch => branch.Enemy(enemy.Id)?.Block ?? 0)} plating {Range(branch => branch.Enemy(enemy.Id)?.Powers[PowerId.Plating] ?? 0)}; ");
        return text.ToString();
    }
}
