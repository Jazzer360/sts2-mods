using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The tabs of the powers and relics read from their code in round seventeen whose tab is a proc of the play (Black Hole's damage, Burning Sticks'
/// copy), as a card's play shows them: 300 generated fights, you with Black Hole and Burning Sticks (used or not), a card that spends stars or gains
/// them, plays once or twice, and exhausts itself or not, each simulated against ending the turn now and read into its tabs by the rules the game
/// shows them by (CardTabs: every tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the play to).
/// Recorded with the hand-written models (Cases/proc-tab-parity.txt, SPIREQOL_RECORD_PROC_TABS=path writes it): a class read from its code must
/// show the same tabs.
/// </summary>
public class ProcTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "proc-tab-parity.txt");

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_PROC_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var expected = File.ReadAllLines(ParityPath);
        var changed = actual.Zip(expected).Where(pair => pair.First != pair.Second).Select(pair => $"{pair.Second}\n  now: {pair.First}").ToList();
        Assert.True(expected.Length == actual.Count && changed.Count == 0, $"the tabs changed in {changed.Count} of {actual.Count} scenarios:\n{string.Join("\n", changed.Take(6))}");
    }

    [Fact]
    public void TheScenariosShowEachTab()
    {
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        foreach (var name in new[] { PowerId.BlackHole.Name, RelicId.BurningSticks.Name })
            Assert.True(lines.Any(line => line.Contains($"proc:{name} ", StringComparison.Ordinal)), $"no scenario shows {name}'s tab");
    }

    /// <summary>One generated fight and play: its tabs, as text.</summary>
    private static string Tabs(int index)
    {
        var random = new Random(9500 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
            state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0, Hits = new List<int> { random.Next(1, 12) } });
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = random.Next(0, 6);
        if (random.Next(3) != 0) state.You.Powers = state.You.Powers.With(PowerId.BlackHole, amount: random.Next(1, 6));
        if (random.Next(3) != 0)
        {
            // Used this fight or not: the hand-written model's flag (RelicUse.Armed while unused), the read model's own WasUsedThisCombat.
            var used = random.Next(3) == 0;
            state.You.Relics = state.You.Relics.With(RelicId.BurningSticks, amount: 0, data: used ? 0 : (int)RelicUse.Armed);
            SimEngine.SetField(state, Who.You, RelicId.BurningSticks.Name + ".WasUsedThisCombat", used ? 1 : 0);
        }
        var type = random.Next(3) == 0 ? SimCardType.Attack : SimCardType.Skill;
        var card = new SimCard("Played", type) { Energy = random.Next(0, 3), Stars = random.Next(3) == 0 ? random.Next(1, 3) : 0, ExhaustsSelf = random.Next(2) == 0 };
        var play = new SimPlay { Name = "Played", IsAttack = type == SimCardType.Attack, Plays = random.Next(4) == 0 ? 2 : 1,
            Card = new SimCardPlay(card, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        if (play.IsAttack)
        {
            play.HitTarget = SimPlay.Target.All;
            play.Hits = 1;
            for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 10);
        }
        if (card.Stars > 0) play.Body.Insert(0, new CardEventAction(CardEvent.StarsSpent, Count: 1) { FirstPlayOnly = true });
        if (random.Next(3) == 0) play.Body.Add(new CardEventAction(CardEvent.StarsGained, Count: 1));
        // A card that exhausts itself: its exhaust in the play's After actions, as the adapter builds it; another card of yours exhausted by the play.
        if (card.ExhaustsSelf) play.After.Add(new CardEventAction(CardEvent.Exhausted, Count: 1) { Card = card });
        if (random.Next(4) == 0) play.Body.Add(new CardEventAction(CardEvent.Exhausted, Count: 1) { Card = new SimCard("Other", random.Next(2) == 0 ? SimCardType.Skill : SimCardType.Attack) });
        play.TargetId = play.IsAttack ? 0 : Who.None;
        var outcomes = new[] { SimEngine.Run(state, play) };
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
        foreach (var power in state.You.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, null, OutcomePass: 2));
        foreach (var relic in state.You.Relics.Items) sources.Add(new TabSource(relic.Name.Name, relic.Name.Name, null, OutcomePass: 1));
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
        // What the simulation ends with: the enemies' HP, your hand.
        text.Append($"| hp {string.Join(",", outcomes[0].Branches.Select(branch => string.Join("/", branch.Enemies.Select(enemy => enemy.Hp))).Distinct())} hand {string.Join(",", outcomes[0].Branches.Select(branch => branch.You.HandCount).Distinct())}");
        return text.ToString();
    }
}
