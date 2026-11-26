using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Corruption as a card's play shows it (its owner's Skills go to the exhaust pile once played, and cost 0): 200 generated fights, you (and sometimes
/// a teammate) with Corruption or not, beside what reacts to an exhaust (Feel No Pain's block, Dark Embrace's draw, a card's own exhaust energy) and
/// the other cost passes (Borrowed Time, Curious); sometimes Simpleton with a Strike and a Defend in your hand (its pick asks their costs), sometimes
/// a play that gives its owner Corruption. A card played (an Attack, a Skill, a Power, a Status, one that exhausts itself, twice played, yours or a
/// teammate's), each simulated against ending the turn now and read into its tabs by the rules the game shows them by (CardTabs: every tab's key,
/// crossing, number, chance and aim marker, in order, and what the play check holds the play to), with each player's exhaust pile, energy and the
/// cost of a probe card of each type over the outcomes. Recorded with the hand-written model (Cases/corruption-tab-parity.txt,
/// SPIREQOL_RECORD_CORRUPTION_TABS=path writes it): read from its code it shows the same, but where its code places it otherwise (ReviewedTabs: each a
/// fix, with the game's line).
/// </summary>
public class CorruptionTabParityTests
{
    private const int Scenarios = 200;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "corruption-tab-parity.txt");

    /// <summary>Scenarios whose tabs Corruption read from its code places otherwise, each a fix: the scenario, and the game's line that moves them.</summary>
    private static readonly Dictionary<int, string> ReviewedTabs = new();

    private static readonly SimCardType[] ProbeTypes = { SimCardType.Attack, SimCardType.Skill, SimCardType.Power };

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_CORRUPTION_TABS") is { Length: > 0 } recordTo)
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
    public void TheScenariosShowCorruptionAtWork()
    {
        // The recording is only a proof where Corruption acts: a Skill of its owner's exhausted by it (Feel No Pain's tab on a Skill that doesn't
        // exhaust itself), a Skill's cost made 0, and Simpleton's pick changed by it.
        var exhaustedByIt = 0;
        var madeFree = 0;
        for (var scenario = 0; scenario < Scenarios; scenario++)
        {
            var (state, play) = Scenario(scenario);
            var owner = play.Card!.Owner;
            var withIt = SimEngine.PowerOn(state, owner, PowerId.Corruption) > 0;
            var outcome = SimEngine.Run(state, play);
            if (withIt && play.Card.Card.Type == SimCardType.Skill && !play.Card.Card.ExhaustsSelf
                && outcome.Branches.All(branch => branch.Player(owner)!.ExhaustPile > state.Player(owner)!.ExhaustPile)) exhaustedByIt++;
            if (withIt && SimEngine.HandCost(state, owner, Probe(SimCardType.Skill, cost: 2)) == 0) madeFree++;
        }
        Assert.True(exhaustedByIt > 15, $"only {exhaustedByIt} scenarios exhaust a Skill by Corruption");
        Assert.True(madeFree > 30, $"only {madeFree} scenarios make a Skill free");
    }

    private static SimHandCard Probe(SimCardType type, int cost, int id = 90, params SimPlay[] plays) => new()
    {
        Id = id, Name = $"Probe{type}", IsAttack = type == SimCardType.Attack, Cost = cost, LocalCost = cost, Card = new SimCard($"Probe{type}", type), Plays = plays.ToList(),
    };

    /// <summary>One generated fight and its play.</summary>
    internal static (SimState State, SimPlay Play) Scenario(int index)
    {
        var random = new Random(8400 + index);
        var state = new SimState { TurnNumber = 1 };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = random.Next(2) == 0 ? 6 : 40, Hits = new List<int> { 5 }, IntendsToAttack = true });
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = 3;
        if (random.Next(3) == 0) state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2, Energy = 2 });
        foreach (var player in state.PlayerIds)
        {
            var who = state.MutablePlayer(player);
            if (random.Next(3) != 0) who.Powers = who.Powers.With(PowerId.Corruption, amount: 1);
            if (random.Next(2) == 0) who.Powers = who.Powers.With(PowerId.FeelNoPain, random.Next(1, 4));
            if (random.Next(3) == 0) who.Powers = who.Powers.With(PowerId.DarkEmbrace, amount: 1);
            if (random.Next(4) == 0) who.Powers = who.Powers.With(PowerId.BorrowedTime, amount: 1);
            if (random.Next(4) == 0) who.Powers = who.Powers.With(PowerId.Curious, amount: 1);
        }
        // Simpleton: the Strike costs 1, the Defend 2; its pick asks their costs as the turn ends.
        if (random.Next(4) == 0)
        {
            state.You.Simpleton = 1;
            state.You.AutoHand = new()
            {
                Probe(SimCardType.Attack, cost: 1, id: 11, new SimPlay
                {
                    Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 0, HitDamage = { [0] = 6 }, Hits = 1, HandCard = 11, HandDelta = -1,
                }),
                Probe(SimCardType.Skill, cost: 2, id: 12, new SimPlay { Name = "Defend", BlockAmount = 5, BlockGains = 1, HandCard = 12, HandDelta = -1 }),
            };
        }
        var type = random.Next(4) switch { 0 => SimCardType.Attack, 1 => SimCardType.Skill, 2 => SimCardType.Power, _ => SimCardType.Status };
        var owner = state.Teammates.Count > 0 && random.Next(4) == 0 ? Who.Teammate(0) : Who.You;
        var card = new SimCard("Played", type) { Energy = 1, ExhaustsSelf = random.Next(5) == 0, ExhaustEnergy = random.Next(5) == 0 ? 1 : 0 };
        var plays = random.Next(4) == 0 ? 2 : 1;
        var play = new SimPlay { Name = "Played", Plays = plays, HandDelta = -1, Card = new SimCardPlay(card, owner, Who.None, PlayIndex: 0, PlayCount: plays, AutoPlay: false) };
        // A Power that gives its owner Corruption.
        if (type == SimCardType.Power && random.Next(2) == 0) play.Body.Add(new ApplyPowerAction(PowerId.Corruption, Amount: 1) { To = ApplyPowerAction.Where.You });
        return (state, play);
    }

    /// <summary>One generated fight and play: its tabs, and each player's exhaust pile, energy and probe costs at the end of the turn, as text.</summary>
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
        string Over(Func<SimState, string> read) => string.Join(",", outcome.Branches.Select(read).Distinct().Order(StringComparer.Ordinal));
        foreach (var player in state.PlayerIds)
        {
            text.Append($"{player} exhausted {Over(branch => branch.Player(player)!.ExhaustPile.ToString())} energy {Over(branch => branch.Player(player)!.Energy.ToString())}");
            text.Append($" block {Over(branch => branch.Player(player)!.Block.ToString())} hand {Over(branch => branch.Player(player)!.HandCount.ToString())}");
            foreach (var type in ProbeTypes)
                text.Append($" {type} {SimEngine.HandCost(state, player, Probe(type, cost: 2))}>{Over(branch => SimEngine.HandCost(branch, player, Probe(type, cost: 2))?.ToString() ?? "?")}");
            text.Append("; ");
        }
        text.Append($"| enemy {Over(branch => branch.Enemies[0].Hp.ToString())}");
        return text.ToString();
    }
}
