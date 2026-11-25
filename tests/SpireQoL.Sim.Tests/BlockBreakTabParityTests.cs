using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What breaking an enemy's block sets off (Hand Drill's Vulnerable on it, Burrowed's stun of its owner and the power gone) in a card's play: 300
/// generated fights, enemies with block (some Burrowed, some Vulnerable or with Artifact), you and sometimes a teammate (some with Hand Drill), your
/// Osty sometimes; an Attack aimed at each enemy, at all of them or at a random one, of one to four hits (some by Osty), or a Skill that strips the
/// target's block (Expose's CreatureCmd.LoseBlock), each simulated against ending the turn now and read into its tabs by the rules the game shows
/// them by (CardTabs: every tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the play to), with each
/// enemy's HP and stun and each player's HP at the end of the turn, over the outcomes. Recorded with the hand-written models
/// (Cases/block-break-tab-parity.txt, SPIREQOL_RECORD_BLOCK_BREAK_TABS=path writes it): read from their code, they show the same tabs and the same
/// HP, but where their code places them otherwise (ReviewedTabs: each a fix, with the game's line).
/// </summary>
public class BlockBreakTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "block-break-tab-parity.txt");

    /// <summary>Scenarios whose tabs or HP a class read from its code places otherwise, each a fix: the scenario, and the game's line that moves them.</summary>
    private static readonly Dictionary<int, string> ReviewedTabs = new();

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_BLOCK_BREAK_TABS") is { Length: > 0 } recordTo)
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
    public void TheScenariosShowBlockBreaking()
    {
        // The recording is only a proof where the models act: Hand Drill's tab and Burrowed's tab show, aimed and not, and some enemy ends the turn
        // with less HP or stunned only because of them.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        Assert.Contains(lines, line => line.Contains($"proc:{RelicId.HandDrill.Name} ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains($"proc:{PowerId.Burrowed.Name} ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("aimed", StringComparison.Ordinal));
        var changes = Enumerable.Range(0, Scenarios).Count(scenario =>
        {
            var (state, play, _) = Scenario(scenario);
            var (without, _, _) = Scenario(scenario);
            foreach (var enemy in without.Enemies) enemy.Powers = enemy.Powers.Without(PowerId.Burrowed);
            foreach (var player in without.PlayerIds) without.MutablePlayer(player).Relics = without.Player(player)!.Relics.Without(RelicId.HandDrill);
            string End(SimState start) => string.Join(",", SimEngine.Run(start, play).Branches.Select(branch => string.Join("/", branch.Enemies.Select(enemy => $"{enemy.Hp}{(enemy.Stunned ? "s" : "")}"))));
            return End(state) != End(without);
        });
        Assert.True(changes > 50, $"only {changes} scenarios end otherwise without Hand Drill and Burrowed");
    }

    /// <summary>One generated fight and its play.</summary>
    internal static (SimState State, SimPlay Play, int Enemies) Scenario(int index)
    {
        var random = new Random(7300 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(5, 40), Block = random.Next(4) != 0 ? random.Next(1, 14) : 0,
                Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 12)).ToList() };
            enemy.IntendsToAttack = enemy.Hits.Count > 0;
            if (random.Next(3) == 0) enemy.Powers = enemy.Powers.With(PowerId.Burrowed, 1);
            if (random.Next(5) == 0) enemy.Powers = enemy.Powers.With(PowerId.Vulnerable, random.Next(1, 3));
            if (random.Next(6) == 0) enemy.Powers = enemy.Powers.With(PowerId.Artifact, 1);
            state.Enemies.Add(enemy);
        }
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = 3;
        if (random.Next(3) != 0) state.You.Relics = state.You.Relics.With(RelicId.HandDrill, 0);
        var byOsty = random.Next(5) == 0;
        if (byOsty || random.Next(4) == 0) state.You.WithOsty(hp: random.Next(1, 12));
        if (random.Next(3) == 0)
        {
            var teammate = new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2, Energy = 2 };
            if (random.Next(2) == 0) teammate.Relics = teammate.Relics.With(RelicId.HandDrill, 0);
            state.Teammates.Add(teammate);
        }
        SimPlay play;
        if (random.Next(6) == 0)
        {
            // A Skill that strips its target's block (Expose: CreatureCmd.LoseBlock of all of it).
            play = new SimPlay { Name = "Played", HitTarget = SimPlay.Target.Single, Card = new SimCardPlay(new SimCard("Played", SimCardType.Skill) { Energy = 1 }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            play.Body.Add(new StripBlockAction(RemoveArtifact: random.Next(2) == 0));
        }
        else
        {
            play = new SimPlay { Name = "Played", IsAttack = true, ByOsty = byOsty, Card = new SimCardPlay(new SimCard("Played", SimCardType.Attack) { Energy = 1 }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            play.HitTarget = random.Next(3) switch { 0 => SimPlay.Target.Single, 1 => SimPlay.Target.All, _ => SimPlay.Target.Random };
            play.Hits = random.Next(1, 5);
            for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 12);
        }
        return (state, play, enemies);
    }

    /// <summary>One generated fight and play: its tabs, and every creature's HP (an enemy's stun too) at the end of each aim's turn, as text.</summary>
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
        foreach (var enemy in state.Enemies)
            foreach (var power in enemy.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, enemy.Id, OutcomePass: 0));
        foreach (var player in state.PlayerIds)
            foreach (var relic in state.Player(player)!.Relics.Items) sources.Add(new TabSource(relic.Name.Name, relic.Name.Name, player, OutcomePass: 1));
        var tabs = new CardTabs { Indirect = index % 2 == 0, Chances = true, ProcNumbers = true };
        tabs.Add(sources, state, baseline, outcomes);
        var text = new StringBuilder();
        foreach (var tab in tabs.Tabs.Values)
            text.Append($"{tab.Key} {(tab.Crossed ? "crossed" : "plain")}{(tab.Chance != null ? " chance " + tab.Chance : "")}{(tab.Number is { } number ? $" {tab.Counted} {number}" : "")}"
                + $" amount {tab.Amount} {tab.AuditMin}-{tab.AuditMax}{(tab.Aimed ? " aimed" : "")}; ");
        text.Append("| checked ");
        foreach (var (source, (every, any)) in tabs.FiresBy.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            text.Append($"{source} {string.Concat(every.Select(fires => fires ? '1' : '0'))}/{string.Concat(any.Select(fires => fires ? '1' : '0'))}; ");
        // Every creature's HP at the end of the turn, least and most over each aim's outcomes, then over ending the turn now; an enemy's stun.
        text.Append("| hp ");
        string Range(SimOutcome outcome, Func<SimState, int> hp) => outcome.Branches.Count == 0 ? "-" : $"{outcome.Branches.Min(hp)}-{outcome.Branches.Max(hp)}";
        foreach (var enemy in state.Enemies)
            text.Append($"{enemy.Id} {string.Join(",", outcomes.Append(baseline).Select(outcome => Range(outcome, branch => branch.Enemy(enemy.Id)?.Hp ?? 0) + (outcome.StunnedInAll(enemy.Id) ? "s" : "")))}; ");
        foreach (var player in state.PlayerIds)
            text.Append($"{player} {string.Join(",", outcomes.Append(baseline).Select(outcome => Range(outcome, branch => branch.Player(player)!.Hp)))}; ");
        return text.ToString();
    }
}
