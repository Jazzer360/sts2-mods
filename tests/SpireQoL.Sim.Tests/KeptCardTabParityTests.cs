using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The tabs of the relics that keep a card (round twenty-two: Pen Nib's 10th Attack, Vambrace's first block card), as a card's play shows them: 300
/// generated fights, you with either (Pen Nib at each count of Attacks played, sometimes with the card the game is playing kept; Vambrace unused, its
/// card kept or its bonus spent), an Attack, a Skill or a Power that gains block or not, played once or twice, each simulated against ending the
/// turn now and read into its tabs by the rules the game shows them by (CardTabs: every tab's key, crossing, number, chance and aim marker, in
/// order, and what the play check holds the play to). Recorded with the hand-written models (Cases/kept-card-tab-parity.txt,
/// SPIREQOL_RECORD_KEPT_CARD_TABS=path writes it): read from their code they must show the same tabs.
/// </summary>
public class KeptCardTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "kept-card-tab-parity.txt");

    private static readonly string[] Sources = { RelicId.PenNib.Name, RelicId.Vambrace.Name };

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_KEPT_CARD_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var expected = File.ReadAllLines(ParityPath);
        var changed = actual.Zip(expected).Where((pair, scenario) => pair.First != pair.Second && !ReviewedBlock.Contains(scenario)).Select(pair => $"{pair.Second}\n  now: {pair.First}").ToList();
        Assert.True(expected.Length == actual.Count && changed.Count == 0, $"the tabs changed in {changed.Count} of {actual.Count} scenarios:\n{string.Join("\n", changed.Take(6))}");
    }

    [Fact]
    public void TheScenariosShowEachTab()
    {
        // The recording is only a proof where the tabs show: every source has a tab in some scenario.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        foreach (var name in Sources)
            Assert.True(lines.Any(line => line.Contains($"proc:{name} ", StringComparison.Ordinal)), $"no scenario shows {name}'s tab");
    }

    /// <summary>
    /// Scenarios whose simulation ends otherwise, each a fix, its tabs as recorded: Vambrace.ModifyBlockMultiplicative's `TriggeringCard != null &amp;&amp;
    /// TriggeringCard != cardSource` → 1: with the card the game is playing kept (its block doubled, its play not over as the state was read), a card
    /// simulated here isn't it and its block isn't doubled, where the hand-written model doubled any card's block until the bonus was spent.
    /// </summary>
    private static readonly int[] ReviewedBlock = { 150, 240 };

    [Fact]
    public void TheReviewedScenariosKeepTheGamesCardWithVambrace()
    {
        var expected = File.ReadAllLines(ParityPath);
        foreach (var scenario in ReviewedBlock)
        {
            var (state, _, _) = Scenario(scenario);
            Assert.True(state.You.Relics.Has(RelicId.Vambrace) && state.You.Fields[RelicId.Vambrace.Name + ".TriggeringCard"] != 0 && state.You.Fields[RelicId.Vambrace.Name + ".BlockGainedThisCombat"] == 0,
                $"scenario {scenario}: Vambrace doesn't keep the game's card");
            Assert.Equal(expected[scenario].Split(" | hp ")[0], $"{scenario} {Tabs(scenario)}".Split(" | hp ")[0]);
        }
    }

    /// <summary>One generated fight and its play.</summary>
    private static (SimState State, SimPlay Play, int Enemies) Scenario(int index)
    {
        var random = new Random(10700 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
            state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0, Hits = new List<int> { random.Next(1, 12) } });
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = random.Next(0, 6);
        if (random.Next(2) == 0)
        {
            // Its Attacks played (the hand-written model's amount, the read model's AttacksPlayed), now and then with the game's card kept mid-play
            // (the hand-written model's Data 1, the read model's AttackToDouble kept outside any simulated card play).
            var attacks = random.Next(3) == 0 ? 9 : random.Next(0, 10);
            var keptByTheGame = random.Next(6) == 0;
            state.You.Relics = state.You.Relics.With(RelicId.PenNib, amount: attacks, data: keptByTheGame ? 1 : 0);
            KeptCardFields.PenNib(state, attacks, keptByTheGame);
        }
        if (random.Next(2) == 0)
        {
            // Unused, its card kept (its block doubled, the card's play not over), or its bonus spent: the hand-written model's Data bits, the read
            // model's TriggeringCard and BlockGainedThisCombat.
            var kept = random.Next(4) == 0;
            var spent = random.Next(3) == 0;
            state.You.Relics = state.You.Relics.With(RelicId.Vambrace, amount: 0, data: (kept ? 1 : 0) | (spent ? 2 : 0));
            KeptCardFields.Vambrace(state, kept, spent);
        }
        var type = random.Next(4) switch { 0 or 1 => SimCardType.Attack, 2 => SimCardType.Skill, _ => SimCardType.Power };
        var gainsBlock = random.Next(2) == 0;
        var card = new SimCard("Played", type) { Energy = random.Next(0, 3), GainsBlock = gainsBlock };
        var play = new SimPlay { Name = "Played", IsAttack = type == SimCardType.Attack, Plays = random.Next(4) == 0 ? 2 : 1,
            Card = new SimCardPlay(card, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        if (play.IsAttack)
        {
            var all = random.Next(2) == 0;
            play.Body.Add(ScriptBuilder.Attack(baseDamage: random.Next(1, 10), hits: random.Next(1, 3), mode: all ? SimPlay.Target.All : SimPlay.Target.Single));
            play.TargetId = all ? Who.None : random.Next(enemies);
        }
        if (gainsBlock) play.Body.Add(ScriptBuilder.Block(baseBlock: random.Next(3, 9)));
        return (state, play, enemies);
    }

    /// <summary>One generated fight and play: its tabs, as text.</summary>
    private static string Tabs(int index)
    {
        var (state, play, _) = Scenario(index);
        var outcomes = new[] { SimEngine.Run(state, play) };
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
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
        // What the simulation ends with: the enemies' HP and your block.
        text.Append($"| hp {string.Join(",", outcomes[0].Branches.Select(branch => string.Join("/", branch.Enemies.Select(enemy => enemy.Hp))).Distinct())}"
            + $" block {string.Join(",", outcomes[0].Branches.Select(branch => branch.You.Block).Distinct())}");
        return text.ToString();
    }
}

/// <summary>The read models' own fields for the state a hand-written relic model kept in its amount and Data (each set as the adapter reads the game's).</summary>
internal static class KeptCardFields
{
    /// <summary>What a card kept as the state was read is: there, and no simulated card play's identity (SimReaders.KeptOutside).</summary>
    private const int KeptOutside = int.MaxValue;

    /// <summary>Pen Nib's AttacksPlayed and AttackToDouble.</summary>
    public static void PenNib(SimState state, int attacks, bool keptByTheGame)
    {
        SimEngine.SetField(state, Who.You, RelicId.PenNib.Name + ".AttacksPlayed", attacks);
        SimEngine.SetField(state, Who.You, RelicId.PenNib.Name + ".AttackToDouble", keptByTheGame ? KeptOutside : 0);
    }

    /// <summary>Vambrace's TriggeringCard and BlockGainedThisCombat.</summary>
    public static void Vambrace(SimState state, bool kept, bool spent)
    {
        SimEngine.SetField(state, Who.You, RelicId.Vambrace.Name + ".TriggeringCard", kept ? KeptOutside : 0);
        SimEngine.SetField(state, Who.You, RelicId.Vambrace.Name + ".BlockGainedThisCombat", spent ? 1 : 0);
    }
}
