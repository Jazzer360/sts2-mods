using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The tabs of the models that make cards into a draw pile (Personal Hive's Dazed on a powered hit, Biiig Hug's Soot on a shuffle), as a card's
/// play shows them: 300 generated fights, enemies with Personal Hive (one to three a hit), your Biiig Hug, a draw pile of zero to two cards and
/// a discard pile, an Attack of one to three hits aimed at one enemy, all of them or a random one, and draws that can empty the pile and shuffle,
/// each simulated against ending the turn now and read into its tabs by the rules the game shows them by (CardTabs: every tab's key, crossing,
/// number, chance and aim marker, in order, and what the play check holds the play to). Recorded with the hand-written models
/// (Cases/made-card-tab-parity.txt, SPIREQOL_RECORD_MADE_CARD_TABS=path writes it): read from their code they must show the same tabs.
/// </summary>
public class MadeCardTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "made-card-tab-parity.txt");

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_MADE_CARD_TABS") is { Length: > 0 } recordTo)
        {
            ParityRecording.Write(recordTo, actual, ParityPath, (recorded, now) => Reviewed(recorded) == now);
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
        Assert.True(lines.Any(line => line.Contains($"proc:{PowerId.PersonalHive.Name} ", StringComparison.Ordinal)), "no scenario shows Personal Hive's tab");
        // Biiig Hug has no tab (no settings row: decision #263), and its Soot is still made in some scenario.
        Assert.DoesNotContain(lines, line => line.Contains($"proc:{RelicId.BiiigHug.Name} ", StringComparison.Ordinal));
        Assert.Contains(lines, line => System.Text.RegularExpressions.Regex.IsMatch(line, @"\| piles .*[1-9]s"));
    }

    /// <summary>
    /// The recording as reviewed: what the play check holds a play to (FiresBy) for Personal Hive is its code's flashes (decision #199), and
    /// PersonalHivePower.AfterDamageReceived never calls Flash(): none expected, where the hand-written model expected one wherever it acted. The
    /// in-game check never asked it (ProcAudit only expects a flash of a power whose code flashes: CodeIndex.FlashesOutsideAmountHooks). Its tab
    /// is unchanged. Biiig Hug's tab is gone with its settings row (decision #263, the owner's call), and with it what the play check holds it to;
    /// its Soot is made as before (the piles).
    /// </summary>
    private static string Reviewed(string line)
    {
        line = System.Text.RegularExpressions.Regex.Replace(line, @"(\| checked [^|]*?PersonalHivePower )([01]+)/([01]+)",
            match => match.Groups[1].Value + new string('0', match.Groups[2].Length) + "/" + new string('0', match.Groups[3].Length));
        line = System.Text.RegularExpressions.Regex.Replace(line, @"proc:BiiigHug [^;]*; ", "");
        return System.Text.RegularExpressions.Regex.Replace(line, @"(?<=\| checked [^|]*)BiiigHug [01]+/[01]+; ", "");
    }

    /// <summary>One generated fight and play: its tabs, as text.</summary>
    private static string Tabs(int index)
    {
        var random = new Random(9700 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0, Hits = new List<int> { random.Next(1, 12) } };
            if (random.Next(3) != 0) enemy.Powers = enemy.Powers.With(PowerId.PersonalHive, amount: random.Next(1, 4));
            state.Enemies.Add(enemy);
        }
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = random.Next(0, 6);
        if (random.Next(2) == 0) state.You.Relics = state.You.Relics.With(RelicId.BiiigHug, amount: 0, data: 1);
        state.You.Draw = new SimDrawPile
        {
            Rest = { new SimDrawGroup(new SimDrawCard("plain"), random.Next(0, 3)) },
            Discard = { new SimDrawGroup(new SimDrawCard("old"), random.Next(1, 4)) },
        };
        state.You.MadeCards = new Dictionary<string, SimDrawCard> { [CardId.Dazed.Name] = new SimDrawCard("dazed"), [CardId.Soot.Name] = new SimDrawCard("soot") };
        var play = new SimPlay { Name = "Played", IsAttack = true, Card = new SimCardPlay(new SimCard("Played", SimCardType.Attack) { Energy = 1 }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        play.HitTarget = random.Next(3) switch { 0 => SimPlay.Target.Single, 1 => SimPlay.Target.All, _ => SimPlay.Target.Random };
        play.Hits = random.Next(1, 4);
        for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 12);
        var draws = random.Next(0, 4);
        if (draws > 0) play.Body.Add(new CardEventAction(CardEvent.Drawn, Count: draws));
        // One simulation per aim: each enemy for a card aimed at one, else one.
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
        foreach (var source in sources.Select(source => (source.Model, Owner: source.Owner ?? Who.You)).Distinct())
            if (SimModels.Get(source.Model) is { } model)
                text.Append($"{source.Model}@{source.Owner} {model.Seen} {(model.HarmfulFor(source.Owner) ? "crossed" : "plain")}; ");
        // What the simulations end with: the made cards in your piles, in every branch.
        text.Append("| piles ");
        foreach (var outcome in outcomes)
            text.Append(string.Join(",", outcome.Branches.Select(branch => $"{Count(branch, "dazed")}d{Count(branch, "soot")}s").Distinct()) + "; ");
        return text.ToString();
    }

    private static int Count(SimState branch, string key) => branch.You.Draw is not { } pile ? 0
        : pile.Top.Count(card => card.Key == key) + pile.Rest.Where(group => group.Card.Key == key).Sum(group => group.Count) + pile.Discard.Where(group => group.Card.Key == key).Sum(group => group.Count);
}
