using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What Insatiable Dominion (its Damage, 2 plus the turn number, to its owner as their side's turn ends, late) does to a card's play: 200
/// generated fights, Insatiable Dominion on you or a teammate (sometimes both, sometimes with Plating, Buffer or block beside it), one to three
/// enemies attacking or not; a Skill that gives you block once or twice, or an Attack, aimed at each enemy, at all of them or at a random one, each simulated against ending the turn now and read
/// into its tabs by the rules the game shows them by (CardTabs: every tab's key, crossing, number, chance and aim marker, in order, and what the
/// play check holds the play to), with each player's block after the play and HP at the end of the turn, over the outcomes. Recorded with the
/// hand-written model (Cases/insatiable-dominion-tab-parity.txt, SPIREQOL_RECORD_INSATIABLE_DOMINION_TABS=path writes it): read from its code, it shows the same,
/// but where its code places it otherwise (ReviewedTabs: each a fix, with the game's line).
/// </summary>
public class InsatiableDominionTabParityTests
{
    private const int Scenarios = 200;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "insatiable-dominion-tab-parity.txt");

    /// <summary>Scenarios a class read from its code places otherwise, each a fix: the scenario, and the game's line that moves them.</summary>
    private static readonly Dictionary<int, string> ReviewedTabs = new();

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_INSATIABLE_DOMINION_TABS") is { Length: > 0 } recordTo)
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
    public void TheScenariosShowInsatiableDominionAtWork()
    {
        // The recording is only a proof where Insatiable Dominion hurts: some play leaves a player otherwise than it would without it.
        var hurt = Enumerable.Range(0, Scenarios).Count(scenario =>
        {
            var (state, play, _) = Scenario(scenario);
            var (without, _, _) = Scenario(scenario);
            foreach (var player in without.PlayerIds) without.MutablePlayer(player).Powers = without.Player(player)!.Powers.Without(PowerId.InsatiableDominion);
            int Block(SimState start) => SimEngine.Run(start, play).Branches.Sum(branch => branch.PlayerIds.Sum(player => branch.Player(player)!.Hp * 1000 + branch.Player(player)!.Block));
            return Block(state) != Block(without);
        });
        Assert.True(hurt > 40, $"only {hurt} scenarios end otherwise without Insatiable Dominion");
    }

    /// <summary>One generated fight and its play.</summary>
    internal static (SimState State, SimPlay Play, int Enemies) Scenario(int index)
    {
        var random = new Random(8600 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(5, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0,
                Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 15)).ToList() };
            enemy.IntendsToAttack = enemy.Hits.Count > 0;
            state.Enemies.Add(enemy);
        }
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = 3;
        if (random.Next(3) == 0) state.You.Block = random.Next(1, 10);
        var teammate = random.Next(3) == 0;
        if (teammate) state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2, Energy = 2 });
        var on = teammate ? random.Next(3) : 0;
        foreach (var player in state.PlayerIds.Where((_, at) => on == 2 || at == on).ToList())
        {
            var mutable = state.MutablePlayer(player);
            // Its Damage as the game sets it (2 plus the turn number: InsatiableDominionPower.RefreshDamage), seeded as the adapter reads it.
            mutable.Powers = mutable.Powers.With(PowerId.InsatiableDominion, amount: 1);
            state.Vars[$"{player}:{PowerId.InsatiableDominion.Name}:Damage"] = 2 + random.Next(1, 8);
            if (random.Next(3) == 0) mutable.Powers = mutable.Powers.With(PowerId.Plating, random.Next(1, 6));
            if (random.Next(4) == 0) mutable.Powers = mutable.Powers.With(PowerId.Buffer, amount: 1);
        }
        SimPlay play;
        if (random.Next(3) != 0)
        {
            play = new SimPlay { Name = "Played", Card = new SimCardPlay(new SimCard("Played", SimCardType.Skill) { Energy = 1, GainsBlock = true }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            // Its block through the block math on each gain (CreatureCmd.GainBlock), once or twice: block Insatiable Dominion meets as the turn ends.
            play.Body.Add(new GainBlockAction(Times: random.Next(1, 3), Base: random.Next(1, 15), SimProps.Move));
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

    /// <summary>One generated fight and play: its tabs, and each player's block after the play and HP at the end of each aim's turn, as text.</summary>
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
            foreach (var power in state.Player(player)!.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, player, OutcomePass: 0));
        var tabs = new CardTabs { Indirect = index % 2 == 0, Chances = true, ProcNumbers = true };
        tabs.Add(sources, state, baseline, outcomes);
        var text = new StringBuilder();
        foreach (var tab in tabs.Tabs.Values)
            text.Append($"{tab.Key} {(tab.Crossed ? "crossed" : "plain")}{(tab.Chance != null ? " chance " + tab.Chance : "")}{(tab.Number is { } number ? $" {tab.Counted} {number}" : "")}"
                + $" amount {tab.Amount} {tab.AuditMin}-{tab.AuditMax}{(tab.Aimed ? " aimed" : "")}; ");
        text.Append("| checked ");
        foreach (var (source, (every, any)) in tabs.FiresBy.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            text.Append($"{source} {string.Concat(every.Select(fires => fires ? '1' : '0'))}/{string.Concat(any.Select(fires => fires ? '1' : '0'))}; ");
        // Each player's HP at the end of the turn (least and most) for each aim's play and then ending the turn now, and every creature's HP.
        text.Append("| players ");
        string Range(SimOutcome outcome, Func<SimState, int> value) => outcome.Branches.Count == 0 ? "-" : $"{outcome.Branches.Min(value)}-{outcome.Branches.Max(value)}";
        foreach (var player in state.PlayerIds)
            text.Append($"{player} hp {string.Join(",", outcomes.Append(baseline).Select(outcome => Range(outcome, branch => branch.Player(player)!.Hp)))}; ");
        text.Append("| enemies ");
        foreach (var enemy in state.Enemies)
            text.Append($"{enemy.Id} {string.Join(",", outcomes.Select(outcome => Range(outcome, branch => branch.Enemy(enemy.Id)?.Hp ?? 0)))}; ");
        return text.ToString();
    }
}
