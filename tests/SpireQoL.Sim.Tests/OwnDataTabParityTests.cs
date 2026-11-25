using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The tabs of the models that keep a card or a play in their own data (round twenty-one: Serpent Form's and Calamity's amount bound to the card
/// played, Juggling's third Attack, Stretch's and Pael's Legion's play whose block they change), as a card's play shows them: 300 generated
/// fights, you with any of them (Juggling at each count of Attacks this turn, Pael's Legion on cooldown or not), an Attack, a Skill or a Power that
/// gains block or not, played once or twice, each simulated against ending the turn now and read into its tabs by the rules the game shows them by
/// (CardTabs: every tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the play to). Recorded with the
/// hand-written models (Cases/own-data-tab-parity.txt, SPIREQOL_RECORD_OWN_DATA_TABS=path writes it): read from their code they must show the
/// same tabs.
/// </summary>
public class OwnDataTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "own-data-tab-parity.txt");

    private static readonly string[] Sources = { PowerId.SerpentForm.Name, PowerId.Calamity.Name, PowerId.Juggling.Name, PowerId.Stretch.Name, RelicId.PaelsLegion.Name };

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_OWN_DATA_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var expected = File.ReadAllLines(ParityPath).Select(Reviewed).ToArray();
        var changed = actual.Zip(expected).Where(pair => pair.First != pair.Second).Select(pair => $"{pair.Second}\n  now: {pair.First}").ToList();
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
    /// The recording as reviewed: what the play check holds a play to (FiresBy) for a read model is its code's flashes (decision #199), and
    /// neither SerpentFormPower.AfterCardPlayed (its Vfx's OnEffectTriggered shows it) nor CalamityPower.AfterCardPlayed calls Flash(): none
    /// expected, where the hand-written models expected one wherever they acted. Their tabs are unchanged.
    /// </summary>
    private static string Reviewed(string line) => System.Text.RegularExpressions.Regex.Replace(line, @"(?<=\| checked [^|]*)(SerpentFormPower|CalamityPower) ([01]+)/([01]+)",
        match => match.Groups[1].Value + " " + new string('0', match.Groups[2].Length) + "/" + new string('0', match.Groups[3].Length));

    /// <summary>One generated fight and play: its tabs, as text.</summary>
    private static string Tabs(int index)
    {
        var random = new Random(10100 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
            state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0, Hits = new List<int> { random.Next(1, 12) } });
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = random.Next(0, 6);
        if (random.Next(3) == 0) state.You.Powers = state.You.Powers.With(PowerId.SerpentForm, amount: random.Next(1, 5));
        if (random.Next(3) == 0) state.You.Powers = state.You.Powers.With(PowerId.Calamity, amount: random.Next(1, 3));
        if (random.Next(3) == 0)
        {
            // Its Attacks this turn so far: the hand-written model's Data, the read model's own data's field.
            var attacks = random.Next(0, 4);
            state.You.Powers = state.You.Powers.With(PowerId.Juggling, amount: random.Next(1, 3), data: attacks);
            OwnDataFields.Juggling(state, attacks);
        }
        if (random.Next(3) == 0) state.You.Powers = state.You.Powers.With(PowerId.Stretch, amount: random.Next(1, 6));
        if (random.Next(3) == 0)
        {
            // Its cooldown: the hand-written model's amount, the read model's own field.
            var cooldown = random.Next(3) == 0 ? random.Next(1, 3) : 0;
            state.You.Relics = state.You.Relics.With(RelicId.PaelsLegion, amount: cooldown);
            OwnDataFields.PaelsLegion(state, cooldown);
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
        // What the simulation ends with: the enemies' HP, your block and hand.
        text.Append($"| hp {string.Join(",", outcomes[0].Branches.Select(branch => string.Join("/", branch.Enemies.Select(enemy => enemy.Hp))).Distinct())}"
            + $" block {string.Join(",", outcomes[0].Branches.Select(branch => branch.You.Block).Distinct())} hand {string.Join(",", outcomes[0].Branches.Select(branch => branch.You.HandCount).Distinct())}");
        return text.ToString();
    }
}

/// <summary>The read models' own fields for the state a hand-written model kept in its Data or amount (each set as the adapter reads the game's).</summary>
internal static class OwnDataFields
{
    /// <summary>Juggling's Attacks this turn (its own data's attacksPlayedThisTurn).</summary>
    public static void Juggling(SimState state, int attacks) => SimEngine.SetField(state, Who.You, PowerId.Juggling.Name + ".Data.attacksPlayedThisTurn", attacks);

    /// <summary>Pael's Legion's cooldown (its Cooldown).</summary>
    public static void PaelsLegion(SimState state, int cooldown) { }
}
